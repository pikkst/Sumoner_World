using Godot;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.World;
using SumonerWorld.Net.Server;
using SumonerWorld.Net.Telemetry;

namespace SumonerWorld.Game.Playtest;

/// <summary>
/// Writes the playtest's session telemetry (QA plan B6) to <c>user://telemetry/session-&lt;UTC time&gt;-&lt;id&gt;.jsonl</c>. The
/// playtest runs server and client in one process, so the recorder sees both sides: interactions and memories from the server's
/// events (<see cref="TelemetryEventSystem"/>), and every emote a creature starts showing, with whether it is on the tester's screen
/// (inside the camera view), its distance to the player and, when the creature is looking at the player and remembers them, the
/// strongest memory about the player.
/// </summary>
public sealed partial class PlaytestRecorder(GameServer server, Func<Camera3D?> camera, ulong account, ILog log) : Node
{
    private const string Build = "memory-grove-m1-23";
    private const float EyeHeight = 0.8f;

    private readonly RecalledMemory[] _recalled = new RecalledMemory[ICreatureMindStore.CoreCapacity + ICreatureMindStore.EpisodicCapacity];
    private Emote[] _shown = [];
    private PlaytestTelemetry? _telemetry;

    /// <summary>Where the file is.</summary>
    public string FilePath { get; private set; } = string.Empty;

    /// <inheritdoc />
    public override void _Ready()
    {
        CreatureWorld world = server.World;
        string session = System.Security.Cryptography.RandomNumberGenerator.GetHexString(16, lowercase: true);
        string stamp = Time.GetDatetimeStringFromSystem(utc: true).Replace(':', '-');
        string folder = ProjectSettings.GlobalizePath("user://telemetry");
        Directory.CreateDirectory(folder);
        FilePath = Path.Combine(folder, $"session-{stamp}-{session[..6]}.jsonl");
        _telemetry = new PlaytestTelemetry(new StreamWriter(FilePath) { AutoFlush = true }, world.Data, world.Clock.Current.Index);
        _telemetry.SessionStart(world.Clock.Current.Index, session, Build, world.World.Store<MindRef>().Count);
        server.AddSystem(new TelemetryEventSystem(world.Events, world, _telemetry));
        _shown = new Emote[world.World.Capacity];
        log.Write(LogLevel.Info, "playtest.telemetry", FilePath);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        CreatureWorld world = server.World;
        if (_telemetry is null || !server.TryGetAvatar(account, out Entity avatar))
        {
            return;
        }

        Vector3 player = ToGodot(world.World.GetComponent<Position>(avatar).Value);
        ComponentStore<CurrentEmote> emotes = world.World.Store<CurrentEmote>();
        ReadOnlySpan<Entity> creatures = emotes.Entities;
        for (int i = 0; i < creatures.Length; i++)
        {
            Emote emote = emotes.Components[i].Value;
            int slot = creatures[i].Index;
            if (emote == _shown[slot])
            {
                continue;
            }

            _shown[slot] = emote;
            if (emote != Emote.None)
            {
                Record(world, creatures[i], avatar, emote, player);
            }
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _telemetry?.SessionEnd(server.World.Clock.Current.Index);
        _telemetry?.Dispose();
        _telemetry = null;
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private void Record(CreatureWorld world, Entity creature, Entity avatar, Emote emote, Vector3 player)
    {
        Vector3 at = ToGodot(world.World.GetComponent<Position>(creature).Value) + new Vector3(0, EyeHeight, 0);
        Camera3D? view = camera();
        bool inView = view is not null && !view.IsPositionBehind(at) && view.IsPositionInFrustum(at);
        FocusRef focus = world.World.GetComponent<FocusRef>(creature);
        ushort? memory = null;
        bool rumour = false;
        if (focus.Has != 0 && focus.Entity == avatar)
        {
            int count = world.Minds.RecallAbout(
                world.World.GetComponent<MindRef>(creature).Handle, new MemorySubject(SubjectKind.Player, account), world.Clock.Current.Index, _recalled);
            memory = count > 0 ? _recalled[0].Record.EventType : null;
            rumour = count > 0 && (_recalled[0].Record.Tags & MemoryTags.Rumour) != 0;
        }

        _telemetry!.EmoteShown(world.Clock.Current.Index, emote, world.World.GetComponent<SpeciesRef>(creature).Index, inView, at.DistanceTo(player), memory, rumour);
    }
}
