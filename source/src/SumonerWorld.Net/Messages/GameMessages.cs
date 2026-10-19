using System.Numerics;
using SumonerWorld.Core.Intents;

namespace SumonerWorld.Net.Messages;

/// <summary>
/// Client → server, unreliable (TDD §3.2): the newest inputs, oldest first, ending at the newest sequence. Every batch repeats
/// up to <see cref="MaxInputs"/> recent inputs, so a lost datagram costs nothing as long as one of the next few arrives.
/// </summary>
public static class InputBatch
{
    /// <summary>Most inputs per batch (≈ 270 ms at 30 Hz).</summary>
    public const int MaxInputs = 8;

    /// <summary>
    /// Writes header and body. <paramref name="inputs"/> must be 1–8 consecutive inputs, oldest first; their sequences start at 1.
    /// </summary>
    public static void Write(ref MessageWriter writer, scoped ReadOnlySpan<PlayerInput> inputs)
    {
        if (inputs.Length is 0 or > MaxInputs)
        {
            throw new ArgumentOutOfRangeException(nameof(inputs), inputs.Length, $"A batch holds 1–{MaxInputs} inputs.");
        }

        Protocol.WriteHeader(ref writer, MessageType.InputBatch);
        writer.WriteVarUInt(inputs[^1].Sequence);
        writer.WriteByte((byte)inputs.Length);
        for (int i = 0; i < inputs.Length; i++)
        {
            PlayerInput input = inputs[i];
            if (input.Sequence != inputs[^1].Sequence - (uint)(inputs.Length - 1 - i) || input.Sequence == 0)
            {
                throw new ArgumentException("Batch inputs must be consecutive and start at sequence 1 or later.", nameof(inputs));
            }

            if (Math.Abs((int)input.MoveX) > PlayerInput.MaxAxis || Math.Abs((int)input.MoveZ) > PlayerInput.MaxAxis)
            {
                throw new ArgumentOutOfRangeException(nameof(inputs), input, $"Movement axes must be within ±{PlayerInput.MaxAxis}.");
            }

            writer.WriteVarInt(input.MoveX);
            writer.WriteVarInt(input.MoveZ);
            writer.WriteByte(input.Buttons);
        }
    }

    /// <summary>Reads the body (after the header) into <paramref name="destination"/> (at least 8 long); returns the count.</summary>
    public static int Read(ref MessageReader reader, Span<PlayerInput> destination)
    {
        if (destination.Length < MaxInputs)
        {
            throw new ArgumentException($"Destination must hold {MaxInputs} inputs.", nameof(destination));
        }

        ulong last = reader.ReadVarUInt();
        int count = reader.ReadByte();
        if (count is 0 or > MaxInputs || last > uint.MaxValue || last < (ulong)count)
        {
            throw new MalformedMessageException($"Input batch of {count} ending at {last} is out of range.");
        }

        uint first = (uint)last - (uint)count + 1;
        for (int i = 0; i < count; i++)
        {
            destination[i] = new PlayerInput(
                first + (uint)i,
                (short)reader.ReadVarInt(-PlayerInput.MaxAxis, PlayerInput.MaxAxis),
                (short)reader.ReadVarInt(-PlayerInput.MaxAxis, PlayerInput.MaxAxis),
                reader.ReadByte());
        }

        reader.EnsureEnd();
        return count;
    }
}

/// <summary>Intent kinds on the wire. Values are part of the protocol: never reuse or renumber them.</summary>
public enum IntentKind : byte
{
    /// <summary>Offer a Pact: target = creature stable id, argument = Pact Terms mask.</summary>
    OfferPact = 1,
}

/// <summary>Client → server, reliable: a state-changing intent (TDD §3.2, §3.4). The server pairs the sequence with the session.</summary>
/// <param name="Sequence">Increases by one per intent, starting at 1.</param>
/// <param name="Kind">What the player wants.</param>
/// <param name="Target">The target's id (kind-specific).</param>
/// <param name="Argument">Kind-specific argument.</param>
public readonly record struct IntentRequest(uint Sequence, IntentKind Kind, ulong Target, byte Argument);

/// <summary>Server → client, reliable: the result of an intent (a resent intent gets its first result again).</summary>
/// <param name="Sequence">The intent's sequence.</param>
/// <param name="Result">Applied or rejected, with reason and detail.</param>
public readonly record struct IntentAck(uint Sequence, IntentResult Result);

/// <summary>Codec for <see cref="IntentRequest"/>.</summary>
public sealed class IntentRequestCodec : IMessageCodec<IntentRequest>
{
    /// <inheritdoc />
    public MessageType Type => MessageType.Intent;

    /// <inheritdoc />
    public void Write(ref MessageWriter writer, in IntentRequest message)
    {
        Protocol.WriteHeader(ref writer, Type);
        writer.WriteVarUInt(message.Sequence);
        writer.WriteByte((byte)message.Kind);
        writer.WriteVarUInt(message.Target);
        writer.WriteByte(message.Argument);
    }

    /// <inheritdoc />
    public IntentRequest Read(ref MessageReader reader)
    {
        ulong sequence = reader.ReadVarUInt();
        var kind = (IntentKind)reader.ReadByte();
        if (sequence is 0 or > uint.MaxValue || !Enum.IsDefined(kind))
        {
            throw new MalformedMessageException($"Intent {sequence} of kind {(byte)kind} is out of range.");
        }

        var message = new IntentRequest((uint)sequence, kind, reader.ReadVarUInt(), reader.ReadByte());
        reader.EnsureEnd();
        return message;
    }
}

/// <summary>Codec for <see cref="IntentAck"/>.</summary>
public sealed class IntentAckCodec : IMessageCodec<IntentAck>
{
    /// <inheritdoc />
    public MessageType Type => MessageType.IntentAck;

    /// <inheritdoc />
    public void Write(ref MessageWriter writer, in IntentAck message)
    {
        Protocol.WriteHeader(ref writer, Type);
        writer.WriteVarUInt(message.Sequence);
        writer.WriteByte((byte)message.Result.Status);
        writer.WriteByte((byte)message.Result.Reason);
        writer.WriteByte(message.Result.Detail);
    }

    /// <inheritdoc />
    public IntentAck Read(ref MessageReader reader)
    {
        ulong sequence = reader.ReadVarUInt();
        var status = (IntentStatus)reader.ReadByte();
        var reason = (IntentRejectReason)reader.ReadByte();
        if (sequence > uint.MaxValue || !Enum.IsDefined(status) || !Enum.IsDefined(reason))
        {
            throw new MalformedMessageException($"Intent ack {sequence} ({(byte)status}, {(byte)reason}) is out of range.");
        }

        var message = new IntentAck((uint)sequence, new IntentResult(status, reason, reader.ReadByte()));
        reader.EnsureEnd();
        return message;
    }
}

/// <summary>What a snapshot entity is.</summary>
public enum SnapshotEntityKind : byte
{
    /// <summary>A creature; the id is its stable id.</summary>
    Creature = 1,

    /// <summary>Another player's avatar; the id is the account id.</summary>
    Player = 2,
}

/// <summary>One part's header: the tick, the receiver's own avatar (exact, for reconciliation) and how many entities follow.</summary>
/// <param name="ServerTick">Tick the state is from.</param>
/// <param name="AckedInput">Last input sequence of the receiver the server has applied.</param>
/// <param name="OwnPosition">The receiver's avatar position after that input (full precision).</param>
/// <param name="EntityCount">Entities in this part.</param>
public readonly record struct SnapshotHeader(ulong ServerTick, uint AckedInput, Vector3 OwnPosition, int EntityCount);

/// <summary>One entity in a snapshot, quantized: position to 1 cm, facing to 1/256 of a turn.</summary>
/// <param name="Kind">Creature or player.</param>
/// <param name="Id">Stable id or account id.</param>
/// <param name="Position">Position (rounded to 1 cm).</param>
/// <param name="Facing">Heading in 1/256 turns (0 = +Z).</param>
/// <param name="Emote">Emote id shown above it (0 = none).</param>
public readonly record struct SnapshotEntity(SnapshotEntityKind Kind, ulong Id, Vector3 Position, byte Facing, byte Emote);

/// <summary>
/// Server → client, unreliable, 20 Hz (TDD §3.2): entities within the interest radius. A tick's state can span several parts, each
/// with the full header and at most <see cref="MaxEntitiesPerPart"/> entities. Delta compression is a later bandwidth task (M1-18).
/// </summary>
public static class Snapshot
{
    /// <summary>Largest encoded entity, in bytes.</summary>
    public const int MaxEntityBytes = 1 + 10 + (3 * 5) + 1 + 1;

    /// <summary>Most entities per part.</summary>
    public const int MaxEntitiesPerPart = 64;

    private const float CentimetresPerMetre = 100f;

    /// <summary>Byte offset of the entity count (patched by <see cref="SetEntityCount"/>).</summary>
    public static int WriteHeader(ref MessageWriter writer, ulong serverTick, uint ackedInput, Vector3 ownPosition)
    {
        Protocol.WriteHeader(ref writer, MessageType.Snapshot);
        writer.WriteVarUInt(serverTick);
        writer.WriteVarUInt(ackedInput);
        writer.WriteSingle(ownPosition.X);
        writer.WriteSingle(ownPosition.Y);
        writer.WriteSingle(ownPosition.Z);
        writer.WriteByte(0);
        return writer.Written - 1;
    }

    /// <summary>Sets the entity count written as a placeholder by <see cref="WriteHeader"/>.</summary>
    public static void SetEntityCount(Span<byte> buffer, int countOffset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxEntitiesPerPart);
        buffer[countOffset] = (byte)count;
    }

    /// <summary>Writes one entity.</summary>
    public static void WriteEntity(ref MessageWriter writer, in SnapshotEntity entity)
    {
        writer.WriteByte((byte)entity.Kind);
        writer.WriteVarUInt(entity.Id);
        writer.WriteVarInt(Quantize(entity.Position.X));
        writer.WriteVarInt(Quantize(entity.Position.Y));
        writer.WriteVarInt(Quantize(entity.Position.Z));
        writer.WriteByte(entity.Facing);
        writer.WriteByte(entity.Emote);
    }

    /// <summary>Reads the header (after the message header); <see cref="ReadEntity"/> must then be called EntityCount times.</summary>
    public static SnapshotHeader ReadHeader(ref MessageReader reader)
    {
        ulong tick = reader.ReadVarUInt();
        ulong acked = reader.ReadVarUInt();
        if (acked > uint.MaxValue)
        {
            throw new MalformedMessageException($"Acked input {acked} is out of range.");
        }

        var own = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        int count = reader.ReadByte();
        return count > MaxEntitiesPerPart
            ? throw new MalformedMessageException($"Snapshot part with {count} entities (max {MaxEntitiesPerPart}).")
            : new SnapshotHeader(tick, (uint)acked, own, count);
    }

    /// <summary>Reads one entity.</summary>
    public static SnapshotEntity ReadEntity(ref MessageReader reader)
    {
        var kind = (SnapshotEntityKind)reader.ReadByte();
        if (!Enum.IsDefined(kind))
        {
            throw new MalformedMessageException($"Unknown snapshot entity kind {(byte)kind}.");
        }

        ulong id = reader.ReadVarUInt();
        var position = new Vector3(Dequantize(ref reader), Dequantize(ref reader), Dequantize(ref reader));
        return new SnapshotEntity(kind, id, position, reader.ReadByte(), reader.ReadByte());
    }

    /// <summary>Heading of a direction in 1/256 turns (0 = +Z, clockwise seen from above).</summary>
    public static byte EncodeFacing(Vector3 direction) =>
        (byte)((int)MathF.Round(MathF.Atan2(direction.X, direction.Z) / (2 * MathF.PI) * 256) & 0xFF);

    private static long Quantize(float metres) => (long)MathF.Round(metres * CentimetresPerMetre);

    private static float Dequantize(ref MessageReader reader) => reader.ReadVarInt(-int.MaxValue, int.MaxValue) / CentimetresPerMetre;
}
