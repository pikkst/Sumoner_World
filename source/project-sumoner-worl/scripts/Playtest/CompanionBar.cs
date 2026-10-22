using Godot;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Data;
using SumonerWorld.Game.Characters;
using SumonerWorld.Game.Creatures;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Game.Playtest;

/// <summary>
/// The player's companions (M2-06) at the top left, one card each (M2-17): name, level and life stage, and "away" or "with you"
/// with a health bar and its health in words, from the server's companion list and the snapshot. Z calls the first one that is away (a summoning sigil, AI Bible §10.4), X sends back the last one that is with you; the
/// result line comes through the <see cref="InteractionControls"/>. When a creature arrives, a sigil flares under it. Commands v1
/// (M2-08) go to every companion with you from the action bar (M2-14b; by default 7 attack the lock-on target, else the creature
/// in reach, 8 guard me, 9 hold here, 0 come back); each answers with its body (AI Bible §7.4).
/// </summary>
public sealed partial class CompanionBar(
    GameClient client, IDataRegistry data, EntityViewSync views, InteractionControls controls, Func<ulong> lockedTarget, Ui.SettingsWindow settings)
    : CanvasLayer
{
    private const double ArrivalWaitSeconds = 3;
    private const float CardBarWidth = 180f;

    private readonly HashSet<ulong> _summoned = [];
    private readonly Dictionary<ulong, byte> _combos = [];
    private readonly List<(ulong Id, double At)> _arrivals = [];
    private readonly List<(Label Title, Label State, ColorRect Back, ColorRect Fill)> _cards = [];
    private VBoxContainer? _column;
    private Label? _hint;
    private double _clock;

    /// <summary>The cards as text, one line per companion (for the self-test log).</summary>
    public string Shown { get; private set; } = string.Empty;

    /// <summary>Calls the first companion that is away (also used by the self-test); false when there is none.</summary>
    public bool CallNext()
    {
        foreach (CompanionEntry companion in client.Companions)
        {
            if (!companion.Summoned)
            {
                controls.Send(IntentKind.Summon, companion.Id, NameOf(companion));
                return true;
            }
        }

        return false;
    }

    /// <summary>Commands every companion with the player (also used by the self-test).</summary>
    public void Command(CompanionCommand command)
    {
        ulong target = command == CompanionCommand.Attack ? (lockedTarget() is ulong locked and not 0 ? locked : controls.Target.Id) : 0;
        controls.Send(IntentKind.Command, target, controls.Target.Name, (byte)command);
    }

    /// <summary>Asks the companions with the player for a learned move on the target, like an attack command (M2-14c).</summary>
    public void CommandSkill(string move)
    {
        ulong target = lockedTarget() is ulong locked and not 0 ? locked : controls.Target.Id;
        if (data.Moves.Any(definition => definition.Id == move))
        {
            controls.Send(IntentKind.CompanionSkill, target, InteractionText.SkillName(move), (byte)data.MoveIndex(move));
        }
    }

    /// <summary>Sends back the last companion that is with the player; false when there is none.</summary>
    public bool SendBackLast()
    {
        for (int i = client.Companions.Count - 1; i >= 0; i--)
        {
            CompanionEntry companion = client.Companions[i];
            if (companion.Summoned)
            {
                controls.Send(IntentKind.Dismiss, companion.Id, NameOf(companion));
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 4;
        _column = new VBoxContainer { Position = new Vector2(20, 18), MouseFilter = Control.MouseFilterEnum.Ignore };
        _column.AddThemeConstantOverride("separation", 6);
        AddChild(_column);
        _hint = Text(14);
        _hint.Text = HudText.CompanionHint;
        _column.AddChild(_hint);
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || controls.Menu.IsOpen)
        {
            return;
        }

        if (key.Keycode == Key.Z)
        {
            CallNext();
        }
        else if (key.Keycode == Key.X)
        {
            SendBackLast();
        }
        else
        {
            return;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        IReadOnlyList<CompanionEntry> companions = client.Companions;
        ShowCards(companions);

        _clock += delta;
        foreach (CompanionEntry companion in companions)
        {
            AnnounceCombos(companion);
            if (companion.Summoned && _summoned.Add(companion.Id))
            {
                // Marked summoned when the sigil starts; it arrives when the cast is done.
                _arrivals.Add((companion.Id, _clock + (data.Combat.SummonCastTicks / (double)Core.Simulation.SimulationSettings.DefaultTicksPerSecond)));
            }
            else if (!companion.Summoned)
            {
                _summoned.Remove(companion.Id);
            }
        }

        for (int i = _arrivals.Count - 1; i >= 0; i--)
        {
            // A sigil flares under the creature once it has arrived and its view is there (it may still be streaming in).
            if (_clock >= _arrivals[i].At && views.TryGetCreature(_arrivals[i].Id, out Node3D? view) && view is not null)
            {
                view.AddChild(new SigilFlash(1.6f));
                _arrivals.RemoveAt(i);
            }
            else if (_clock >= _arrivals[i].At + ArrivalWaitSeconds)
            {
                _arrivals.RemoveAt(i);
            }
        }
    }

    // One card per companion, made when the list grows; the hint under them while the key hints are on.
    private void ShowCards(IReadOnlyList<CompanionEntry> companions)
    {
        while (_cards.Count < companions.Count)
        {
            AddCard();
        }

        var shown = new System.Text.StringBuilder();
        for (int i = 0; i < _cards.Count; i++)
        {
            (Label title, Label state, ColorRect back, ColorRect fill) = _cards[i];
            bool used = i < companions.Count;
            title.GetParent().GetParent<Control>().Visible = used;
            if (!used)
            {
                continue;
            }

            CompanionEntry companion = companions[i];
            byte? health = companion.Summoned && client.Entities.TryGetValue((SnapshotEntityKind.Creature, companion.Id), out SnapshotEntity entity) ? entity.Health : null;
            title.Text = HudText.CompanionTitle(NameOf(companion), companion.Level, (Core.Progression.LifeStage)companion.Stage);
            state.Text = HudText.CompanionState(companion.Summoned, health);
            state.Modulate = companion.Summoned ? Colors.White : new Color(0.7f, 0.7f, 0.7f);
            back.Visible = health is not null;
            fill.Size = new Vector2(CardBarWidth * (health ?? 0) / 255f, 8);
            shown.Append(title.Text).Append(": ").Append(state.Text).Append(';');
        }

        Shown = shown.ToString().TrimEnd(';');
        _hint!.Visible = companions.Count > 0 && settings.Settings.ShowHints;
    }

    private void AddCard()
    {
        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.45f) };
        style.SetCornerRadiusAll(6);
        style.SetContentMarginAll(6);
        card.AddThemeStyleboxOverride("panel", style);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 2);
        card.AddChild(rows);
        Label title = Text(16);
        Label state = Text(14);
        var back = new ColorRect { Color = new Color(0, 0, 0, 0.6f), CustomMinimumSize = new Vector2(CardBarWidth, 8) };
        var fill = new ColorRect { Color = new Color(0.4f, 0.8f, 0.35f) };
        back.AddChild(fill);
        rows.AddChild(title);
        rows.AddChild(state);
        rows.AddChild(back);
        _column!.AddChild(card);
        _column.MoveChild(_hint, -1);
        _cards.Add((title, state, back, fill));
    }

    private static Label Text(int size)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        label.AddThemeConstantOverride("outline_size", 5);
        return label;
    }

    // A combo bit that was not there before: the companion just learned it (the first list only records what it knows).
    private void AnnounceCombos(CompanionEntry companion)
    {
        if (_combos.TryGetValue(companion.Id, out byte known))
        {
            for (int i = 0; i < data.Combos.Count; i++)
            {
                if ((companion.Combos & ~known & (1 << i)) != 0)
                {
                    controls.Say(InteractionText.ComboLearned(NameOf(companion), data.Combos[i].Id));
                }
            }
        }

        _combos[companion.Id] = companion.Combos;
    }

    private string NameOf(CompanionEntry companion) =>
        InteractionText.DisplayName(companion.Species < data.Species.Count ? data.Species[companion.Species].Id : "creature");
}
