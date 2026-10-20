using MessagePack;

namespace SumonerWorld.Persistence.Records;

/// <summary>A saved payload could not be read (unknown record version or damaged bytes).</summary>
public sealed class RecordFormatException : Exception
{
    /// <summary>Creates the exception.</summary>
    public RecordFormatException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public RecordFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and cause.</summary>
    public RecordFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Encodes one record type for the world database (TDD §4.1, §4.3): one version byte, then MessagePack with explicit integer keys.
/// Keys are never reused or renumbered; a new payload layout gets a new version and an upgrade path.
/// </summary>
/// <typeparam name="T">The record.</typeparam>
public interface IRecordCodec<T>
    where T : class
{
    /// <summary>The payload version this codec writes.</summary>
    byte Version { get; }

    /// <summary>Encodes a record.</summary>
    byte[] Encode(T record);

    /// <summary>Decodes a payload; throws <see cref="RecordFormatException"/> for an unknown version or damaged bytes.</summary>
    T Decode(ReadOnlySpan<byte> payload);
}

/// <summary>MessagePack record codec (MessagePack-CSharp, MIT). Payloads are read with the untrusted-data security settings.</summary>
/// <typeparam name="T">A <see cref="MessagePackObjectAttribute"/> type with integer keys.</typeparam>
public sealed class MessagePackRecordCodec<T> : IRecordCodec<T>
    where T : class
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);

    /// <summary>Creates a codec for payload version <paramref name="version"/>.</summary>
    public MessagePackRecordCodec(byte version)
    {
        ArgumentOutOfRangeException.ThrowIfZero(version);
        Version = version;
    }

    /// <inheritdoc />
    public byte Version { get; }

    /// <inheritdoc />
    public byte[] Encode(T record)
    {
        ArgumentNullException.ThrowIfNull(record);
        byte[] body = MessagePackSerializer.Serialize(record, Options);
        var payload = new byte[body.Length + 1];
        payload[0] = Version;
        body.CopyTo(payload, 1);
        return payload;
    }

    /// <inheritdoc />
    public T Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload[0] != Version)
        {
            throw new RecordFormatException(
                $"{typeof(T).Name} payload version {(payload.IsEmpty ? "(empty)" : payload[0].ToString(System.Globalization.CultureInfo.InvariantCulture))} is not {Version}.");
        }

        try
        {
            return MessagePackSerializer.Deserialize<T>(payload[1..].ToArray(), Options)
                ?? throw new RecordFormatException($"{typeof(T).Name} payload is nil.");
        }
        catch (MessagePackSerializationException exception)
        {
            throw new RecordFormatException($"{typeof(T).Name} payload is damaged: {exception.Message}", exception);
        }
    }
}

/// <summary>An item (TDD §3.4): the id and owner are columns, the rest is this payload (version 1).</summary>
[MessagePackObject]
public sealed record ItemRecord
{
    /// <summary>Item kind id from the game data.</summary>
    [Key(0)]
    public ushort Kind { get; init; }

    /// <summary>Stack size.</summary>
    [Key(1)]
    public int Quantity { get; init; } = 1;
}
