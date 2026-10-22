using System.Text;
using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Quests;
using SumonerWorld.Game.Playtest;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using IntentKind = SumonerWorld.Net.Messages.IntentKind;

namespace SumonerWorld.Game.Ui;

/// <summary>
/// Quests on screen (quests spec §5, M2-19). J opens the quest log: the quests offered now with Accept, the quests taken with
/// their step, objectives and Abandon, and the quests done. The tracker at the top right always shows the taken quests'
/// objectives. A line says when a step moves on or a quest is done. J or Esc closes the log. The server decides everything.
/// </summary>
public sealed partial class QuestLog(GameClient client, IDataRegistry data, InteractionControls controls, Func<Godot.Vector3> player) : CanvasLayer
{
    private PanelContainer? _window;
    private VBoxContainer? _rows;
    private Label? _tracker;
    private IReadOnlyList<QuestEntry> _seen = [];
    private string _built = "-";

    /// <summary>True while the log is shown.</summary>
    public bool IsOpen => _window?.Visible ?? false;

    /// <summary>The tracker's text (for the self-test log).</summary>
    public string Tracker => _tracker?.Text ?? string.Empty;

    /// <summary>The log as text: offered, taken and done quests (for the self-test log).</summary>
    public string Describe()
    {
        var text = new StringBuilder("offered: ");
        text.AppendJoin(", ", QuestJournal.Offered(data, client.Quests, client.OwnVitals.Level).Select(quest => data.Quests[quest].Title));
        text.Append("; taken: ").AppendJoin(", ", client.Quests.Where(entry => entry.Status != (byte)QuestStatus.Done).Select(entry => data.Quests[entry.Quest].Title));
        text.Append("; done: ").AppendJoin(", ", client.Quests.Where(entry => entry.Status == (byte)QuestStatus.Done).Select(entry => data.Quests[entry.Quest].Title));
        return text.ToString();
    }

    /// <summary>Opens or closes the log.</summary>
    public void Toggle() => _window!.Visible = !_window.Visible;

    /// <summary>Takes a quest (the Accept button; also used by the self-test).</summary>
    public void Accept(int quest)
    {
        if (quest >= 0 && quest < data.Quests.Count)
        {
            controls.Send(IntentKind.AcceptQuest, 0, data.Quests[quest].Title, (byte)quest);
        }
    }

    /// <summary>Leaves a quest (the Abandon button).</summary>
    public void Abandon(int quest)
    {
        if (quest >= 0 && quest < data.Quests.Count)
        {
            controls.Send(IntentKind.AbandonQuest, 0, data.Quests[quest].Title, (byte)quest);
        }
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 6;
        _tracker = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        _tracker.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _tracker.Position += new Vector2(-380, 20);
        _tracker.CustomMinimumSize = new Vector2(360, 0);
        _tracker.AddThemeFontSizeOverride("font_size", 15);
        _tracker.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _tracker.AddThemeConstantOverride("outline_size", 5);
        AddChild(_tracker);
        _window = new PanelContainer { Visible = false };
        _window.AddThemeStyleboxOverride("panel", ItemLook.Window());
        _window.SetAnchorsPreset(Control.LayoutPreset.Center);
        _window.GrowHorizontal = Control.GrowDirection.Both;
        _window.GrowVertical = Control.GrowDirection.Both;
        _window.CustomMinimumSize = new Vector2(560, 0);
        AddChild(_window);
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 6);
        _window.AddChild(_rows);
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || controls.Menu.IsOpen)
        {
            return;
        }

        if (key.Keycode == Key.J || (IsOpen && key.Keycode == Key.Escape))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        IReadOnlyList<QuestEntry> quests = client.Quests;
        if (!ReferenceEquals(quests, _seen))
        {
            foreach (string line in QuestJournal.Changes(data, _seen, quests))
            {
                controls.Say(line);
            }

            _seen = quests;
        }

        _tracker!.Text = TrackerText(quests);
        string key = $"{client.OwnVitals.Level}|{string.Join(",", quests)}";
        if (IsOpen && key != _built)
        {
            _built = key;
            Rebuild(quests);
        }
    }

    private string TrackerText(IReadOnlyList<QuestEntry> quests)
    {
        var text = new StringBuilder();
        foreach (QuestEntry entry in quests.Where(entry => entry.Status != (byte)QuestStatus.Done))
        {
            text.Append(data.Quests[entry.Quest].Title).Append('\n');
            IReadOnlyList<string> lines = QuestJournal.Objectives(data, entry);
            IReadOnlyList<QuestObjectiveDefinition> objectives = entry.Step < data.Quests[entry.Quest].Steps.Count
                ? data.Quests[entry.Quest].Steps[entry.Step].Objectives
                : [];
            for (int i = 0; i < lines.Count; i++)
            {
                text.Append("  ").Append(lines[i]);
                if (i < objectives.Count && objectives[i].Type == ObjectiveType.Reach && entry[i] == 0)
                {
                    Godot.Vector3 at = player();
                    text.Append(" (").Append(InteractionText.Direction(objectives[i].X - at.X, objectives[i].Z - at.Z)).Append(')');
                }

                text.Append('\n');
            }
        }

        return text.Length == 0 && QuestJournal.Offered(data, quests, client.OwnVitals.Level).Count > 0 ? "[J] new quests" : text.ToString().TrimEnd();
    }

    private void Rebuild(IReadOnlyList<QuestEntry> quests)
    {
        foreach (Node child in _rows!.GetChildren())
        {
            child.QueueFree();
        }

        _rows.AddChild(ItemLook.Text("Quests", 19));
        IReadOnlyList<int> offered = QuestJournal.Offered(data, quests, client.OwnVitals.Level);
        Section("Offered", offered.Count == 0 ? "(nothing new for now)" : null);
        foreach (int quest in offered)
        {
            QuestDefinition definition = data.Quests[quest];
            AddQuest(definition, $"{definition.Summary}\nReward: {InteractionText.RewardsLine(definition.Rewards)}", "Accept", () => Accept(quest));
        }

        List<QuestEntry> taken = [.. quests.Where(entry => entry.Status != (byte)QuestStatus.Done)];
        Section("Taken", taken.Count == 0 ? "(none)" : null);
        foreach (QuestEntry entry in taken)
        {
            QuestDefinition definition = data.Quests[entry.Quest];
            string step = entry.Step < definition.Steps.Count ? definition.Steps[entry.Step].Text : string.Empty;
            string status = entry.Status == (byte)QuestStatus.Ready ? "\nDone! Make room in your bag for the reward." : string.Empty;
            AddQuest(definition, $"{step}\n  {string.Join("\n  ", QuestJournal.Objectives(data, entry))}{status}", "Abandon", () => Abandon(entry.Quest));
        }

        List<string> done = [.. quests.Where(entry => entry.Status == (byte)QuestStatus.Done).Select(entry => data.Quests[entry.Quest].Title)];
        Section("Done", done.Count == 0 ? "(none yet)" : string.Join(", ", done));
        _rows.AddChild(ItemLook.Text("[J/Esc] close", 12));
    }

    private void Section(string title, string? note)
    {
        _rows!.AddChild(ItemLook.Text(title, 16));
        if (note is not null)
        {
            _rows.AddChild(ItemLook.Text(note, 13));
        }
    }

    private void AddQuest(QuestDefinition quest, string body, string action, Action pressed)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddChild(ItemLook.Text(quest.Title, 15));
        var detail = ItemLook.Text(body, 13);
        detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.AddChild(detail);
        row.AddChild(text);
        var button = new Button { Text = action, FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        button.Pressed += pressed;
        row.AddChild(button);
        _rows!.AddChild(row);
    }
}
