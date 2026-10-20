using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Intents;
using SumonerWorld.Game.Creatures;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Sessions;

namespace SumonerWorld.Game.Playtest;

/// <summary>
/// The Memory Grove interactions (M1-23, QA plan B2) on the keyboard: E feed, F hit, R free from a snare, P offer a Pact (first
/// term) to the nearest creature within <see cref="ReachMetres"/>, Q shout. A line at the bottom names the creature in reach and the
/// keys; after each action a short line says what happened (<see cref="InteractionText"/>), never what the creature thinks.
/// </summary>
public sealed partial class InteractionControls : CanvasLayer
{
    /// <summary>How close a creature's centre must be to act on it (largest body radius + touch range + the player's radius).</summary>
    public const float ReachMetres = 5f;

    private const double FeedbackSeconds = 3.5;
    private const byte FirstPactTerm = 0b1;

    private readonly GameClient _client;
    private readonly EntityViewSync _views;
    private readonly IDataRegistry _data;
    private readonly Func<Vector3> _player;
    private readonly List<(uint Sequence, IntentKind Kind, string Creature)> _pending = [];
    private Label? _prompt;
    private Label? _feedback;
    private double _feedbackLeft;

    /// <summary>Creates the controls; <paramref name="player"/> gives the own character's position.</summary>
    public InteractionControls(GameClient client, EntityViewSync views, IDataRegistry data, Func<Vector3> player)
    {
        _client = client;
        _views = views;
        _data = data;
        _player = player;
        Name = "Interactions";
        Layer = 5;
    }

    /// <summary>The last interaction line shown.</summary>
    public string Feedback => _feedback?.Text ?? string.Empty;

    /// <summary>The creature in reach now (0 when none) and its display name.</summary>
    public (ulong Id, string Name) Target { get; private set; }

    /// <summary>Sends one interaction as the player would with a key (also used by the self-test).</summary>
    public void Act(IntentKind kind)
    {
        if (_client.Session.State != ClientSessionState.InSession || (kind != IntentKind.Scare && Target.Id == 0))
        {
            return;
        }

        ulong target = kind == IntentKind.Scare ? 0 : Target.Id;
        byte argument = kind == IntentKind.OfferPact ? FirstPactTerm : (byte)0;
        _pending.Add((_client.SendIntent(kind, target, argument), kind, Target.Name));
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _prompt = Line(new Vector2(0, -70));
        _feedback = Line(new Vector2(0, -150));
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        IntentKind? kind = key.Keycode switch
        {
            Key.E => IntentKind.Feed,
            Key.F => IntentKind.Hit,
            Key.R => IntentKind.Help,
            Key.P => IntentKind.OfferPact,
            Key.Q => IntentKind.Scare,
            _ => null,
        };
        if (kind is IntentKind chosen)
        {
            Act(chosen);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        Target = FindTarget();
        _prompt!.Text = Target.Id == 0 ? InteractionText.ShoutHint : $"{Target.Name}\n{InteractionText.Hints}   {InteractionText.ShoutHint}";

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            (uint sequence, IntentKind kind, string creature) = _pending[i];
            if (_client.IntentResults.TryGetValue(sequence, out IntentResult result))
            {
                _feedback!.Text = InteractionText.Result(kind, result, creature);
                _feedbackLeft = FeedbackSeconds;
                _pending.RemoveAt(i);
            }
        }

        _feedbackLeft -= delta;
        _feedback!.Visible = _feedbackLeft > 0;
    }

    private (ulong Id, string Name) FindTarget()
    {
        Vector3 player = _player();
        (ulong id, float distance) best = (0, ReachMetres);
        foreach ((ulong id, Node3D view) in _views.Creatures)
        {
            Vector3 offset = view.GlobalPosition - player;
            float distance = new Vector2(offset.X, offset.Z).Length();
            if (distance < best.distance)
            {
                best = (id, distance);
            }
        }

        if (best.id == 0 || !_client.Entities.TryGetValue((SnapshotEntityKind.Creature, best.id), out SnapshotEntity entity))
        {
            return (0, string.Empty);
        }

        string species = entity.Species < _data.Species.Count ? _data.Species[entity.Species].Id : "creature";
        return (best.id, InteractionText.DisplayName(species));
    }

    private Label Line(Vector2 offset)
    {
        var label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Begin,
        };
        label.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        label.Position += offset;
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        label.AddThemeConstantOverride("outline_size", 6);
        AddChild(label);
        return label;
    }
}
