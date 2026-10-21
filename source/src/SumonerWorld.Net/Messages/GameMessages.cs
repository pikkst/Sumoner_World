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

    /// <summary>Feed a creature: target = stable id (since protocol 6).</summary>
    Feed = 2,

    /// <summary>Attack a creature with the basic move, aimed at it: target = stable id (since protocol 7, no detail).</summary>
    Hit = 3,

    /// <summary>Free a snared creature: target = stable id.</summary>
    Help = 4,

    /// <summary>Shout at nearby predators: no target; detail = predators scared.</summary>
    Scare = 5,

    /// <summary>
    /// Start one of the player's moves (since protocol 7): target = aim heading in 1/256 turns (0 = +Z, as snapshot facing),
    /// argument = move slot (0 basic, 1 heavy).
    /// </summary>
    Attack = 6,

    /// <summary>
    /// Call a pacted creature with a summoning sigil (since protocol 9): target = stable id. A refusal carries a <c>SummonRefusal</c>
    /// as detail.
    /// </summary>
    Summon = 7,

    /// <summary>Send a summoned creature back (since protocol 9): target = stable id.</summary>
    Dismiss = 8,

    /// <summary>Pick up one's own loot bag (since protocol 16): target = bag id; detail = gold picked up.</summary>
    PickUp = 12,

    /// <summary>Gather from a node (since protocol 16): target = node id; detail = how many were gathered.</summary>
    Gather = 13,

    /// <summary>
    /// Cast a spell (since protocol 14): argument = index in the spells data; target = aim heading in 1/256 turns (bits 0–7),
    /// burst distance in decimetres (bits 8–23) and the ward's creature stable id (bits 24 and up, 0 = self).
    /// </summary>
    Cast = 11,

    /// <summary>
    /// Teach a pacted creature a skill (since protocol 13): target = stable id, argument = node index in its species' tree; a
    /// refusal carries the node's <c>SkillState</c> as detail.
    /// </summary>
    LearnSkill = 10,

    /// <summary>
    /// Command every summoned companion (since protocol 10): argument = <c>CompanionCommand</c>, target = the creature to attack
    /// (Attack) or 0; detail = companions doing it (low 4 bits) and refusing (high 4 bits).
    /// </summary>
    Command = 9,
}

/// <summary>One of the receiver's pacted creatures.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Species">Species index.</param>
/// <param name="Summoned">At the player's side.</param>
/// <param name="Level">Its level (since protocol 11).</param>
/// <param name="Stage">Its life stage (<c>LifeStage</c>, since protocol 11).</param>
/// <param name="BondLevel">Its bond level (since protocol 13).</param>
/// <param name="Points">Skill points it can spend (since protocol 13).</param>
/// <param name="Learned">Learned skill bits (since protocol 13).</param>
/// <param name="Affinity">How much it likes each branch of its tree, in branch order (since protocol 13).</param>
/// <param name="Combos">Learned combo bits (since protocol 15).</param>
public readonly record struct CompanionEntry(
    ulong Id,
    byte Species,
    bool Summoned,
    byte Level = 1,
    byte Stage = 0,
    byte BondLevel = 0,
    byte Points = 0,
    ulong Learned = 0,
    Affinities Affinity = default,
    byte Combos = 0);

/// <summary>Up to four branch affinities (−128…127), in branch order.</summary>
/// <param name="Count">How many branches.</param>
/// <param name="A">First.</param>
/// <param name="B">Second.</param>
/// <param name="C">Third.</param>
/// <param name="D">Fourth.</param>
public readonly record struct Affinities(byte Count, sbyte A, sbyte B, sbyte C, sbyte D)
{
    /// <summary>The affinity of branch <paramref name="index"/>.</summary>
    public sbyte this[int index] => index switch
    {
        0 => A,
        1 => B,
        2 => C,
        _ => D,
    };

    /// <summary>Builds from values (clamped to a signed byte).</summary>
    public static Affinities Of(ReadOnlySpan<int> values)
    {
        static sbyte At(ReadOnlySpan<int> v, int i) => i < v.Length ? (sbyte)Math.Clamp(v[i], sbyte.MinValue, sbyte.MaxValue) : (sbyte)0;
        return new Affinities((byte)Math.Min(values.Length, 4), At(values, 0), At(values, 1), At(values, 2), At(values, 3));
    }
}

/// <summary>Server → client, reliable (since protocol 9): the receiver's pacted creatures, oldest Pact first by id.</summary>
/// <param name="Entries">At most <see cref="MaxEntries"/>.</param>
public readonly record struct CompanionList(IReadOnlyList<CompanionEntry> Entries)
{
    /// <summary>Most companions sent.</summary>
    public const int MaxEntries = 32;
}

/// <summary>One inventory slot on the wire.</summary>
/// <param name="Item">Index in the items data both sides load.</param>
/// <param name="Count">How many.</param>
public readonly record struct InventoryEntry(int Item, int Count);

/// <summary>Server → client, reliable (since protocol 16): the receiver's gold and non-empty slots, in slot order.</summary>
/// <param name="Gold">Gold.</param>
/// <param name="Entries">At most <see cref="MaxEntries"/>.</param>
public readonly record struct InventoryList(int Gold, IReadOnlyList<InventoryEntry> Entries)
{
    /// <summary>Most slots sent.</summary>
    public const int MaxEntries = 24;
}

/// <summary>Codec for <see cref="InventoryList"/>.</summary>
public sealed class InventoryListCodec : IMessageCodec<InventoryList>
{
    /// <inheritdoc />
    public MessageType Type => MessageType.InventoryList;

    /// <inheritdoc />
    public void Write(ref MessageWriter writer, in InventoryList message)
    {
        ArgumentNullException.ThrowIfNull(message.Entries);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(message.Entries.Count, InventoryList.MaxEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(message.Gold);
        Protocol.WriteHeader(ref writer, Type);
        writer.WriteVarUInt((ulong)message.Gold);
        writer.WriteByte((byte)message.Entries.Count);
        foreach (InventoryEntry entry in message.Entries)
        {
            writer.WriteVarUInt((ulong)entry.Item);
            writer.WriteVarUInt((ulong)entry.Count);
        }
    }

    /// <inheritdoc />
    public InventoryList Read(ref MessageReader reader)
    {
        ulong gold = reader.ReadVarUInt();
        int count = reader.ReadByte();
        if (gold > int.MaxValue || count > InventoryList.MaxEntries)
        {
            throw new MalformedMessageException($"Inventory with {gold} gold and {count} slots is out of range.");
        }

        var entries = new InventoryEntry[count];
        for (int i = 0; i < count; i++)
        {
            ulong item = reader.ReadVarUInt();
            ulong number = reader.ReadVarUInt();
            if (item > ushort.MaxValue || number is 0 or > ushort.MaxValue)
            {
                throw new MalformedMessageException($"Inventory slot {i} ({item} x{number}) is out of range.");
            }

            entries[i] = new InventoryEntry((int)item, (int)number);
        }

        reader.EnsureEnd();
        return new InventoryList((int)gold, entries);
    }
}

/// <summary>Codec for <see cref="CompanionList"/>.</summary>
public sealed class CompanionListCodec : IMessageCodec<CompanionList>
{
    /// <inheritdoc />
    public MessageType Type => MessageType.CompanionList;

    /// <inheritdoc />
    public void Write(ref MessageWriter writer, in CompanionList message)
    {
        ArgumentNullException.ThrowIfNull(message.Entries);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(message.Entries.Count, CompanionList.MaxEntries);
        Protocol.WriteHeader(ref writer, Type);
        writer.WriteByte((byte)message.Entries.Count);
        foreach (CompanionEntry entry in message.Entries)
        {
            writer.WriteVarUInt(entry.Id);
            writer.WriteByte(entry.Species);
            writer.WriteByte(entry.Summoned ? (byte)1 : (byte)0);
            writer.WriteByte(entry.Level);
            writer.WriteByte(entry.Stage);
            writer.WriteByte(entry.BondLevel);
            writer.WriteByte(entry.Points);
            writer.WriteVarUInt(entry.Learned);
            writer.WriteByte(entry.Affinity.Count);
            for (int a = 0; a < entry.Affinity.Count; a++)
            {
                writer.WriteByte(unchecked((byte)entry.Affinity[a]));
            }

            writer.WriteByte(entry.Combos);
        }
    }

    /// <inheritdoc />
    public CompanionList Read(ref MessageReader reader)
    {
        int count = reader.ReadByte();
        if (count > CompanionList.MaxEntries)
        {
            throw new MalformedMessageException($"Companion list of {count} (max {CompanionList.MaxEntries}).");
        }

        var entries = new CompanionEntry[count];
        Span<int> affinity = stackalloc int[4];
        for (int i = 0; i < count; i++)
        {
            ulong id = reader.ReadVarUInt();
            byte species = reader.ReadByte();
            byte summoned = reader.ReadByte();
            if (summoned > 1)
            {
                throw new MalformedMessageException($"Companion {id} has summoned flag {summoned}.");
            }

            byte level = reader.ReadByte();
            byte stage = reader.ReadByte();
            byte bondLevel = reader.ReadByte();
            byte points = reader.ReadByte();
            ulong learned = reader.ReadVarUInt();
            int branches = reader.ReadByte();
            if (branches > 4)
            {
                throw new MalformedMessageException($"Companion {id} has {branches} branch affinities (max 4).");
            }

            for (int b = 0; b < branches; b++)
            {
                affinity[b] = unchecked((sbyte)reader.ReadByte());
            }

            entries[i] = new CompanionEntry(id, species, summoned == 1, level, stage, bondLevel, points, learned, Affinities.Of(affinity[..branches]), reader.ReadByte());
        }

        reader.EnsureEnd();
        return new CompanionList(entries);
    }
}

/// <summary>Bits of <see cref="PlayerInput.Buttons"/> (since protocol 7). Values are part of the protocol.</summary>
public static class PlayerButtons
{
    /// <summary>Guard held up (combat spec §5).</summary>
    public const byte Block = 1;

    /// <summary>Dodge pressed (the server reacts to it turning on).</summary>
    public const byte Dodge = 2;
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

    /// <summary>The receiver's own loot bag (only its owner gets it, since protocol 16); the id is the bag's id.</summary>
    Loot = 3,

    /// <summary>A gathering node (since protocol 16): species = node kind index, health 255 when it can be gathered, 0 when not.</summary>
    Node = 4,
}

/// <summary>The receiver's own vitals as fractions (0–255 of full; never 0 while above 0) and combat action (since protocol 7).</summary>
/// <param name="Health">Health.</param>
/// <param name="Stamina">Stamina.</param>
/// <param name="Mana">Mana.</param>
/// <param name="Action">What they are doing (<c>CombatAction</c>).</param>
/// <param name="Level">Their level (since protocol 12).</param>
/// <param name="Rank">Their Summoner Rank (<c>Rank</c>, since protocol 12).</param>
/// <param name="Class">Their class (<c>SummonerClass</c>, since protocol 12).</param>
/// <param name="Xp">Progress to the next level, 0–255 (255 at the rank's cap).</param>
/// <param name="Status">Status effect bits (<c>StatusMarks</c>, since protocol 14).</param>
public readonly record struct SnapshotVitals(
    byte Health, byte Stamina, byte Mana, byte Action, byte Level = 1, byte Rank = 0, byte Class = 0, byte Xp = 0, byte Status = 0);

/// <summary>One part's header: the tick, the receiver's own avatar (exact, for reconciliation) and how many entities follow.</summary>
/// <param name="ServerTick">Tick the state is from.</param>
/// <param name="AckedInput">Last input sequence of the receiver the server has applied.</param>
/// <param name="OwnPosition">The receiver's avatar position after that input (full precision).</param>
/// <param name="EntityCount">Entities in this part.</param>
/// <param name="Own">The receiver's own vitals.</param>
public readonly record struct SnapshotHeader(ulong ServerTick, uint AckedInput, Vector3 OwnPosition, int EntityCount, SnapshotVitals Own = default);

/// <summary>One entity in a snapshot, quantized: position to 1 cm, facing to 1/256 of a turn.</summary>
/// <param name="Kind">Creature or player.</param>
/// <param name="Id">Stable id or account id.</param>
/// <param name="Position">Position (rounded to 1 cm).</param>
/// <param name="Facing">Heading in 1/256 turns (0 = +Z).</param>
/// <param name="Emote">Emote it shows (0 = none; AI Bible §6.2).</param>
/// <param name="Species">Creatures: index in the species data both sides load (picks the model); players: 0. Since protocol 4.</param>
/// <param name="Health">Health as a fraction, 255 = full (since protocol 7).</param>
/// <param name="Action">What it is doing (<c>CombatAction</c>, since protocol 7).</param>
/// <param name="Move">The move in progress: index in the move data + 1; 0 = none (since protocol 7).</param>
/// <param name="Stage">Creatures: life stage (<c>LifeStage</c>, since protocol 11).</param>
/// <param name="Level">Creatures: level, 0 for players (since protocol 11).</param>
/// <param name="Status">Status effects as bits (<c>StatusMarks</c>, since protocol 14).</param>
public readonly record struct SnapshotEntity(
    SnapshotEntityKind Kind, ulong Id, Vector3 Position, byte Facing, byte Emote, byte Species = 0, byte Health = 255, byte Action = 0, byte Move = 0,
    byte Stage = 0, byte Level = 0, byte Status = 0);

/// <summary>
/// Server → client, unreliable, 20 Hz (TDD §3.2): entities within the interest radius. A tick's state can span several parts, each
/// with the full header and at most <see cref="MaxEntitiesPerPart"/> entities. Delta compression is a later bandwidth task (M1-18).
/// </summary>
public static class Snapshot
{
    /// <summary>Largest encoded entity, in bytes.</summary>
    public const int MaxEntityBytes = 1 + 10 + (3 * 5) + 1 + 1 + 1 + 3 + 3;

    /// <summary>Most entities per part.</summary>
    public const int MaxEntitiesPerPart = 64;

    private const float CentimetresPerMetre = 100f;

    /// <summary>Byte offset of the entity count (patched by <see cref="SetEntityCount"/>).</summary>
    public static int WriteHeader(ref MessageWriter writer, ulong serverTick, uint ackedInput, Vector3 ownPosition, SnapshotVitals own = default)
    {
        Protocol.WriteHeader(ref writer, MessageType.Snapshot);
        writer.WriteVarUInt(serverTick);
        writer.WriteVarUInt(ackedInput);
        writer.WriteSingle(ownPosition.X);
        writer.WriteSingle(ownPosition.Y);
        writer.WriteSingle(ownPosition.Z);
        writer.WriteByte(own.Health);
        writer.WriteByte(own.Stamina);
        writer.WriteByte(own.Mana);
        writer.WriteByte(own.Action);
        writer.WriteByte(own.Level);
        writer.WriteByte(own.Rank);
        writer.WriteByte(own.Class);
        writer.WriteByte(own.Xp);
        writer.WriteByte(own.Status);
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
        writer.WriteByte(entity.Species);
        writer.WriteByte(entity.Health);
        writer.WriteByte(entity.Action);
        writer.WriteByte(entity.Move);
        writer.WriteByte(entity.Stage);
        writer.WriteByte(entity.Level);
        writer.WriteByte(entity.Status);
    }

    /// <summary>A value as a fraction of its full value in 0–255, never 0 unless the value is 0 (a sliver of health still shows).</summary>
    public static byte Fraction(int value, int full) =>
        full <= 0 || value <= 0 ? (byte)0 : (byte)Math.Clamp((long)value * 255 / full, 1, 255);

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
        var vitals = new SnapshotVitals(
            reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(),
            reader.ReadByte());
        int count = reader.ReadByte();
        return count > MaxEntitiesPerPart
            ? throw new MalformedMessageException($"Snapshot part with {count} entities (max {MaxEntitiesPerPart}).")
            : new SnapshotHeader(tick, (uint)acked, own, count, vitals);
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
        return new SnapshotEntity(
            kind, id, position, reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(),
            reader.ReadByte());
    }

    /// <summary>Heading of a direction in 1/256 turns (0 = +Z, clockwise seen from above).</summary>
    public static byte EncodeFacing(Vector3 direction) =>
        (byte)((int)MathF.Round(MathF.Atan2(direction.X, direction.Z) / (2 * MathF.PI) * 256) & 0xFF);

    private static long Quantize(float metres) => (long)MathF.Round(metres * CentimetresPerMetre);

    private static float Dequantize(ref MessageReader reader) => reader.ReadVarInt(-int.MaxValue, int.MaxValue) / CentimetresPerMetre;
}
