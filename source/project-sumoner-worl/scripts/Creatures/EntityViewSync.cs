using Godot;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Progression;
using SumonerWorld.Game.Characters;
using SumonerWorld.Game.Combat;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Game.Creatures;

/// <summary>
/// Shows what the server sends (ADR-001, R38): every frame it advances the client's <see cref="RemoteInterpolation"/>, places one
/// pooled view per remote entity at its interpolated position and heading, and lets the <see cref="IEmotePresenter"/> pose creatures
/// from their emote and ground speed. Views never own state: an entity that leaves the snapshots returns its view to the pool.
/// Creatures get the model of their species' role; other players the <see cref="HumanoidRig"/>, animated from their ground speed
/// and combat action (M2-04). Combat (M2-02b): each view tilts with its combat action, swells briefly (players also flinch) when
/// its health drops, and shows a health bar while hurt.
/// </summary>
public sealed partial class EntityViewSync : Node3D
{
    private readonly GameClient _client;
    private readonly DataRegistry _data;
    private readonly IEmotePresenter _presenter;
    private readonly List<RemoteState> _states = [];
    private readonly Dictionary<(SnapshotEntityKind, ulong), Node3D> _views = [];
    private readonly Dictionary<CreatureRole, Stack<CreatureRig>> _creaturePool = [];
    private readonly Stack<Node3D> _playerPool = new();
    private readonly HashSet<(SnapshotEntityKind, ulong)> _seen = [];
    private readonly List<(SnapshotEntityKind, ulong)> _gone = [];
    private readonly Dictionary<Node3D, (HealthBar3D Bar, byte Health)> _health = [];
    private readonly Dictionary<Node3D, (StatusMarksView Marks, byte Action)> _marks = [];
    private double _time;

    /// <summary>Creates the sync for a client; <paramref name="data"/> maps species indices to roles.</summary>
    public EntityViewSync(GameClient client, DataRegistry data, IEmotePresenter presenter)
    {
        _client = client;
        _data = data;
        _presenter = presenter;
        Name = "EntityViews";
    }

    /// <summary>Views shown now.</summary>
    public int Shown => _views.Count;

    /// <summary>Creature views shown now, by stable id (the dev tools pick from these).</summary>
    public IEnumerable<(ulong Id, Node3D View)> Creatures =>
        _views.Where(entry => entry.Key.Item1 == SnapshotEntityKind.Creature).Select(entry => (entry.Key.Item2, entry.Value));

    /// <summary>The view of a creature shown now.</summary>
    public bool TryGetCreature(ulong id, out Node3D? view) => _views.TryGetValue((SnapshotEntityKind.Creature, id), out view);

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _time += delta;
        _client.Remote.Advance(delta);
        _client.Remote.Sample(_states);
        _seen.Clear();
        foreach (RemoteState state in _states)
        {
            var key = (state.Kind, state.Id);
            _seen.Add(key);
            if (!_views.TryGetValue(key, out Node3D? view))
            {
                view = Take(state);
                _views.Add(key, view);
            }

            view.Position = new Vector3(state.Position.X, state.Position.Y, state.Position.Z);
            CombatPose.Apply(view, state.Action, state.Yaw, _time);
            if (view is CreatureRig rig)
            {
                if (rig.Stage != (LifeStage)state.Stage)
                {
                    rig.SetStage((LifeStage)state.Stage);
                }

                _presenter.Present(rig, (Emote)state.Emote, state.Speed, delta);
            }
            else if (view is HumanoidRig humanoid)
            {
                HumanoidAnimator.Animate(humanoid, (CombatAction)state.Action, state.Speed, delta);
            }

            ShowHealth(view, state.Health, delta);
            ShowSpells(view, state);
        }

        foreach (var key in _views.Keys)
        {
            if (!_seen.Contains(key))
            {
                _gone.Add(key);
            }
        }

        foreach (var key in _gone)
        {
            Return(_views[key]);
            _views.Remove(key);
        }

        _gone.Clear();
    }

    private Node3D Take(RemoteState state)
    {
        Node3D view;
        if (state.Kind == SnapshotEntityKind.Creature)
        {
            CreatureRole role = state.Species < _data.Species.Count ? _data.Species[state.Species].Role : CreatureRole.Grazer;
            role = role == CreatureRole.Predator ? role : CreatureRole.Grazer;
            if (!_creaturePool.TryGetValue(role, out Stack<CreatureRig>? pool))
            {
                pool = new Stack<CreatureRig>();
                _creaturePool.Add(role, pool);
            }

            CreatureRig rig = pool.Count > 0 ? pool.Pop() : (CreatureRig)AddView(CreatureRig.Build(role));
            rig.Clock = state.Id % 97 * 0.31;
            view = rig;
        }
        else
        {
            view = _playerPool.Count > 0 ? _playerPool.Pop() : AddView(HumanoidRig.Build(OtherRobe));
        }

        view.Visible = true;
        if (!_health.TryGetValue(view, out (HealthBar3D Bar, byte Health) shown))
        {
            shown = (new HealthBar3D(state.Kind == SnapshotEntityKind.Creature ? CreatureBarHeight : PlayerBarHeight), byte.MaxValue);
            view.AddChild(shown.Bar);
        }

        _health[view] = (shown.Bar, state.Health);
        return view;
    }

    private const float CreatureBarHeight = 2.6f;
    private const float PlayerBarHeight = 2.2f;

    private void ShowHealth(Node3D view, byte health, double delta)
    {
        (HealthBar3D bar, byte before) = _health[view];
        if (health < before)
        {
            CombatPose.Hit(view, SizeOf(view));
            if (view is HumanoidRig humanoid)
            {
                HumanoidAnimator.Hit(humanoid);
            }
        }

        CombatPose.Settle(view, delta, SizeOf(view));
        bar.SetHealth(health);
        _health[view] = (bar, health);
    }

    // Status marks on every body, and a thornbolt's streak when someone's cast turns into the spell's flight (M2-11).
    private void ShowSpells(Node3D view, RemoteState state)
    {
        if (!_marks.TryGetValue(view, out (StatusMarksView Marks, byte Action) shown))
        {
            shown = (new StatusMarksView(state.Kind == SnapshotEntityKind.Creature ? 1.1f : 0.6f), 0);
            view.AddChild(shown.Marks);
        }

        shown.Marks.Show((StatusMarks)state.Status);
        bool flight = shown.Action == (byte)CombatAction.Cast && state.Action is (byte)CombatAction.Strike or (byte)CombatAction.Recover;
        if (flight && state.Move > 0 && state.Move - 1 < _data.Moves.Count && _data.Moves[state.Move - 1].Tags.Contains("bolt"))
        {
            AddChild(new BoltStreak(view.Position, state.Yaw, _data.Moves[state.Move - 1].Shape.ReachCm / 100f));
        }

        _marks[view] = (shown.Marks, state.Action);
    }

    private static float SizeOf(Node3D view) => view is CreatureRig rig ? rig.Size : 1f;

    private void Return(Node3D view)
    {
        view.Visible = false;
        if (view is CreatureRig rig)
        {
            _creaturePool[rig.Role].Push(rig);
        }
        else
        {
            _playerPool.Push(view);
        }
    }

    private Node3D AddView(Node3D view)
    {
        AddChild(view);
        return view;
    }

    /// <summary>Robe colour of other players (the own character wears <see cref="OwnRobe"/>).</summary>
    public static readonly Color OtherRobe = new(0.26f, 0.36f, 0.6f);

    /// <summary>Robe colour of the own character.</summary>
    public static readonly Color OwnRobe = new(0.62f, 0.3f, 0.24f);
}
