using Godot;
using SumonerWorld.Core.Academy;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Game.Playtest;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;
using IntentKind = SumonerWorld.Net.Messages.IntentKind;

namespace SumonerWorld.Game.Ui;

/// <summary>
/// Talking to the Academy's people (Academy spec §1, §3, §6, M2-21). Near one, a prompt says "[E] talk to …"; E talks (it
/// counts for the ranking quest) and opens the dialogue box: the person's words, and for the Headmaster the ranking questions
/// one by one (click an answer or press 1–4) and, once all are answered, the ceremony. "Read again" shows the words again; Esc
/// closes. The server checks every answer and decides the class and the rank.
/// </summary>
public sealed partial class AcademyDialogue(
    GameClient client, IDataRegistry data, InteractionControls controls, AcademyView view, Func<Godot.Vector3> player, Func<ulong> creature)
    : CanvasLayer
{
    private PanelContainer? _window;
    private Label? _speaker;
    private Label? _text;
    private HBoxContainer? _options;
    private Label? _prompt;
    private int _npc = -1;
    private DialoguePage? _shown;
    private uint _ceremony;
    private string? _override;

    /// <summary>True while the dialogue box is shown.</summary>
    public bool IsOpen => _window?.Visible ?? false;

    /// <summary>The dialogue as shown (for the self-test log).</summary>
    public string Text => $"{_speaker?.Text}: {_text?.Text} [{string.Join(" | ", _shown?.Options ?? [])}]";

    /// <summary>Talks to the person in reach (E; also used by the self-test); false when nobody is in reach.</summary>
    public bool Talk() => Talk(view.PersonNear(player()));

    /// <summary>Talks to person <paramref name="npc"/> (the self-test picks one of two people standing side by side).</summary>
    public bool Talk(int npc)
    {
        if (npc < 0 || data.Academy is not AcademyDefinition academy || npc >= academy.Npcs.Count)
        {
            return false;
        }

        controls.Send(IntentKind.Talk, (ulong)npc, academy.Npcs[npc].Name);
        _npc = npc;
        _override = null;
        _shown = null;
        _window!.Visible = true;
        return true;
    }

    /// <summary>Picks option <paramref name="option"/> of the page shown (a click, 1–4; also used by the self-test).</summary>
    public void Choose(int option)
    {
        if (_shown is not DialoguePage page || option < 0 || option >= page.Options.Count)
        {
            return;
        }

        if (page.Kind == DialogueKind.Question)
        {
            client.SendIntent(IntentKind.Answer, (ulong)_npc, (byte)((page.Question * AcademyDefinition.AnswerCount) + option));
            _text!.Text = "...";
        }
        else if (page.Kind == DialogueKind.Read)
        {
            controls.Send(IntentKind.Read, creature(), AcademyText.Feelings[option], (byte)option);
        }
        else if (page.Kind == DialogueKind.Ceremony)
        {
            _ceremony = client.SendIntent(IntentKind.Talk, (ulong)_npc, 0);
            _text!.Text = "The Headmaster looks at you for a long moment...";
        }
    }

    /// <summary>Closes the dialogue box.</summary>
    public void Close()
    {
        _window!.Visible = false;
        _npc = -1;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Layer = 7;
        _prompt = new Label { HorizontalAlignment = HorizontalAlignment.Center, GrowHorizontal = Control.GrowDirection.Both };
        _prompt.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _prompt.Position += new Vector2(0, -230);
        _prompt.AddThemeFontSizeOverride("font_size", 18);
        _prompt.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _prompt.AddThemeConstantOverride("outline_size", 6);
        AddChild(_prompt);
        var strip = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        strip.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        strip.OffsetTop = -330;
        strip.OffsetBottom = -80;
        AddChild(strip);
        _window = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(720, 0) };
        _window.AddThemeStyleboxOverride("panel", ItemLook.Window());
        strip.AddChild(_window);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 10);
        _window.AddChild(rows);
        _speaker = ItemLook.Text(string.Empty, 18);
        _speaker.AddThemeColorOverride("font_color", new Color(1, 0.9f, 0.6f));
        rows.AddChild(_speaker);
        _text = ItemLook.Text(string.Empty, 17);
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rows.AddChild(_text);
        _options = new HBoxContainer();
        _options.AddThemeConstantOverride("separation", 8);
        rows.AddChild(_options);
        var bottom = new HBoxContainer();
        var again = new Button { Text = "Read again", FocusMode = Control.FocusModeEnum.None };
        again.Pressed += () => _shown = null;
        bottom.AddChild(again);
        bottom.AddChild(ItemLook.Text("   [1-6] choose   [Esc] close", 13));
        rows.AddChild(bottom);
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || controls.Menu.IsOpen)
        {
            return;
        }

        if (IsOpen && key.Keycode == Key.Escape)
        {
            Close();
        }
        else if (IsOpen && key.Keycode is >= Key.Key1 and <= Key.Key6)
        {
            Choose((int)(key.Keycode - Key.Key1));
        }
        else if (key.Keycode == Key.E && !IsOpen && view.PersonNear(player()) >= 0)
        {
            Talk();
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
        if (data.Academy is not AcademyDefinition academy)
        {
            return;
        }

        int near = view.PersonNear(player());
        Godot.Vector3 at = player();
        _prompt!.Text = IsOpen ? string.Empty
            : near >= 0 ? AcademyText.Prompt(academy.Npcs[near])
            : academy.IsSafe(at.X, at.Z) ? "Academy grounds: no fighting here. The arena is just inside the gate."
            : string.Empty;
        if (!IsOpen)
        {
            return;
        }

        if (near < 0)
        {
            Close();
            return;
        }

        if (_ceremony != 0 && client.IntentResults.TryGetValue(_ceremony, out Core.Intents.IntentResult result))
        {
            _ceremony = 0;
            _override = (result.Detail & TalkResult.Ranked) != 0 ? AcademyText.Ceremony(academy, result.Detail) : null;
            _shown = null;
        }

        int quest = RankingRules.RankingQuest(data);
        QuestEntry? ranking = client.Quests.Any(entry => entry.Quest == quest) ? client.Quests.First(entry => entry.Quest == quest) : null;
        DialoguePage page = AcademyText.PageOf(academy, _npc, ranking, client.OwnProfile, quest >= 0 ? data.Quests[quest] : null);
        if (_override is string words)
        {
            page = new DialoguePage(academy.Npcs[_npc].Name, words, DialogueKind.Words, 0, []);
        }

        if (page != _shown && (_shown is null || page.Text != _shown.Text || page.Kind != _shown.Kind))
        {
            Show(page);
        }
    }

    private void Show(DialoguePage page)
    {
        _shown = page;
        _speaker!.Text = page.Speaker;
        _text!.Text = page.Text;
        foreach (Node child in _options!.GetChildren())
        {
            child.QueueFree();
        }

        for (int i = 0; i < page.Options.Count; i++)
        {
            int option = i;
            var button = new Button
            {
                Text = page.Kind == DialogueKind.Question ? $"{i + 1}. {page.Options[i]}" : page.Options[i],
                FocusMode = Control.FocusModeEnum.None,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(160, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            button.Pressed += () => Choose(option);
            _options.AddChild(button);
        }
    }
}
