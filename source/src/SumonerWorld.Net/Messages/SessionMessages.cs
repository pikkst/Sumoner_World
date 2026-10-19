namespace SumonerWorld.Net.Messages;

/// <summary>Client → server: join (resume token 0) or resume a session within the reconnect window (TDD §3.5).</summary>
/// <param name="ResumeToken">Session token from an earlier <see cref="Welcome"/>, or 0.</param>
/// <param name="AuthTicket">Steam session ticket, or the dev token on the Docker dev server (TDD §3.3).</param>
public readonly record struct Hello(ulong ResumeToken, string AuthTicket)
{
    /// <summary>Longest auth ticket, in UTF-8 bytes.</summary>
    public const int MaxTicketBytes = 512;
}

/// <summary>Server → client: the session is open.</summary>
/// <param name="SessionToken">Token for resuming after a disconnect.</param>
/// <param name="AccountId">The player's account id (what creatures remember).</param>
/// <param name="ServerTick">Server tick at the time of sending.</param>
public readonly record struct Welcome(ulong SessionToken, ulong AccountId, ulong ServerTick);

/// <summary>Server → client: the session was refused.</summary>
/// <param name="Reason">Why.</param>
/// <param name="Detail">Human-readable detail for logs and the client UI.</param>
public readonly record struct Rejected(RejectReason Reason, string Detail)
{
    /// <summary>Longest detail text, in UTF-8 bytes.</summary>
    public const int MaxDetailBytes = 128;
}

/// <summary>
/// Client → server: movement intent and buttons for one client tick (TDD §3.2). Clients send input, never positions (R31).
/// </summary>
/// <param name="Sequence">Increases by one per input; the server acknowledges it for reconciliation.</param>
/// <param name="MoveX">Desired movement along X, −1000…1000 (stick or keys, normalised).</param>
/// <param name="MoveZ">Desired movement along Z, −1000…1000.</param>
/// <param name="Buttons">Pressed buttons (bit flags).</param>
public readonly record struct PlayerInput(uint Sequence, short MoveX, short MoveZ, byte Buttons)
{
    /// <summary>Largest movement magnitude per axis.</summary>
    public const short MaxAxis = 1000;
}

/// <summary>Codec for <see cref="Hello"/>.</summary>
public sealed class HelloCodec : IMessageCodec<Hello>
{
    /// <inheritdoc />
    public MessageType Type => MessageType.Hello;

    /// <inheritdoc />
    public void Write(ref MessageWriter writer, in Hello message)
    {
        Protocol.WriteHeader(ref writer, Type);
        writer.WriteUInt64(message.ResumeToken);
        writer.WriteString(message.AuthTicket, Hello.MaxTicketBytes);
    }

    /// <inheritdoc />
    public Hello Read(ref MessageReader reader)
    {
        var message = new Hello(reader.ReadUInt64(), reader.ReadString(Hello.MaxTicketBytes));
        reader.EnsureEnd();
        return message;
    }
}

/// <summary>Codec for <see cref="Welcome"/>.</summary>
public sealed class WelcomeCodec : IMessageCodec<Welcome>
{
    /// <inheritdoc />
    public MessageType Type => MessageType.Welcome;

    /// <inheritdoc />
    public void Write(ref MessageWriter writer, in Welcome message)
    {
        Protocol.WriteHeader(ref writer, Type);
        writer.WriteUInt64(message.SessionToken);
        writer.WriteUInt64(message.AccountId);
        writer.WriteVarUInt(message.ServerTick);
    }

    /// <inheritdoc />
    public Welcome Read(ref MessageReader reader)
    {
        var message = new Welcome(reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadVarUInt());
        reader.EnsureEnd();
        return message;
    }
}

/// <summary>Codec for <see cref="Rejected"/>.</summary>
public sealed class RejectedCodec : IMessageCodec<Rejected>
{
    /// <inheritdoc />
    public MessageType Type => MessageType.Rejected;

    /// <inheritdoc />
    public void Write(ref MessageWriter writer, in Rejected message)
    {
        Protocol.WriteHeader(ref writer, Type);
        writer.WriteByte((byte)message.Reason);
        writer.WriteString(message.Detail, Rejected.MaxDetailBytes);
    }

    /// <inheritdoc />
    public Rejected Read(ref MessageReader reader)
    {
        var reason = (RejectReason)reader.ReadByte();
        if (!Enum.IsDefined(reason))
        {
            throw new MalformedMessageException($"Unknown reject reason {(byte)reason}.");
        }

        var message = new Rejected(reason, reader.ReadString(Rejected.MaxDetailBytes));
        reader.EnsureEnd();
        return message;
    }
}

/// <summary>Codec for <see cref="PlayerInput"/> (5–9 bytes after the header).</summary>
public sealed class PlayerInputCodec : IMessageCodec<PlayerInput>
{
    /// <inheritdoc />
    public MessageType Type => MessageType.PlayerInput;

    /// <inheritdoc />
    public void Write(ref MessageWriter writer, in PlayerInput message)
    {
        if (Math.Abs((int)message.MoveX) > PlayerInput.MaxAxis || Math.Abs((int)message.MoveZ) > PlayerInput.MaxAxis)
        {
            throw new ArgumentOutOfRangeException(nameof(message), message, $"Movement axes must be within ±{PlayerInput.MaxAxis}.");
        }

        Protocol.WriteHeader(ref writer, Type);
        writer.WriteVarUInt(message.Sequence);
        writer.WriteVarInt(message.MoveX);
        writer.WriteVarInt(message.MoveZ);
        writer.WriteByte(message.Buttons);
    }

    /// <inheritdoc />
    public PlayerInput Read(ref MessageReader reader)
    {
        ulong sequence = reader.ReadVarUInt();
        if (sequence > uint.MaxValue)
        {
            throw new MalformedMessageException($"Input sequence {sequence} is out of range.");
        }

        var message = new PlayerInput(
            (uint)sequence,
            (short)reader.ReadVarInt(-PlayerInput.MaxAxis, PlayerInput.MaxAxis),
            (short)reader.ReadVarInt(-PlayerInput.MaxAxis, PlayerInput.MaxAxis),
            reader.ReadByte());
        reader.EnsureEnd();
        return message;
    }
}
