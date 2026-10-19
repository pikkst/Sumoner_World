using System.Buffers.Binary;
using System.Text;

namespace SumonerWorld.Net.Messages;

/// <summary>
/// A received message was invalid: truncated, oversized, wrong protocol version, unknown type, out-of-range or non-finite values.
/// The connection's sender is never trusted (R31); servers log the reason and count it toward a kick (TDD §3.3).
/// </summary>
public sealed class MalformedMessageException : Exception
{
    /// <summary>Creates the exception.</summary>
    public MalformedMessageException()
        : base("Malformed message.")
    {
    }

    /// <summary>Creates the exception with a reason.</summary>
    public MalformedMessageException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a reason and cause.</summary>
    public MalformedMessageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Reads what <see cref="MessageWriter"/> wrote, checking every bound. Any problem throws <see cref="MalformedMessageException"/>
/// and nothing else, so receivers can treat bad input uniformly. No allocation except for strings.
/// </summary>
public ref struct MessageReader
{
    private const int MaxVarIntBytes = 10;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    /// <summary>Creates a reader over <paramref name="buffer"/>.</summary>
    public MessageReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    /// <summary>Bytes not yet read.</summary>
    public readonly int Remaining => _buffer.Length - _position;

    /// <summary>Reads one byte.</summary>
    public byte ReadByte() => Take(1)[0];

    /// <summary>Reads a boolean (only 0 or 1 are valid).</summary>
    public bool ReadBool() => ReadByte() switch
    {
        0 => false,
        1 => true,
        byte other => throw new MalformedMessageException($"Boolean byte must be 0 or 1, got {other}."),
    };

    /// <summary>Reads a 16-bit unsigned integer.</summary>
    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(sizeof(ushort)));

    /// <summary>Reads a 16-bit signed integer.</summary>
    public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Take(sizeof(short)));

    /// <summary>Reads a 32-bit unsigned integer.</summary>
    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(sizeof(uint)));

    /// <summary>Reads a 64-bit unsigned integer.</summary>
    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(sizeof(ulong)));

    /// <summary>Reads an unsigned varint (at most 10 bytes, no overflow).</summary>
    public ulong ReadVarUInt()
    {
        ulong value = 0;
        for (int i = 0; i < MaxVarIntBytes; i++)
        {
            byte next = ReadByte();
            if (i == MaxVarIntBytes - 1 && next > 1)
            {
                throw new MalformedMessageException("Varint overflows 64 bits.");
            }

            value |= (ulong)(next & 0x7F) << (7 * i);
            if ((next & 0x80) == 0)
            {
                return value;
            }
        }

        throw new MalformedMessageException("Varint is longer than 10 bytes.");
    }

    /// <summary>Reads a zig-zag signed varint.</summary>
    public long ReadVarInt()
    {
        ulong raw = ReadVarUInt();
        return (long)(raw >> 1) ^ -(long)(raw & 1);
    }

    /// <summary>Reads a varint that must be within [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public long ReadVarInt(long min, long max)
    {
        long value = ReadVarInt();
        return value >= min && value <= max
            ? value
            : throw new MalformedMessageException($"Value {value} is outside [{min}, {max}].");
    }

    /// <summary>Reads a finite 32-bit float (NaN and infinity are malformed).</summary>
    public float ReadSingle()
    {
        float value = BinaryPrimitives.ReadSingleLittleEndian(Take(sizeof(float)));
        return float.IsFinite(value) ? value : throw new MalformedMessageException("Non-finite number.");
    }

    /// <summary>Reads a length-prefixed UTF-8 string of at most <paramref name="maxBytes"/> bytes.</summary>
    public string ReadString(int maxBytes)
    {
        ulong length = ReadVarUInt();
        if (length > (ulong)maxBytes)
        {
            throw new MalformedMessageException($"String of {length} bytes exceeds the {maxBytes}-byte limit.");
        }

        try
        {
            return StrictUtf8.GetString(Take((int)length));
        }
        catch (DecoderFallbackException exception)
        {
            throw new MalformedMessageException("String is not valid UTF-8.", exception);
        }
    }

    /// <summary>Fails if bytes are left over (a message must be consumed exactly).</summary>
    public readonly void EnsureEnd()
    {
        if (Remaining != 0)
        {
            throw new MalformedMessageException($"{Remaining} unexpected trailing byte(s).");
        }
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count > Remaining)
        {
            throw new MalformedMessageException($"Message is truncated: needed {count} byte(s), {Remaining} left.");
        }

        ReadOnlySpan<byte> span = _buffer.Slice(_position, count);
        _position += count;
        return span;
    }
}
