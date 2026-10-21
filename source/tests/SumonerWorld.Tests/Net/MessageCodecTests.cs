using System.Numerics;
using SumonerWorld.Core.Intents;
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
        Assert.Equal(new Welcome(7, 9_000_001, 123_456, true), RoundTrip(new WelcomeCodec(), new Welcome(7, 9_000_001, 123_456, true)));
        Assert.Equal(ServerRules.LockOn, RoundTrip(new WelcomeCodec(), new Welcome(7, 1, 2, false, ServerRules.LockOn)).Rules);
        Assert.Equal(new Rejected(RejectReason.ServerFull, "16/16"), RoundTrip(new RejectedCodec(), new Rejected(RejectReason.ServerFull, "16/16")));
        var intent = new IntentRequest(5, IntentKind.OfferPact, 123_456, 0b11);
        Assert.Equal(intent, RoundTrip(new IntentRequestCodec(), intent));
        var ack = new IntentAck(5, IntentResult.Reject(IntentRejectReason.OnCooldown));
        Assert.Equal(ack, RoundTrip(new IntentAckCodec(), ack));
        CompanionEntry[] companions = [new(77, 1, true), new(9_000_000_001, 0, false)];
        Assert.Equal(companions, RoundTrip(new CompanionListCodec(), new CompanionList(companions)).Entries);
        Assert.Empty(RoundTrip(new CompanionListCodec(), new CompanionList([])).Entries);
    }

    [Fact]
    public void Input_batches_round_trip_with_implied_sequences()
    {
        PlayerInput[] inputs = [new(98, -1000, 250, 0b101), new(99, 0, 0, 0), new(100, 1000, -1000, 1)];
        var writer = new MessageWriter(new byte[Protocol.MaxMessageSize]);
        InputBatch.Write(ref writer, inputs);
        var reader = new MessageReader(writer.WrittenSpan);
        var read = new PlayerInput[InputBatch.MaxInputs];

        Assert.Equal(MessageType.InputBatch, Protocol.ReadHeader(ref reader));
        Assert.Equal(3, InputBatch.Read(ref reader, read));
        Assert.Equal(inputs, read[..3]);
    }

    [Fact]
    public void Snapshots_round_trip_with_exact_own_position_and_centimetre_entities()
    {
        var buffer = new byte[Protocol.MaxMessageSize];
        var writer = new MessageWriter(buffer);
        int countOffset = Snapshot.WriteHeader(ref writer, 300, 41, new Vector3(1.2345678f, 0, 99.000001f));
        Snapshot.WriteEntity(ref writer, new SnapshotEntity(SnapshotEntityKind.Creature, 77, new Vector3(10.004f, 0, -3.456f), 64, 3, 2));
        Snapshot.WriteEntity(ref writer, new SnapshotEntity(SnapshotEntityKind.Player, 9_000_001, new Vector3(511.99f, 0, 0), 0, 0));
        Snapshot.SetEntityCount(buffer, countOffset, 2);

        var reader = new MessageReader(writer.WrittenSpan);
        Assert.Equal(MessageType.Snapshot, Protocol.ReadHeader(ref reader));
        SnapshotHeader header = Snapshot.ReadHeader(ref reader);
        SnapshotEntity creature = Snapshot.ReadEntity(ref reader);
        SnapshotEntity player = Snapshot.ReadEntity(ref reader);
        reader.EnsureEnd();

        Assert.Equal(new SnapshotHeader(300, 41, new Vector3(1.2345678f, 0, 99.000001f), 2), header);
        Assert.Equal(new SnapshotEntity(SnapshotEntityKind.Creature, 77, new Vector3(10f, 0, -3.46f), 64, 3, 2), creature);
        Assert.Equal(9_000_001UL, player.Id);
        Assert.Equal(64, Snapshot.EncodeFacing(Vector3.UnitX));
        Assert.Equal(0, Snapshot.EncodeFacing(Vector3.UnitZ));
    }

    [Fact]
    public void A_typical_input_message_is_tiny()
    {
        var buffer = new byte[Protocol.MaxMessageSize];
        var writer = new MessageWriter(buffer);
        PlayerInput[] inputs = [.. Enumerable.Range(993, InputBatch.MaxInputs).Select(i => new PlayerInput((uint)i, 700, -700, 1))];
        InputBatch.Write(ref writer, inputs);

        Assert.True(writer.Written <= 56, $"A full input batch is {writer.Written} bytes.");
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
            InputBatch.Write(ref writer, [new PlayerInput(1, 2000, 0, 0)]);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            var writer = new MessageWriter(new byte[64]);
            InputBatch.Write(ref writer, [new PlayerInput(1, 0, 0, 0), new PlayerInput(3, 0, 0, 0)]);
        });
    }

    [Theory]
    [InlineData(new byte[] { (byte)Protocol.Version, 0x00 }, "truncated")]
    [InlineData(new byte[] { 0x01, 0x00, 0x05 }, "Protocol version 1")]
    [InlineData(new byte[] { (byte)Protocol.Version, 0x00, 0x63 }, "Unknown message type 99")]
    [InlineData(new byte[] { (byte)Protocol.Version, 0x00, 0x04 }, "Unknown message type 4")]
    [InlineData(new byte[] { (byte)Protocol.Version, 0x00, 0x05, 0x01, 0x01, 0xA0, 0x1F, 0x00, 0x00 }, "outside")]
    [InlineData(new byte[] { (byte)Protocol.Version, 0x00, 0x05, 0x01, 0x01, 0x00, 0x00, 0x00, 0xFF }, "trailing")]
    [InlineData(new byte[] { (byte)Protocol.Version, 0x00, 0x05, 0x01, 0x00 }, "out of range")]
    [InlineData(new byte[] { (byte)Protocol.Version, 0x00, 0x05, 0x01, 0x02 }, "out of range")]
    public void Malformed_input_is_rejected_with_a_reason(byte[] bytes, string expected)
    {
        var error = Assert.Throws<MalformedMessageException>(() =>
        {
            var reader = new MessageReader(bytes);
            Protocol.ReadHeader(ref reader);
            InputBatch.Read(ref reader, new PlayerInput[InputBatch.MaxInputs]);
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
                buffer[0] = (byte)Protocol.Version;
                buffer[1] = 0;
                buffer[2] = (byte)random.NextInt(1, 12);
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
                case MessageType.InputBatch:
                    InputBatch.Read(ref reader, new PlayerInput[InputBatch.MaxInputs]);
                    break;
                case MessageType.Intent:
                    new IntentRequestCodec().Read(ref reader);
                    break;
                case MessageType.IntentAck:
                    new IntentAckCodec().Read(ref reader);
                    break;
                case MessageType.DevRequest:
                    new DevRequestCodec().Read(ref reader);
                    break;
                case MessageType.DevMetrics:
                    new ServerMetricsCodec().Read(ref reader);
                    break;
                case MessageType.DevInspection:
                    new DevInspectionCodec().Read(ref reader);
                    break;
                default:
                    SnapshotHeader header = Snapshot.ReadHeader(ref reader);
                    for (int i = 0; i < header.EntityCount; i++)
                    {
                        Snapshot.ReadEntity(ref reader);
                    }

                    reader.EnsureEnd();
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
