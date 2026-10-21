using Godot;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Data;
using SumonerWorld.Game.Characters;
using SumonerWorld.Game.Creatures;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Game.Playtest;

/// <summary>
/// The player's companions (M2-06) at the top left: each pacted creature with "with you" or "away", from the server's companion
/// list. Z calls the first one that is away (a summoning sigil, AI Bible §10.4), X sends back the last one that is with you; the
/// result line comes through the <see cref="InteractionControls"/>. When a creature arrives, a sigil flares under it. Commands v1
/// (M2-08) go to every companion with you from the action bar (M2-14b; by default 7 attack the lock-on target, else the creature
/// in reach, 8 guard me, 9 hold here, 0 come back); each answers with its body (AI Bible §7.4).
/// </summary>
public sealed partial class CompanionBar(
    GameClient client, IDataRegistry data, EntityViewSync views, InteractionControls controls, Func<ulong> lockedTarget) : CanvasLayer
{
    private const double ArrivalWaitSeconds = 3;

    private readonly HashSet<ulong> _summoned = [];
    private readonly Dictionary<ulong, byte> _combos = [];
    private readonly List<(ulong Id, double At)> _arrivals = [];
    private Label? _list;
    private double _clock;

    /// <summary>The list as shown.</summary>
    public string Shown => _list?.Text ?? string.Empty;

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
        _list = new Label { Position = new Vector2(24, 20) };
        _list.AddThemeFontSizeOverride("font_size", 16);
        _list.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _list.AddThemeConstantOverride("outline_size", 5);
        AddChild(_list);
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
        _list!.Visible = companions.Count > 0;
        _list.Text = companions.Count == 0
            ? string.Empty
            : "Companions\n" + string.Join("\n", companions.Select(c => InteractionText.CompanionLine(NameOf(c), c.Level, (Core.Progression.LifeStage)c.Stage, c.Summoned))) + "\n" + InteractionText.CompanionHints;

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
