namespace SumonerWorld.Net.Messages;

/// <summary>Message types on the wire. Values are part of the protocol: never reuse or renumber them.</summary>
public enum MessageType : byte
{
    /// <summary>Client → server: join or resume a session.</summary>
    Hello = 1,

    /// <summary>Server → client: session accepted.</summary>
    Welcome = 2,

    /// <summary>Server → client: session refused.</summary>
    Rejected = 3,

    // 4: retired (single PlayerInput, protocol 1); replaced by InputBatch.

    /// <summary>Client → server, unreliable: the newest inputs (redundant copies cover packet loss).</summary>
    InputBatch = 5,

    /// <summary>Client → server, reliable: a state-changing intent with its sequence.</summary>
    Intent = 6,

    /// <summary>Server → client, reliable: the result of an intent.</summary>
    IntentAck = 7,

    /// <summary>Server → client, unreliable: world state around the player (20 Hz, may come in several parts).</summary>
    Snapshot = 8,

    /// <summary>Client → server, reliable: what the dev tools want to see (dev servers only, since protocol 5).</summary>
    DevRequest = 9,

    /// <summary>Server → client, unreliable: server tick metrics for the dev overlay.</summary>
    DevMetrics = 10,

    /// <summary>Server → client, unreliable: the inspected creature's mind for the AI Inspector.</summary>
    DevInspection = 11,
}

/// <summary>Why the server refused a session.</summary>
public enum RejectReason : byte
{
    /// <summary>The client speaks another protocol version.</summary>
    ProtocolMismatch = 1,

    /// <summary>Authentication failed.</summary>
    NotAuthenticated = 2,

    /// <summary>The server is full.</summary>
    ServerFull = 3,

    /// <summary>The account is banned.</summary>
    Banned = 4,
}

/// <summary>
/// Every message starts with the protocol version and the message type (TDD §3.3). A version mismatch or unknown type is malformed.
/// </summary>
public static class Protocol
{
    /// <summary>Current protocol version; bump on any wire-format change.</summary>
    public const ushort Version = 6;

    /// <summary>Largest message, in bytes (fits one UDP datagram on common networks).</summary>
    public const int MaxMessageSize = 1200;

    /// <summary>Writes the header.</summary>
    public static void WriteHeader(ref MessageWriter writer, MessageType type)
    {
        writer.WriteUInt16(Version);
        writer.WriteByte((byte)type);
    }

    /// <summary>Reads and checks the header; returns the message type.</summary>
    public static MessageType ReadHeader(ref MessageReader reader)
    {
        ushort version = reader.ReadUInt16();
        if (version != Version)
        {
            throw new MalformedMessageException($"Protocol version {version} is not supported (server speaks {Version}).");
        }

        var type = (MessageType)reader.ReadByte();
        return Enum.IsDefined(type) ? type : throw new MalformedMessageException($"Unknown message type {(byte)type}.");
    }
}

/// <summary>Writes and reads one message type (TDD §3.3). Codecs hold no state and do not allocate (except strings).</summary>
/// <typeparam name="T">The message.</typeparam>
public interface IMessageCodec<T>
    where T : struct
{
    /// <summary>The message type this codec handles.</summary>
    MessageType Type { get; }

    /// <summary>Writes header and body.</summary>
    void Write(ref MessageWriter writer, in T message);

    /// <summary>Reads the body (after the header) and checks every field; throws <see cref="MalformedMessageException"/>.</summary>
    T Read(ref MessageReader reader);
}
