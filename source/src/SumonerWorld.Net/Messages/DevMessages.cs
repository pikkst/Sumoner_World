using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Lod;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;

namespace SumonerWorld.Net.Messages;

/// <summary>
/// Client → server, reliable (dev tools, M1-22): what the dev client wants to see. The server answers only when it runs with dev
/// tools on (<see cref="Server.GameServerSettings.DevTools"/>); otherwise the request is ignored and logged.
/// </summary>
/// <param name="Metrics">Send <see cref="ServerMetrics"/> for the dev overlay.</param>
/// <param name="Inspect">Stable id of the creature to inspect (AI Inspector), 0 for none.</param>
public readonly record struct DevRequest(bool Metrics, ulong Inspect);

/// <summary>Mean and longest time of one tick stage over a report window, in microseconds.</summary>
/// <param name="MeanMicros">Mean.</param>
/// <param name="MaxMicros">Longest.</param>
public readonly record struct StageTiming(int MeanMicros, int MaxMicros);

/// <summary>
/// Server → client, unreliable, twice a second (dev overlay, TDD §6): the server's tick and stage times, allocations, AI tier
/// counts and bandwidth over the last report window.
/// </summary>
/// <param name="Tick">Server tick at the end of the window.</param>
/// <param name="Ticks">Ticks in the window.</param>
/// <param name="TickTime">Whole server tick (receive, simulate, send).</param>
/// <param name="Stages">Each <see cref="SimulationStage"/>, in stage order.</param>
/// <param name="AllocatedPerTick">Mean bytes allocated on the server thread per tick.</param>
/// <param name="Collections">Garbage collections so far: gen 0, 1, 2.</param>
/// <param name="Tiers">Creatures per <see cref="AiLodTier"/>.</param>
/// <param name="Players">Players connected.</param>
/// <param name="SentBytesPerSecond">Everything the server sent, per second (payload plus the per-message estimate).</param>
/// <param name="ClientBytesPerSecond">What the server sent to the receiving client, per second.</param>
/// <param name="WorkingSetMiB">Server process memory.</param>
public readonly record struct ServerMetrics(
    ulong Tick,
    int Ticks,
    StageTiming TickTime,
    IReadOnlyList<StageTiming> Stages,
    long AllocatedPerTick,
    IReadOnlyList<int> Collections,
    IReadOnlyList<int> Tiers,
    int Players,
    long SentBytesPerSecond,
    long ClientBytesPerSecond,
    int WorkingSetMiB)
{
    /// <summary>Stages reported.</summary>
    public static readonly int StageCount = Enum.GetValues<SimulationStage>().Length;

    /// <summary>AI tiers reported.</summary>
    public static readonly int TierCount = Enum.GetValues<AiLodTier>().Length;

    /// <summary>Garbage-collection generations reported.</summary>
    public const int Generations = 3;

    /// <summary>Time of all creature AI stages (perception, decision, behaviour), mean and longest, in microseconds.</summary>
    public StageTiming Ai => new(
        Stages[(int)SimulationStage.Perception].MeanMicros + Stages[(int)SimulationStage.AiDecision].MeanMicros + Stages[(int)SimulationStage.Behaviour].MeanMicros,
        Stages[(int)SimulationStage.Perception].MaxMicros + Stages[(int)SimulationStage.AiDecision].MaxMicros + Stages[(int)SimulationStage.Behaviour].MaxMicros);
}

/// <summary>Server → client, unreliable, twice a second (AI Inspector): the inspected creature, or null when it is gone.</summary>
/// <param name="Requested">The stable id asked for.</param>
/// <param name="Inspection">What the server sees in its mind; null when no creature has that id (any more).</param>
public readonly record struct DevInspection(ulong Requested, CreatureInspection? Inspection);

/// <summary>Codec for <see cref="DevRequest"/>.</summary>
public sealed class DevRequestCodec : IMessageCodec<DevRequest>
{
    /// <inheritdoc />
    public MessageType Type => MessageType.DevRequest;

    /// <inheritdoc />
    public void Write(ref MessageWriter writer, in DevRequest message)
    {
        Protocol.WriteHeader(ref writer, Type);
        writer.WriteBool(message.Metrics);
        writer.WriteVarUInt(message.Inspect);
    }

    /// <inheritdoc />
    public DevRequest Read(ref MessageReader reader)
    {
        var message = new DevRequest(reader.ReadBool(), reader.ReadVarUInt());
        reader.EnsureEnd();
        return message;
    }
}

/// <summary>Codec for <see cref="ServerMetrics"/> (allocates on read; dev tools only).</summary>
public sealed class ServerMetricsCodec : IMessageCodec<ServerMetrics>
{
    private const int MaxMicros = 60_000_000;

    /// <inheritdoc />
    public MessageType Type => MessageType.DevMetrics;

    /// <inheritdoc />
    public void Write(ref MessageWriter writer, in ServerMetrics message)
    {
        Protocol.WriteHeader(ref writer, Type);
        writer.WriteVarUInt(message.Tick);
        writer.WriteVarUInt((ulong)message.Ticks);
        WriteTiming(ref writer, message.TickTime);
        WriteCount(ref writer, message.Stages.Count, ServerMetrics.StageCount);
        foreach (StageTiming stage in message.Stages)
        {
            WriteTiming(ref writer, stage);
        }

        writer.WriteVarUInt((ulong)message.AllocatedPerTick);
        WriteCount(ref writer, message.Collections.Count, ServerMetrics.Generations);
        foreach (int count in message.Collections)
        {
            writer.WriteVarUInt((ulong)count);
        }

        WriteCount(ref writer, message.Tiers.Count, ServerMetrics.TierCount);
        foreach (int count in message.Tiers)
        {
            writer.WriteVarUInt((ulong)count);
        }

        writer.WriteVarUInt((ulong)message.Players);
        writer.WriteVarUInt((ulong)message.SentBytesPerSecond);
        writer.WriteVarUInt((ulong)message.ClientBytesPerSecond);
        writer.WriteVarUInt((ulong)message.WorkingSetMiB);
    }

    /// <inheritdoc />
    public ServerMetrics Read(ref MessageReader reader)
    {
        ulong tick = reader.ReadVarUInt();
        int ticks = ReadInt(ref reader, int.MaxValue);
        StageTiming whole = ReadTiming(ref reader);
        var stages = new StageTiming[ReadCount(ref reader, ServerMetrics.StageCount)];
        for (int i = 0; i < stages.Length; i++)
        {
            stages[i] = ReadTiming(ref reader);
        }

        long allocated = (long)Math.Min(reader.ReadVarUInt(), long.MaxValue);
        int[] collections = ReadInts(ref reader, ServerMetrics.Generations);
        int[] tiers = ReadInts(ref reader, ServerMetrics.TierCount);
        var message = new ServerMetrics(
            tick,
            ticks,
            whole,
            stages,
            allocated,
            collections,
            tiers,
            ReadInt(ref reader, ushort.MaxValue),
            ReadLong(ref reader),
            ReadLong(ref reader),
            ReadInt(ref reader, int.MaxValue));
        reader.EnsureEnd();
        return message;
    }

    private static void WriteCount(ref MessageWriter writer, int count, int expected)
    {
        if (count != expected)
        {
            throw new ArgumentException($"Expected {expected} values, got {count}.", nameof(count));
        }

        writer.WriteByte((byte)count);
    }

    private static int ReadCount(ref MessageReader reader, int expected)
    {
        int count = reader.ReadByte();
        return count == expected ? count : throw new MalformedMessageException($"Expected {expected} values, got {count}.");
    }

    private static int[] ReadInts(ref MessageReader reader, int expected)
    {
        int[] values = new int[ReadCount(ref reader, expected)];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = ReadInt(ref reader, int.MaxValue);
        }

        return values;
    }

    private static void WriteTiming(ref MessageWriter writer, StageTiming timing)
    {
        writer.WriteVarUInt((ulong)Math.Clamp(timing.MeanMicros, 0, MaxMicros));
        writer.WriteVarUInt((ulong)Math.Clamp(timing.MaxMicros, 0, MaxMicros));
    }

    private static StageTiming ReadTiming(ref MessageReader reader) => new(ReadInt(ref reader, MaxMicros), ReadInt(ref reader, MaxMicros));

    private static int ReadInt(ref MessageReader reader, int max)
    {
        ulong value = reader.ReadVarUInt();
        return value <= (ulong)max ? (int)value : throw new MalformedMessageException($"Value {value} exceeds {max}.");
    }

    private static long ReadLong(ref MessageReader reader)
    {
        ulong value = reader.ReadVarUInt();
        return value <= long.MaxValue ? (long)value : throw new MalformedMessageException($"Value {value} exceeds {long.MaxValue}.");
    }
}

/// <summary>Codec for <see cref="DevInspection"/> (allocates on read; dev tools only). Every field is range-checked.</summary>
public sealed class DevInspectionCodec : IMessageCodec<DevInspection>
{
    private const int MaxMemoryTags = 0x0F;
    private static readonly int NeedCount = Enum.GetValues<Need>().Length;

    /// <summary>Largest encoded message, in bytes (fits <see cref="Protocol.MaxMessageSize"/>; checked by a test).</summary>
    public const int MaxBytes = 3 + 10 + 1 + 10 + 10 + 3 + 5 + 1 + 12 + 1 + 3 + 1 + 1 + 10 + 2 + 8 + (5 * 2) + 1 + (5 * 1) + 2 + 2
        + 1 + (CreatureInspection.MaxMemories * (3 + 1 + 10 + 2 + 2 + 3 + 1 + 10))
        + 1 + (CreatureInspection.MaxOpinions * (1 + 10 + (4 * 2)))
        + 1 + (CreatureInspection.MaxGoals * (3 + 5))
        + 3 + 1
        + 1 + 1 + 1 + 5 + 1 + 10;

    /// <inheritdoc />
    public MessageType Type => MessageType.DevInspection;

    /// <inheritdoc />
    public void Write(ref MessageWriter writer, in DevInspection message)
    {
        Protocol.WriteHeader(ref writer, Type);
        writer.WriteVarUInt(message.Requested);
        writer.WriteBool(message.Inspection is not null);
        if (message.Inspection is not CreatureInspection inspection)
        {
            return;
        }

        writer.WriteVarUInt(inspection.Tick);
        writer.WriteVarUInt(inspection.Id.Value);
        writer.WriteVarUInt(inspection.Species);
        writer.WriteVarInt(inspection.Group);
        writer.WriteByte((byte)inspection.Tier);
        writer.WriteSingle(inspection.Position.X);
        writer.WriteSingle(inspection.Position.Y);
        writer.WriteSingle(inspection.Position.Z);
        writer.WriteByte((byte)inspection.Emote);
        writer.WriteVarUInt(inspection.Goal);
        writer.WriteBool(inspection.Focus is not null);
        if (inspection.Focus is MemorySubject focus)
        {
            WriteSubject(ref writer, focus);
        }

        writer.WriteVarUInt((ulong)Math.Max(0, inspection.FocusDistance));
        WritePersonality(ref writer, inspection.Birth);
        WritePersonality(ref writer, inspection.Personality);
        EmotionState emotions = inspection.Emotions;
        writer.WriteByte((byte)Emotion(emotions.Fear));
        writer.WriteByte((byte)Emotion(emotions.Anger));
        writer.WriteByte((byte)Emotion(emotions.Joy));
        writer.WriteByte((byte)Emotion(emotions.Curiosity));
        writer.WriteByte((byte)Emotion(emotions.Stress));
        writer.WriteByte((byte)inspection.Needs.Count);
        foreach (int need in inspection.Needs)
        {
            writer.WriteByte((byte)Math.Clamp(need, 0, ICreatureMindStore.MaxNeed));
        }

        writer.WriteVarUInt((ulong)inspection.CoreMemories);
        writer.WriteVarUInt((ulong)inspection.EpisodicMemories);
        WriteMemories(ref writer, inspection.Memories);
        WriteOpinions(ref writer, inspection.Opinions);
        writer.WriteByte((byte)Math.Min(inspection.Goals.Count, CreatureInspection.MaxGoals));
        foreach (GoalScore goal in inspection.Goals.Take(CreatureInspection.MaxGoals))
        {
            writer.WriteVarUInt(goal.Goal);
            writer.WriteVarUInt((ulong)Math.Max(0, goal.Score));
        }

        writer.WriteVarUInt(inspection.TreeGoal);
        writer.WriteVarInt(inspection.TreeNode);
        writer.WriteBool(inspection.Command is not null);
        if (inspection.Command is InspectedCommand command)
        {
            writer.WriteByte((byte)command.Command);
            writer.WriteByte((byte)command.Response);
            writer.WriteVarInt(command.Obedience);
            writer.WriteByte((byte)command.Reason);
            writer.WriteVarUInt(command.AgeTicks);
        }
    }

    /// <inheritdoc />
    public DevInspection Read(ref MessageReader reader)
    {
        ulong requested = reader.ReadVarUInt();
        if (!reader.ReadBool())
        {
            reader.EnsureEnd();
            return new DevInspection(requested, null);
        }

        ulong tick = reader.ReadVarUInt();
        var id = new StableId(reader.ReadVarUInt());
        var species = (ushort)ReadUInt(ref reader, ushort.MaxValue);
        int group = (int)reader.ReadVarInt(int.MinValue, int.MaxValue);
        AiLodTier tier = ReadEnum<AiLodTier>(reader.ReadByte());
        var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        Emote emote = ReadEnum<Emote>(reader.ReadByte());
        var goal = (ushort)ReadUInt(ref reader, ushort.MaxValue);
        MemorySubject? focus = reader.ReadBool() ? ReadSubject(ref reader) : null;
        int focusDistance = (int)ReadUInt(ref reader, int.MaxValue);
        Personality birth = ReadPersonality(ref reader);
        Personality personality = ReadPersonality(ref reader);
        var emotions = new EmotionState(ReadEmotion(ref reader), ReadEmotion(ref reader), ReadEmotion(ref reader), ReadEmotion(ref reader), ReadEmotion(ref reader));
        int needCount = reader.ReadByte();
        if (needCount != NeedCount)
        {
            throw new MalformedMessageException($"Expected {NeedCount} needs, got {needCount}.");
        }

        int[] needs = new int[needCount];
        for (int i = 0; i < needs.Length; i++)
        {
            needs[i] = reader.ReadByte() is byte need && need <= ICreatureMindStore.MaxNeed ? need : throw new MalformedMessageException("Need out of range.");
        }

        int core = (int)ReadUInt(ref reader, ICreatureMindStore.CoreCapacity);
        int episodic = (int)ReadUInt(ref reader, ICreatureMindStore.EpisodicCapacity);
        InspectedMemory[] memories = ReadMemories(ref reader);
        InspectedOpinion[] opinions = ReadOpinions(ref reader);
        var goals = new GoalScore[ReadCount(ref reader, CreatureInspection.MaxGoals)];
        for (int i = 0; i < goals.Length; i++)
        {
            goals[i] = new GoalScore((ushort)ReadUInt(ref reader, ushort.MaxValue), (int)ReadUInt(ref reader, int.MaxValue));
        }

        var treeGoal = (ushort)ReadUInt(ref reader, ushort.MaxValue);
        int treeNode = (int)reader.ReadVarInt(-1, BehaviourTreeDefinition.MaxNodes - 1);
        InspectedCommand? command = reader.ReadBool()
            ? new InspectedCommand(
                ReadEnum<CompanionCommand>(reader.ReadByte()),
                ReadEnum<CommandResponse>(reader.ReadByte()),
                (int)reader.ReadVarInt(int.MinValue, int.MaxValue),
                ReadEnum<ObedienceReason>(reader.ReadByte()),
                reader.ReadVarUInt())
            : null;
        reader.EnsureEnd();
        return new DevInspection(requested, new CreatureInspection(
            tick, id, species, group, tier, position, emote, goal, focus, focusDistance, birth, personality, emotions, needs, core, episodic,
            memories, opinions, goals, treeGoal, treeNode, command));
    }

    private static int Emotion(int value) => Math.Clamp(value, 0, EmotionState.Max);

    private static void WriteSubject(ref MessageWriter writer, MemorySubject subject)
    {
        writer.WriteByte((byte)subject.Kind);
        writer.WriteVarUInt(subject.Id);
    }

    private static MemorySubject ReadSubject(ref MessageReader reader) => new(ReadEnum<SubjectKind>(reader.ReadByte()), reader.ReadVarUInt());

    private static void WritePersonality(ref MessageWriter writer, Personality personality)
    {
        writer.WriteByte((byte)personality.Boldness);
        writer.WriteByte((byte)personality.Loyalty);
        writer.WriteByte((byte)personality.Curiosity);
        writer.WriteByte((byte)personality.Temper);
    }

    private static Personality ReadPersonality(ref MessageReader reader) =>
        new(ReadTrait(ref reader), ReadTrait(ref reader), ReadTrait(ref reader), ReadTrait(ref reader));

    private static sbyte ReadTrait(ref MessageReader reader)
    {
        var trait = (sbyte)reader.ReadByte();
        return trait is >= Personality.MinTrait and <= Personality.MaxTrait ? trait : throw new MalformedMessageException($"Trait {trait} out of range.");
    }

    private static int ReadEmotion(ref MessageReader reader)
    {
        int value = reader.ReadByte();
        return value <= EmotionState.Max ? value : throw new MalformedMessageException($"Emotion {value} out of range.");
    }

    private static void WriteMemories(ref MessageWriter writer, IReadOnlyList<InspectedMemory> memories)
    {
        writer.WriteByte((byte)Math.Min(memories.Count, CreatureInspection.MaxMemories));
        foreach (InspectedMemory memory in memories.Take(CreatureInspection.MaxMemories))
        {
            writer.WriteVarUInt(memory.EventType);
            WriteSubject(ref writer, memory.Subject);
            writer.WriteByte((byte)memory.Valence);
            writer.WriteVarUInt((ulong)Math.Clamp(memory.Salience, 0, ICreatureMindStore.MaxSalience));
            writer.WriteVarUInt((ulong)Math.Clamp(memory.Count, 0, ushort.MaxValue));
            writer.WriteByte((byte)memory.Tags);
            writer.WriteVarUInt(memory.AgeTicks);
        }
    }

    private static InspectedMemory[] ReadMemories(ref MessageReader reader)
    {
        var memories = new InspectedMemory[ReadCount(ref reader, CreatureInspection.MaxMemories)];
        for (int i = 0; i < memories.Length; i++)
        {
            var eventType = (ushort)ReadUInt(ref reader, ushort.MaxValue);
            MemorySubject subject = ReadSubject(ref reader);
            var valence = (sbyte)reader.ReadByte();
            int salience = (int)ReadUInt(ref reader, ICreatureMindStore.MaxSalience);
            int count = (int)ReadUInt(ref reader, ushort.MaxValue);
            byte tags = reader.ReadByte();
            if (tags > MaxMemoryTags || valence < -100 || valence > 100)
            {
                throw new MalformedMessageException($"Memory tags {tags} or valence {valence} out of range.");
            }

            memories[i] = new InspectedMemory(eventType, subject, valence, salience, count, (MemoryTags)tags, reader.ReadVarUInt());
        }

        return memories;
    }

    private static void WriteOpinions(ref MessageWriter writer, IReadOnlyList<InspectedOpinion> opinions)
    {
        writer.WriteByte((byte)Math.Min(opinions.Count, CreatureInspection.MaxOpinions));
        foreach (InspectedOpinion opinion in opinions.Take(CreatureInspection.MaxOpinions))
        {
            WriteSubject(ref writer, opinion.Subject);
            Opinion value = opinion.Opinion.Clamped();
            writer.WriteVarInt(value.Trust);
            writer.WriteVarInt(value.Fear);
            writer.WriteVarInt(value.Affection);
            writer.WriteVarInt(value.Respect);
        }
    }

    private static InspectedOpinion[] ReadOpinions(ref MessageReader reader)
    {
        var opinions = new InspectedOpinion[ReadCount(ref reader, CreatureInspection.MaxOpinions)];
        for (int i = 0; i < opinions.Length; i++)
        {
            MemorySubject subject = ReadSubject(ref reader);
            opinions[i] = new InspectedOpinion(subject, new Opinion(ReadOpinion(ref reader), ReadOpinion(ref reader), ReadOpinion(ref reader), ReadOpinion(ref reader)));
        }

        return opinions;
    }

    private static int ReadOpinion(ref MessageReader reader) => (int)reader.ReadVarInt(Opinion.Min, Opinion.Max);

    private static int ReadCount(ref MessageReader reader, int max)
    {
        int count = reader.ReadByte();
        return count <= max ? count : throw new MalformedMessageException($"{count} entries (max {max}).");
    }

    private static ulong ReadUInt(ref MessageReader reader, long max)
    {
        ulong value = reader.ReadVarUInt();
        return value <= (ulong)max ? value : throw new MalformedMessageException($"Value {value} exceeds {max}.");
    }

    private static T ReadEnum<T>(byte value)
        where T : struct, Enum
    {
        var parsed = (T)Enum.ToObject(typeof(T), value);
        return Enum.IsDefined(parsed) ? parsed : throw new MalformedMessageException($"{typeof(T).Name} {value} is not defined.");
    }
}
