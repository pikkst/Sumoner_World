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
/// Talking to people (Academy spec §1, §3, §6, M2-21; the Vale's people since E5a). Near one, a prompt says "[E] talk to …"; E
/// talks (it counts for talk objectives) and opens the dialogue box: the person's words (the Vale's people greet first and then
/// say their lines in turn), and for the Headmaster the ranking questions one by one (click an answer or press 1–4) and, once
/// all are answered, the ceremony. "Read again" shows the words again; Esc
/// closes. The server checks every answer and decides the class and the rank.
/// </summary>
public sealed partial class AcademyDialogue(
    GameClient client, IDataRegistry data, InteractionControls controls, PeopleView view, ShopWindow shop, Func<Godot.Vector3> player, Func<ulong> creature)
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
    private readonly Dictionary<int, int> _talks = [];

    /// <summary>True while the dialogue box is shown.</summary>
    public bool IsOpen => _window?.Visible ?? false;

    /// <summary>The dialogue as shown (for the self-test log).</summary>
    public string Text => $"{_speaker?.Text}: {_text?.Text} [{string.Join(" | ", _shown?.Options ?? [])}]";

    /// <summary>Talks to the person in reach (E; also used by the self-test); false when nobody is in reach.</summary>
    public bool Talk() => Talk(view.PersonNear(player()));

    /// <summary>Talks to person <paramref name="npc"/> (the self-test picks one of two people standing side by side).</summary>
    public bool Talk(int npc)
    {
        if (npc < 0 || npc >= data.People.Count)
        {
            return false;
        }

        controls.Send(IntentKind.Talk, (ulong)npc, data.People[npc].Name);
        _talks[npc] = _talks.GetValueOrDefault(npc) + 1;
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

        if (page.Kind == DialogueKind.Person)
        {
            // A shop or an inn (E5b).
            if (page.Options[option] == AcademyText.Trade)
            {
                int npc = _npc;
                Close();
                shop.Open(npc);
            }
            else
            {
                controls.Send(IntentKind.Rest, (ulong)_npc, "a rest");
            }
        }
        else if (page.Kind == DialogueKind.Question)
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
        int near = view.PersonNear(player());
        Godot.Vector3 at = player();
        AcademyDefinition? academy = data.Academy;
        _prompt!.Text = IsOpen ? string.Empty
            : near >= 0 ? AcademyText.Prompt(data.People[near])
            : academy is not null && academy.IsSafe(at.X, at.Z) ? "Academy grounds: no fighting here. The arena is just inside the gate."
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

        if (academy is null || _npc >= academy.Npcs.Count)
        {
            // One of the Vale's people (E5a).
            Show(AcademyText.PersonPage(data.People[_npc], _talks.GetValueOrDefault(_npc)), onlyIfChanged: true);
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
        int first = Enumerable.Range(0, data.Quests.Count).FirstOrDefault(i => data.Quests[i].Id == academy.FirstFriendQuest, -1);
        QuestEntry? friend = client.Quests.Any(entry => entry.Quest == first) ? client.Quests.First(entry => entry.Quest == first) : null;
        DialoguePage page = AcademyText.PageOf(academy, _npc, ranking, client.OwnProfile, quest >= 0 ? data.Quests[quest] : null, friend);
        if (_override is string words)
        {
            page = new DialoguePage(academy.Npcs[_npc].Name, words, DialogueKind.Words, 0, []);
        }

        Show(page, onlyIfChanged: true);
    }

    // Shows a page; with onlyIfChanged, nothing happens while the same words are up (the options keep their state).
    private void Show(DialoguePage page, bool onlyIfChanged)
    {
        if (onlyIfChanged && _shown is not null && page.Text == _shown.Text && page.Kind == _shown.Kind)
        {
            return;
        }

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
