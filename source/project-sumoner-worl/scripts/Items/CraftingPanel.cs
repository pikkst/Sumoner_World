using System.Globalization;
using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Game.Playtest;
using SumonerWorld.Game.Ui;
using SumonerWorld.Net.Client;
using IntentKind = SumonerWorld.Net.Messages.IntentKind;

namespace SumonerWorld.Game.Items;

/// <summary>
/// The crafting window (crafting spec §6, M2-14): B opens it. Every recipe with what it needs and whether the bag has it; click a
/// recipe, or press its number, to start making it. It shows what is being made, and a line says when it is done or was stopped.
/// B or Esc closes it. The server checks everything.
/// </summary>
public sealed partial class CraftingPanel(GameClient client, IDataRegistry data, InteractionControls controls) : CanvasLayer
{
    private readonly List<Button> _recipes = [];
    private PanelContainer? _window;
    private Label? _making;
    private int _wasCrafting;
    private int _outputBefore;

    /// <summary>True while the window is shown.</summary>
    public bool IsOpen => _window?.Visible ?? false;

    /// <summary>The window's text as shown (for the self-test log).</summary>
    public string Text => string.Join("; ", _recipes.Select(button => button.Text)) + "; " + _making?.Text;

    /// <summary>Opens or closes the window.</summary>
    public void Toggle() => _window!.Visible = !_window.Visible;

    /// <summary>Starts making recipe <paramref name="recipe"/> (also used by the self-test).</summary>
    public void Craft(int recipe)
    {
        if (recipe >= 0 && recipe < data.Recipes.Count)
        {
            controls.Send(IntentKind.Craft, 0, InteractionText.SkillName(data.Recipes[recipe].Output.Item), (byte)recipe);
        }
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 6;
        _window = new PanelContainer { Visible = false };
        _window.AddThemeStyleboxOverride("panel", ItemLook.Window());
        _window.SetAnchorsPreset(Control.LayoutPreset.CenterLeft);
        _window.Position += new Vector2(24, -200);
        AddChild(_window);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 6);
        _window.AddChild(rows);
        rows.AddChild(ItemLook.Text("Crafting (by hand)", 19));
        for (int i = 0; i < data.Recipes.Count; i++)
        {
            int recipe = i;
            var button = new Button { Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None };
            button.Pressed += () => Craft(recipe);
            _recipes.Add(button);
            rows.AddChild(button);
        }

        _making = ItemLook.Text(string.Empty, 14);
        rows.AddChild(_making);
        rows.AddChild(ItemLook.Text("Click or [1-9]: make   [B/Esc] close", 12));
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || controls.Menu.IsOpen)
        {
            return;
        }

        if (key.Keycode == Key.B || (IsOpen && key.Keycode == Key.Escape))
        {
            Toggle();
        }
        else if (IsOpen && key.Keycode is >= Key.Key1 and <= Key.Key9)
        {
            Craft((int)(key.Keycode - Key.Key1));
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
        int crafting = client.Inventory.Crafting;
        if (crafting != _wasCrafting && crafting > 0 && crafting - 1 < data.Recipes.Count)
        {
            _outputBefore = CountOf(data.Recipes[crafting - 1].Output.Item);
        }
        else if (_wasCrafting > 0 && crafting == 0 && _wasCrafting - 1 < data.Recipes.Count)
        {
            string output = data.Recipes[_wasCrafting - 1].Output.Item;
            controls.Say(InteractionText.CraftEnded(InteractionText.SkillName(output), CountOf(output) > _outputBefore));
        }

        _wasCrafting = crafting;
        if (!IsOpen)
        {
            return;
        }

        for (int i = 0; i < _recipes.Count; i++)
        {
            Core.Data.Definitions.RecipeDefinition recipe = data.Recipes[i];
            bool ready = recipe.Inputs.All(part => CountOf(part.Item) >= part.Count);
            _recipes[i].Text = string.Create(CultureInfo.InvariantCulture, $"[{i + 1}] {InteractionText.RecipeLine(recipe)}");
            _recipes[i].Modulate = ready ? new Color(1, 1, 1) : new Color(0.6f, 0.6f, 0.6f);
        }

        _making!.Text = crafting > 0 && crafting - 1 < data.Recipes.Count
            ? $"Making: {InteractionText.SkillName(data.Recipes[crafting - 1].Output.Item)}..."
            : string.Empty;
    }

    private int CountOf(string item)
    {
        int index = data.ItemIndex(item);
        return client.Inventory.Entries.Where(entry => entry.Item == index).Sum(entry => entry.Count);
    }
}
