using System.Globalization;
using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Progression;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Game.Playtest;

/// <summary>
/// The companion panel (companions spec §10, M2-10): K opens it. It shows one companion at a time (Left / Right switch): level,
/// life stage, bond level and skill points, then its skill tree, one column per branch with how the creature feels about it
/// (warm when it likes it, cold when it dislikes it and its skills cost 2), and each skill with what it gives and whether it is
/// learned, can be learned or what it still needs. Click a skill, or press its number (1–9), to teach it; drag a learned move
/// onto the action bar to call for it in a fight (M2-14c); K or Esc closes.
/// Everything comes from the server's companion list; the server checks every choice.
/// </summary>
public sealed partial class CompanionPanel(GameClient client, IDataRegistry data, InteractionControls controls) : CanvasLayer
{
    private static readonly Color Warm = new(0.95f, 0.65f, 0.35f);
    private static readonly Color Cold = new(0.5f, 0.7f, 0.95f);
    private static readonly Color Neutral = new(0.9f, 0.9f, 0.9f);

    private PanelContainer? _panel;
    private Label? _title;
    private HBoxContainer? _columns;
    private int _selected;
    private CompanionEntry _shown;
    private bool _dirty = true;

    /// <summary>True while the panel is shown.</summary>
    public bool IsOpen => _panel?.Visible ?? false;

    /// <summary>The panel's text as shown (title and every skill line, for the self-test log).</summary>
    public string Shown { get; private set; } = string.Empty;

    /// <summary>Opens or closes the panel.</summary>
    public void Toggle()
    {
        _panel!.Visible = !_panel.Visible;
        _dirty = true;
    }

    /// <summary>Asks the server to teach the shown companion skill <paramref name="node"/>.</summary>
    public void Learn(int node)
    {
        if (Selected() is CompanionEntry companion)
        {
            controls.Send(SumonerWorld.Net.Messages.IntentKind.LearnSkill, companion.Id, NameOf(companion), (byte)node);
        }
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 6;
        _panel = new PanelContainer { Visible = false };
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Both;
        AddChild(_panel);
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        }

        _panel.AddChild(margin);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 10);
        margin.AddChild(rows);
        _title = new Label();
        _title.AddThemeFontSizeOverride("font_size", 19);
        rows.AddChild(_title);
        _columns = new HBoxContainer();
        _columns.AddThemeConstantOverride("separation", 18);
        rows.AddChild(_columns);
        var hints = new Label { Text = "[1-9] or click: teach a skill   drag a learned move onto the bar   [Left/Right] other companion   [K/Esc] close" };
        hints.AddThemeFontSizeOverride("font_size", 13);
        rows.AddChild(hints);
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (key.Keycode == Key.K && !controls.Menu.IsOpen)
        {
            Toggle();
        }
        else if (!IsOpen)
        {
            return;
        }
        else if (key.Keycode == Key.Escape)
        {
            Toggle();
        }
        else if (key.Keycode is Key.Left or Key.Right)
        {
            _selected += key.Keycode == Key.Right ? 1 : -1;
            _dirty = true;
        }
        else if (key.Keycode is >= Key.Key1 and <= Key.Key9)
        {
            Learn((int)(key.Keycode - Key.Key1));
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
        if (!IsOpen)
        {
            return;
        }

        CompanionEntry? selected = Selected();
        if (!_dirty && selected is CompanionEntry now && now == _shown)
        {
            return;
        }

        _dirty = false;
        _shown = selected ?? default;
        Rebuild(selected);
    }

    private CompanionEntry? Selected()
    {
        IReadOnlyList<CompanionEntry> companions = client.Companions;
        if (companions.Count == 0)
        {
            return null;
        }

        _selected = ((_selected % companions.Count) + companions.Count) % companions.Count;
        return companions[_selected];
    }

    private void Rebuild(CompanionEntry? selected)
    {
        foreach (Node child in _columns!.GetChildren())
        {
            child.QueueFree();
        }

        if (selected is not CompanionEntry companion)
        {
            _title!.Text = "No companions yet. Offer a Pact [P] to a creature that trusts you.";
            Shown = _title.Text;
            return;
        }

        string bond = companion.BondLevel < data.BondLevels.Count ? data.BondLevels[companion.BondLevel].Key : $"bond {companion.BondLevel}";
        _title!.Text = $"{NameOf(companion)}  Lv {companion.Level} ({((LifeStage)companion.Stage).ToString().ToLowerInvariant()})  ·  {bond}  ·  "
            + $"{companion.Points} skill point{(companion.Points == 1 ? string.Empty : "s")}  ·  {SkillRules.Slots((LifeStage)companion.Stage)} active slots";
        var text = new System.Text.StringBuilder(_title.Text);
        if (data.SkillTreeOf(companion.Species) is not SkillTreeDefinition tree)
        {
            Shown = text.ToString();
            return;
        }

        for (int b = 0; b < tree.Branches.Count; b++)
        {
            int affinity = b < companion.Affinity.Count ? companion.Affinity[b] : 0;
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 6);
            _columns.AddChild(column);
            var header = new Label { Text = $"{InteractionText.SkillName(tree.Branches[b].Id)}\n({InteractionText.BranchFeeling(affinity)})" };
            header.AddThemeFontSizeOverride("font_size", 16);
            header.AddThemeColorOverride("font_color", affinity >= SkillRules.WarmAt ? Warm : affinity <= SkillRules.ColdAt ? Cold : Neutral);
            column.AddChild(header);
            text.Append(CultureInfo.InvariantCulture, $"\n{InteractionText.SkillName(tree.Branches[b].Id)} ({InteractionText.BranchFeeling(affinity)}):");
            for (int n = 0; n < tree.Nodes.Count; n++)
            {
                if (tree.Nodes[n].Branch != tree.Branches[b].Id)
                {
                    continue;
                }

                SkillNodeDefinition node = tree.Nodes[n];
                SkillState state = SkillRules.StateOf(
                    tree, n, companion.Level, (LifeStage)companion.Stage, companion.BondLevel, companion.Learned, companion.Points, affinity);
                string line = $"[{n + 1}] {InteractionText.SkillName(node.Id)}  Lv {node.Level}\n{InteractionText.SkillEffect(node)}\n{InteractionText.SkillStateText(state)}";
                var button = new Button { Text = line, Disabled = state != SkillState.Available, FocusMode = Control.FocusModeEnum.None };
                button.AddThemeFontSizeOverride("font_size", 13);
                int index = n;
                button.Pressed += () => Learn(index);
                if (state == SkillState.Learned)
                {
                    button.AddThemeColorOverride("font_disabled_color", new Color(0.6f, 0.95f, 0.6f));
                }

                if (state == SkillState.Learned && node.Move is string move)
                {
                    var row = new HBoxContainer();
                    row.AddThemeConstantOverride("separation", 4);
                    var skill = new ActionRef(ActionKind.CompanionSkill, move);
                    var tile = new Ui.UiSlot(new DragSpot(DragPlace.Action, 0, 0, skill.ToString()));
                    tile.Show(Ui.ItemLook.Action(skill, $"{InteractionText.SkillName(move)}: drag onto the bar"));
                    row.AddChild(tile);
                    row.AddChild(button);
                    column.AddChild(row);
                }
                else
                {
                    column.AddChild(button);
                }

                text.Append(CultureInfo.InvariantCulture, $" {InteractionText.SkillName(node.Id)}={state}");
            }
        }

        Shown = text.ToString();
    }

    private string NameOf(CompanionEntry companion) =>
        InteractionText.DisplayName(companion.Species < data.Species.Count ? data.Species[companion.Species].Id : "creature");
}
