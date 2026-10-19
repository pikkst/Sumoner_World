using System.Buffers.Binary;
using System.Text;

namespace SumonerWorld.Net.Messages;

/// <summary>
/// Writes a message into a caller buffer (TDD §3.3): little-endian integers, LEB128 varints, zig-zag signed varints, finite floats
/// and length-prefixed UTF-8 strings. Overflowing the buffer is an error, never a silent truncation. No allocation.
/// </summary>
public ref struct MessageWriter
{
    private readonly Span<byte> _buffer;

    /// <summary>Creates a writer over <paramref name="buffer"/>.</summary>
    public MessageWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        Written = 0;
    }

    /// <summary>Bytes written so far.</summary>
    public int Written { get; private set; }

    /// <summary>The written bytes.</summary>
    public readonly ReadOnlySpan<byte> WrittenSpan => _buffer[..Written];

    /// <summary>Writes one byte.</summary>
    public void WriteByte(byte value) => Take(1)[0] = value;

    /// <summary>Writes a boolean as one byte.</summary>
    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    /// <summary>Writes a 16-bit unsigned integer.</summary>
    public void WriteUInt16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Take(sizeof(ushort)), value);

    /// <summary>Writes a 16-bit signed integer.</summary>
    public void WriteInt16(short value) => BinaryPrimitives.WriteInt16LittleEndian(Take(sizeof(short)), value);

    /// <summary>Writes a 32-bit unsigned integer.</summary>
    public void WriteUInt32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Take(sizeof(uint)), value);

    /// <summary>Writes a 64-bit unsigned integer.</summary>
    public void WriteUInt64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Take(sizeof(ulong)), value);

    /// <summary>Writes an unsigned integer in 1–10 bytes (small values are short).</summary>
    public void WriteVarUInt(ulong value)
    {
        while (value >= 0x80)
        {
            WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        WriteByte((byte)value);
    }

    /// <summary>Writes a signed integer as a zig-zag varint (small magnitudes are short).</summary>
    public void WriteVarInt(long value) => WriteVarUInt((ulong)((value << 1) ^ (value >> 63)));

    /// <summary>Writes a finite 32-bit float. NaN and infinity are rejected (R31: never send what the receiver must reject).</summary>
    public void WriteSingle(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Messages carry finite numbers only.");
        }

        BinaryPrimitives.WriteSingleLittleEndian(Take(sizeof(float)), value);
    }

    /// <summary>Writes a UTF-8 string of at most <paramref name="maxBytes"/> bytes, prefixed with its length.</summary>
    public void WriteString(string value, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        int length = Encoding.UTF8.GetByteCount(value);
        if (length > maxBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"String is {length} bytes; the limit is {maxBytes}.");
        }

        WriteVarUInt((uint)length);
        Encoding.UTF8.GetBytes(value, Take(length));
    }

    private Span<byte> Take(int count)
    {
        if (Written + count > _buffer.Length)
        {
            throw new InvalidOperationException($"Message does not fit in {_buffer.Length} bytes.");
        }

        Span<byte> span = _buffer.Slice(Written, count);
        Written += count;
        return span;
    }
}
