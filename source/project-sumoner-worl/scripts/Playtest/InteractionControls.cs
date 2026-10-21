using Godot;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Intents;
using SumonerWorld.Game.Creatures;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Sessions;

namespace SumonerWorld.Game.Playtest;

/// <summary>
/// The Memory Grove interactions (M1-23, QA plan B2) on the keyboard: E feed, F hit, R free from a snare, P opens the
/// <see cref="PactMenu"/> to offer a Pact (M2-05) to the nearest creature within <see cref="ReachMetres"/>, Q shout. A line at the
/// bottom names the creature in reach and the keys; after each action a short line says what happened (<see cref="InteractionText"/>),
/// never what the creature thinks. A Pact's answer line waits <see cref="InteractionText.PactAnswerDelaySeconds"/>, so the creature's
/// body answers first.
/// </summary>
public sealed partial class InteractionControls : CanvasLayer
{
    /// <summary>How close a creature's centre must be to act on it (largest body radius + touch range + the player's radius).</summary>
    public const float ReachMetres = 5f;

    private const double FeedbackSeconds = 3.5;

    private readonly GameClient _client;
    private readonly EntityViewSync _views;
    private readonly IDataRegistry _data;
    private readonly Func<Vector3> _player;
    private readonly List<(uint Sequence, IntentKind Kind, string Creature, byte Argument)> _pending = [];
    private readonly List<(double ShowAt, string Line)> _delayed = [];
    private (ulong Id, string Name) _pactTarget;
    private double _clock;
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

    /// <summary>The Pact menu.</summary>
    public PactMenu Menu { get; private set; } = null!;

    /// <summary>The last interaction line shown.</summary>
    public string Feedback => _feedback?.Text ?? string.Empty;

    /// <summary>The creature in reach now (0 when none) and its display name.</summary>
    public (ulong Id, string Name) Target { get; private set; }

    /// <summary>Ground distance from the player to the target's centre, in metres.</summary>
    public float TargetDistance { get; private set; }

    /// <summary>Sends one interaction as the player would with a key (also used by the self-test).</summary>
    public void Act(IntentKind kind)
    {
        if (_client.Session.State != ClientSessionState.InSession || (kind != IntentKind.Scare && Target.Id == 0))
        {
            return;
        }

        if (kind == IntentKind.OfferPact)
        {
            _pactTarget = Target;
            Menu.Open(Target.Name);
            return;
        }

        ulong target = kind == IntentKind.Scare ? 0 : Target.Id;
        _pending.Add((_client.SendIntent(kind, target, 0), kind, Target.Name, 0));
    }

    /// <summary>Shows a line at once (news from the server, e.g. a combo learned).</summary>
    public void Say(string line) => _delayed.Add((_clock, line));

    /// <summary>Sends an intent about a creature and shows its result line like the keys' (the companion keys use this).</summary>
    public void Send(IntentKind kind, ulong creature, string name, byte argument = 0)
    {
        if (_client.Session.State == ClientSessionState.InSession)
        {
            _pending.Add((_client.SendIntent(kind, creature, argument), kind, name, argument));
        }
    }

    // The Pact menu's offer, to the creature that was in reach when it opened.
    private void OfferPact(PactTerms terms)
    {
        if (_client.Session.State == ClientSessionState.InSession && _pactTarget.Id != 0)
        {
            _pending.Add((_client.SendIntent(IntentKind.OfferPact, _pactTarget.Id, terms.Mask), IntentKind.OfferPact, _pactTarget.Name, terms.Mask));
        }
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _prompt = Line(new Vector2(0, -110));
        _feedback = Line(new Vector2(0, -190));
        Menu = new PactMenu(_data, OfferPact);
        AddChild(Menu);
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || Menu.IsOpen)
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
            (uint sequence, IntentKind kind, string creature, byte argument) = _pending[i];
            if (_client.IntentResults.TryGetValue(sequence, out IntentResult result))
            {
                bool answered = kind == IntentKind.OfferPact && result.IsApplied;
                _delayed.Add((_clock + (answered ? InteractionText.PactAnswerDelaySeconds : 0), InteractionText.Result(kind, result, creature, argument)));
                _pending.RemoveAt(i);
            }
        }

        _clock += delta;
        for (int i = _delayed.Count - 1; i >= 0; i--)
        {
            if (_clock >= _delayed[i].ShowAt)
            {
                _feedback!.Text = _delayed[i].Line;
                _feedbackLeft = FeedbackSeconds;
                _delayed.RemoveAt(i);
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

        TargetDistance = best.distance;
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
