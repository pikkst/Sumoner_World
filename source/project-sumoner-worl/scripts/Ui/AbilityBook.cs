using Godot;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Data;
using SumonerWorld.Game.Playtest;
using SumonerWorld.Net.Client;

namespace SumonerWorld.Game.Ui;

/// <summary>
/// The ability book (M2-14b): L opens it. It shows the spells of the player's class's school (E3a) and the companion commands as
/// tiles; drag one onto the action bar to put it on a key. Clicking a tile uses it once. L or Esc closes it.
/// </summary>
public sealed partial class AbilityBook(IDataRegistry data, InteractionControls controls, Action<ActionRef> use, Func<Core.Progression.SummonerClass> summonerClass)
    : CanvasLayer
{
    private PanelContainer? _window;
    private VBoxContainer? _rows;
    private Core.Progression.SummonerClass? _shownFor;

    /// <summary>True while it is shown.</summary>
    public bool IsOpen => _window?.Visible ?? false;

    /// <summary>Every action in the book, in order.</summary>
    public IReadOnlyList<ActionRef> Actions =>
    [
        .. ClassSpells.Of(data, summonerClass()).Select(spell => new ActionRef(ActionKind.Spell, spell)),
        .. new[] { CompanionCommand.Attack, CompanionCommand.GuardMe, CompanionCommand.Hold, CompanionCommand.Retreat }
            .Select(command => new ActionRef(ActionKind.Command, command.ToString())),
    ];

    /// <summary>Opens or closes the book.</summary>
    public void Toggle() => _window!.Visible = !_window.Visible;

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 6;
        _window = new PanelContainer { Visible = false };
        _window.AddThemeStyleboxOverride("panel", ItemLook.Window());
        _window.SetAnchorsPreset(Control.LayoutPreset.CenterLeft);
        _window.Position += new Vector2(24, -170);
        AddChild(_window);
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 8);
        _window.AddChild(_rows);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        Core.Progression.SummonerClass now = summonerClass();
        if (now == _shownFor)
        {
            return;
        }

        _shownFor = now;
        foreach (Node child in _rows!.GetChildren())
        {
            child.QueueFree();
        }

        _rows.AddChild(ItemLook.Text("Abilities", 19));
        AddSection(_rows, $"{InteractionText.SchoolName(Core.Progression.Schools.Of(now))} spells", Actions.Where(action => action.Kind == ActionKind.Spell));
        AddSection(_rows, "Companion commands", Actions.Where(action => action.Kind == ActionKind.Command));
        _rows.AddChild(ItemLook.Text("Drag onto the bar (keys 1-0)   click: use   [L/Esc] close", 12));
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || controls.Menu.IsOpen)
        {
            return;
        }

        if (key.Keycode == Key.L || (IsOpen && key.Keycode == Key.Escape))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    private void AddSection(VBoxContainer rows, string title, IEnumerable<ActionRef> actions)
    {
        rows.AddChild(ItemLook.Text(title, 14));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        rows.AddChild(row);
        foreach (ActionRef action in actions)
        {
            var tile = new UiSlot(new DragSpot(DragPlace.Action, 0, 0, action.ToString()))
            {
                Clicked = spot => use(ActionRef.Parse(spot.Id)),
            };
            string tooltip = action.Kind == ActionKind.Spell && data.Spells.FirstOrDefault(spell => spell.Id == action.Id) is { } spell
                ? $"{InteractionText.ActionName(action)}\n{spell.Mana} mana"
                : InteractionText.ActionName(action);
            tile.Show(ItemLook.Action(action, tooltip));
            row.AddChild(tile);
        }
    }
}
