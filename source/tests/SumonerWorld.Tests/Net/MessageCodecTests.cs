using SumonerWorld.Core.Randomness;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Tests.Net;

public class MessageCodecTests
{
    [Theory]
    [InlineData(0UL)]
    [InlineData(127UL)]
    [InlineData(128UL)]
    [InlineData(300UL)]
    [InlineData(ulong.MaxValue)]
    public void Varints_round_trip_and_small_values_are_short(ulong value)
    {
        var buffer = new byte[16];
        var writer = new MessageWriter(buffer);
        writer.WriteVarUInt(value);
        var reader = new MessageReader(writer.WrittenSpan);

        Assert.Equal(value, reader.ReadVarUInt());
        Assert.Equal(value < 128 ? 1 : value < 16_384 ? 2 : writer.Written, writer.Written);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(1000L)]
    [InlineData(-1000L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void Signed_varints_round_trip(long value)
    {
        var buffer = new byte[16];
        var writer = new MessageWriter(buffer);
        writer.WriteVarInt(value);
        var reader = new MessageReader(writer.WrittenSpan);

        Assert.Equal(value, reader.ReadVarInt());
    }

    [Fact]
    public void Every_message_round_trips()
    {
        Assert.Equal(new Hello(42, "dev-token"), RoundTrip(new HelloCodec(), new Hello(42, "dev-token")));
        Assert.Equal(new Welcome(7, 9_000_001, 123_456), RoundTrip(new WelcomeCodec(), new Welcome(7, 9_000_001, 123_456)));
        Assert.Equal(new Rejected(RejectReason.ServerFull, "16/16"), RoundTrip(new RejectedCodec(), new Rejected(RejectReason.ServerFull, "16/16")));
        Assert.Equal(new PlayerInput(99, -1000, 250, 0b101), RoundTrip(new PlayerInputCodec(), new PlayerInput(99, -1000, 250, 0b101)));
    }

    [Fact]
    public void A_typical_input_message_is_tiny()
    {
        var buffer = new byte[Protocol.MaxMessageSize];
        var writer = new MessageWriter(buffer);
        new PlayerInputCodec().Write(ref writer, new PlayerInput(1_000, 700, -700, 1));

        Assert.True(writer.Written <= 12, $"Input message is {writer.Written} bytes.");
    }

    [Fact]
    public void Writers_refuse_what_receivers_would_reject()
    {
        var buffer = new byte[4];

        Assert.Throws<ArgumentOutOfRangeException>(() => new MessageWriter(new byte[8]).WriteSingle(float.NaN));
        Assert.Throws<InvalidOperationException>(() => new MessageWriter(buffer).WriteUInt64(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MessageWriter(new byte[64]).WriteString(new string('x', 40), 32));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var writer = new MessageWriter(new byte[64]);
            new PlayerInputCodec().Write(ref writer, new PlayerInput(1, 2000, 0, 0));
        });
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x00 }, "truncated")]
    [InlineData(new byte[] { 0x02, 0x00, 0x04 }, "Protocol version 2")]
    [InlineData(new byte[] { 0x01, 0x00, 0x63 }, "Unknown message type 99")]
    [InlineData(new byte[] { 0x01, 0x00, 0x04, 0x01, 0xA0, 0x1F, 0x00, 0x00 }, "outside")]
    [InlineData(new byte[] { 0x01, 0x00, 0x04, 0x01, 0x00, 0x00, 0x00, 0xFF }, "trailing")]
    public void Malformed_input_is_rejected_with_a_reason(byte[] bytes, string expected)
    {
        var error = Assert.Throws<MalformedMessageException>(() =>
        {
            var reader = new MessageReader(bytes);
            Protocol.ReadHeader(ref reader);
            new PlayerInputCodec().Read(ref reader);
        });

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Random_bytes_never_cause_anything_but_a_malformed_message()
    {
        // Fuzz test (R31): 20,000 seeded random messages, some starting with a valid header, through every codec.
        var random = new SeededRandom(0xF022);
        var buffer = new byte[64];
        for (int i = 0; i < 20_000; i++)
        {
            int length = random.NextInt(0, buffer.Length);
            for (int b = 0; b < length; b++)
            {
                buffer[b] = (byte)random.NextInt(0, 256);
            }

            if (length >= 3 && i % 2 == 0)
            {
                buffer[0] = 1;
                buffer[1] = 0;
                buffer[2] = (byte)random.NextInt(1, 5);
            }

            ReadAnything(buffer.AsSpan(0, length));
        }
    }

    private static void ReadAnything(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var reader = new MessageReader(bytes);
            switch (Protocol.ReadHeader(ref reader))
            {
                case MessageType.Hello:
                    new HelloCodec().Read(ref reader);
                    break;
                case MessageType.Welcome:
                    new WelcomeCodec().Read(ref reader);
                    break;
                case MessageType.Rejected:
                    new RejectedCodec().Read(ref reader);
                    break;
                default:
                    new PlayerInputCodec().Read(ref reader);
                    break;
            }
        }
        catch (MalformedMessageException)
        {
            // Expected: anything else escapes and fails the test.
        }
    }

    private static T RoundTrip<T>(IMessageCodec<T> codec, T message)
        where T : struct
    {
        var buffer = new byte[Protocol.MaxMessageSize];
        var writer = new MessageWriter(buffer);
        codec.Write(ref writer, message);
        var reader = new MessageReader(writer.WrittenSpan);
        Assert.Equal(codec.Type, Protocol.ReadHeader(ref reader));
        return codec.Read(ref reader);
    }
}
