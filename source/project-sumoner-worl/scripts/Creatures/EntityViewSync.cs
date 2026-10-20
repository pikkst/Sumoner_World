using Godot;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Game.Creatures;

/// <summary>
/// Shows what the server sends (ADR-001, R38): every frame it advances the client's <see cref="RemoteInterpolation"/>, places one
/// pooled view per remote entity at its interpolated position and heading, and lets the <see cref="IEmotePresenter"/> pose creatures
/// from their emote and ground speed. Views never own state: an entity that leaves the snapshots returns its view to the pool.
/// Creatures get the model of their species' role; other players a simple capsule.
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

    /// <inheritdoc />
    public override void _Process(double delta)
    {
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
            view.Rotation = new Vector3(0, state.Yaw, 0);
            if (view is CreatureRig rig)
            {
                _presenter.Present(rig, (Emote)state.Emote, state.Speed, delta);
            }
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
            view = _playerPool.Count > 0 ? _playerPool.Pop() : AddView(PlayerView());
        }

        view.Visible = true;
        return view;
    }

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

    private static Node3D PlayerView()
    {
        var root = new Node3D { Name = "Player" };
        var cloth = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.3f, 0.55f) };
        root.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.8f, RadialSegments = 8, Rings = 2 }, MaterialOverride = cloth, Position = new Vector3(0, 0.9f, 0) });
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.15f, 0.3f) }, MaterialOverride = cloth, Position = new Vector3(0, 1.5f, 0.35f) });
        return root;
    }
}
