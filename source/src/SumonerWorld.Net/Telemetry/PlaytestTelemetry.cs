using System.Globalization;
using System.Text.Json;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Events;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.World;

namespace SumonerWorld.Net.Telemetry;

/// <summary>
/// The playtest's anonymous session log (QA plan B6): one JSON object per line, written as things happen. Every line has
/// <c>t</c> (seconds since the session started, from the simulation clock) and <c>type</c>:
/// <list type="bullet">
/// <item><c>session_start</c>: <c>session</c> (a random id the host makes), <c>build</c>, <c>creatures</c>.</item>
/// <item><c>player_action</c>: <c>action</c> (feed | attack | help | pact | scare), <c>target_species</c>, <c>affected</c>.</item>
/// <item><c>memory_formed</c>: <c>event_type</c>, <c>salience</c>, <c>core</c>, <c>species</c>.</item>
/// <item><c>emote_shown</c>: <c>emote</c>, <c>in_view</c>, <c>distance</c>, <c>species</c>.</item>
/// <item><c>memory_behaviour_shown</c>: the same plus <c>memory_type</c> and <c>rumour</c> (heard from its group, not lived), when
/// the creature showing it remembers the player and is looking at them.</item>
/// <item><c>session_end</c>: <c>duration</c> and the count of each line type.</item>
/// </list>
/// No personal data: no account, name or machine information, only a random session id.
/// </summary>
public sealed class PlaytestTelemetry : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly TextWriter _writer;
    private readonly IDataRegistry _data;
    private readonly ulong _startTick;
    private readonly Dictionary<string, int> _counts = [];
    private bool _ended;

    /// <summary>Starts a session log at <paramref name="startTick"/>.</summary>
    public PlaytestTelemetry(TextWriter writer, IDataRegistry data, ulong startTick)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(data);
        _writer = writer;
        _data = data;
        _startTick = startTick;
    }

    /// <summary>Lines written so far, by type.</summary>
    public IReadOnlyDictionary<string, int> Counts => _counts;

    /// <summary>The first line; <paramref name="session"/> is a random id from the host (never derived from the player).</summary>
    public void SessionStart(ulong tick, string session, string build, int creatures) =>
        Write(tick, "session_start", new Dictionary<string, object> { ["session"] = session, ["build"] = build, ["creatures"] = creatures });

    /// <summary>A player interaction was applied.</summary>
    public void PlayerAction(ulong tick, in PlayerInteracted action)
    {
        string verb = action.Kind switch
        {
            InteractionKind.Feed => "feed",
            InteractionKind.Hit => "attack",
            InteractionKind.Help => "help",
            InteractionKind.Pact => "pact",
            _ => "scare",
        };
        Write(tick, "player_action", new Dictionary<string, object> { ["action"] = verb, ["target_species"] = Species(action.Species), ["affected"] = action.Affected });
    }

    /// <summary>A memory about the player formed or grew.</summary>
    public void MemoryFormed(ulong tick, in MemoryFormed memory, ushort species) =>
        Write(tick, "memory_formed", new Dictionary<string, object>
        {
            ["event_type"] = EventKey(memory.EventType),
            ["salience"] = memory.Salience,
            ["core"] = memory.Outcome == Core.Cognition.Memory.MemoryOutcome.CoreFormed,
            ["species"] = Species(species),
        });

    /// <summary>
    /// A creature started showing an emote. <paramref name="memoryType"/> is the strongest memory it has about the player when it is
    /// looking at the player (then a <c>memory_behaviour_shown</c> line follows), otherwise null; <paramref name="rumour"/> says
    /// whether that memory was heard from its group.
    /// </summary>
    public void EmoteShown(ulong tick, Emote emote, ushort species, bool inView, float distance, ushort? memoryType, bool rumour = false)
    {
        var fields = new Dictionary<string, object>
        {
            ["emote"] = emote.ToString().ToLowerInvariant(),
            ["in_view"] = inView,
            ["distance"] = Math.Round(distance, 1),
            ["species"] = Species(species),
        };
        Write(tick, "emote_shown", fields);
        if (memoryType is ushort type)
        {
            fields["memory_type"] = EventKey(type);
            fields["rumour"] = rumour;
            Write(tick, "memory_behaviour_shown", fields);
        }
    }

    /// <summary>The last line (written once).</summary>
    public void SessionEnd(ulong tick)
    {
        if (_ended)
        {
            return;
        }

        var fields = new Dictionary<string, object> { ["duration"] = Seconds(tick) };
        foreach ((string type, int count) in _counts)
        {
            fields[type] = count;
        }

        Write(tick, "session_end", fields);
        _ended = true;
        _writer.Flush();
    }

    /// <inheritdoc />
    public void Dispose() => _writer.Dispose();

    private void Write(ulong tick, string type, Dictionary<string, object> fields)
    {
        var line = new Dictionary<string, object> { ["t"] = Seconds(tick), ["type"] = type };
        foreach ((string key, object value) in fields)
        {
            line[key] = value;
        }

        _writer.WriteLine(JsonSerializer.Serialize(line, Json));
        _counts[type] = _counts.GetValueOrDefault(type) + 1;
    }

    private double Seconds(ulong tick) =>
        Math.Round((tick - Math.Min(tick, _startTick)) / (double)SimulationSettings.DefaultTicksPerSecond, 2);

    private string Species(ushort index) => index < _data.Species.Count ? _data.Species[index].Id : "none";

    private string EventKey(ushort id) =>
        _data.MemoryEventTypes.FirstOrDefault(type => type.Id == id)?.Key ?? id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Copies the tick's <see cref="PlayerInteracted"/> and <see cref="Core.World.MemoryFormed"/> events into the telemetry (event
/// dispatch stage, after the transactions that publish them). Playtest only; it allocates when it writes.
/// </summary>
public sealed class TelemetryEventSystem(IEventBus events, CreatureWorld world, PlaytestTelemetry telemetry) : ISimulationSystem
{
    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.EventDispatch;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        foreach (PlayerInteracted action in events.Read<PlayerInteracted>())
        {
            telemetry.PlayerAction(tick.Index, action);
        }

        foreach (MemoryFormed memory in events.Read<MemoryFormed>())
        {
            ushort species = world.TryFindCreature(memory.Creature, out Core.Ecs.Entity creature)
                ? world.World.GetComponent<SpeciesRef>(creature).Index
                : ushort.MaxValue;
            telemetry.MemoryFormed(tick.Index, memory, species);
        }
    }
}
