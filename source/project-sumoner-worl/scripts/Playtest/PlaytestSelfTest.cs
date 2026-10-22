using Godot;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Game.Combat;
using SumonerWorld.Game.Hosting;
using SumonerWorld.Net.Client;

namespace SumonerWorld.Game.Playtest;

/// <summary>
/// Plays the start of a Memory Grove session through the real controls (<c>--self-test</c>, M1-23, M2-02b): queues input events,
/// so input goes the same way as a tester's. Holds W until a creature is within 3 m (the snared calf 12 m ahead), then, one step
/// apart: a left click (a basic attack at the calf while it is still snared, so its health bar shows), T (lock on to it), a right-drag that the
/// lock ignores, T again (let go), a Pact while it is still snared (M2-05: P opens the menu, 1 and 2 choose two promises,
/// <paramref name="capture"/> saves frames of the menu and of the answer, Enter offers it), R (free it), E (feed it), Q (shout), a
/// right-button drag that turns the camera a quarter round, Space held for a dodge, then Z (summon the companion the
/// <see cref="SelfTestFixture"/> gave), W held for a moment (it follows, M2-07), the commands 2 (guard me) and 3 (hold here),
/// W again (it holds), 4 (come back, M2-08), X (send it back), and the companion panel (K, M2-10): it logs the skill tree and
/// teaches skill 2; then the spells (M2-11): C thornbolt, V entangle, G mend; the inventory (I, M2-13); crafting (B, M2-14): it makes
/// a wooden staff from the branches the fixture gave; the character window (I, M2-14b): it drags the charm onto a charm slot,
/// calls the owl and drags berries into its bag. Spells and commands go through the action bar (1-3, 8-0).
///  It logs the interaction lines, the attack's result and the calf's health, the own stamina, the calf's
/// emote while it answers and the answer line, then watches for <see cref="WatchSeconds"/> so the creature can react.
/// <see cref="Done"/> lets the host take its screenshot afterwards.
/// </summary>
public sealed partial class PlaytestSelfTest(
    InteractionControls controls,
    CompanionBar companions,
    CompanionPanel panel,
    SelfTestPanels panels,
    CombatControls combat, LockOn lockOn, GameClient client, OrbitCamera camera, Func<bool> worldShown, ILog log, Action<string> capture)
    : Node
{
    private const double WalkLimitSeconds = 8;
    private const float CloseMetres = 3f; // inside the basic attack's reach of a mossback (2 m + its 1.6 m body)
    private const double StepSeconds = 1.2;
    private const double WatchSeconds = 6;
    private const int LastStep = 45;
    private const int AcademyLastStep = 7;

    private double _clock;
    private double _walking;
    private int _step = -1;
    private int _academy;

    /// <summary>All steps were done and the last answer had time to arrive.</summary>
    public bool Done { get; private set; }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (Done || !worldShown())
        {
            return;
        }

        if (_academy <= AcademyLastStep && panels.Dialogue is Ui.AcademyDialogue dialogue)
        {
            _clock += delta;
            if (_clock >= StepSeconds)
            {
                _clock = 0;
                AcademyStep(dialogue);
                _academy++;
            }

            return;
        }

        if (_step < 0)
        {
            Walk(delta);
            return;
        }

        _clock += delta;
        if (_clock < (_step <= LastStep ? StepSeconds : WatchSeconds))
        {
            return;
        }

        _clock = 0;
        Step();
        _step++;
    }

    private void Walk(double delta)
    {
        _walking += delta;
        bool inReach = controls.Target.Id != 0 && controls.TargetDistance < CloseMetres;
        Press(Key.W, pressed: !inReach && _walking < WalkLimitSeconds);
        if (inReach || _walking >= WalkLimitSeconds)
        {
            log.Write(LogLevel.Info, "selftest.reach", inReach ? $"{controls.Target.Name} #{controls.Target.Id} in reach after {_walking:F1} s" : "nothing in reach");
            _step = 0;
            _clock = 0;
        }
    }

    private void Step()
    {
        switch (_step)
        {
            case 0:
                log.Write(LogLevel.Info, "selftest.aim", $"attacking {controls.Target.Name} at {controls.TargetDistance:F1} m; gather: {(panels.Items.Gather() ? "a bush in reach" : "no node in reach")}");
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
                break;
            case 1:
                log.Write(LogLevel.Info, "selftest.attack", $"attack: {combat.LastResult?.ToString() ?? "no answer"}; {controls.Target.Name} health {TargetHealth()}/255");
                PressKey(Key.T);
                break;
            case 2:
                log.Write(LogLevel.Info, "selftest.lock", $"lock-on allowed {client.LockOnAllowed}, target #{lockOn.Target}, camera at {Mathf.RadToDeg(camera.Yaw):F0}°");
                OrbitCamera.DragForTest(Mathf.Pi / 2 / OrbitCamera.Sensitivity); // ignored while locked
                break;
            case 3:
                log.Write(LogLevel.Info, "selftest.lock", $"after a drag while locked the camera is at {Mathf.RadToDeg(camera.Yaw):F0}° (target #{lockOn.Target})");
                PressKey(Key.T);
                PressKey(Key.P);
                break;
            case 4:
                log.Write(LogLevel.Info, "selftest.pact", $"menu open {controls.Menu.IsOpen} for {controls.Target.Name} #{controls.Target.Id} at {controls.TargetDistance:F1} m");
                PressKey(Key.Key1);
                PressKey(Key.Key2);
                break;
            case 5:
                log.Write(LogLevel.Info, "selftest.pact", $"chosen terms {controls.Menu.Chosen.Mask:b6}");
                capture("pact");
                PressKey(Key.Enter);
                break;
            case 6:
                log.Write(LogLevel.Info, "selftest.pact", $"menu open {controls.Menu.IsOpen}; the {controls.Target.Name} shows {TargetEmote()}; line so far: '{controls.Feedback}'");
                capture("answer");
                break;
            case 7:
                log.Write(LogLevel.Info, "selftest.pact", $"the {controls.Target.Name} shows {TargetEmote()}; answer: {controls.Feedback}");
                PressKey(Key.R);
                break;
            case 8:
                log.Write(LogLevel.Info, "selftest.feedback", controls.Feedback);
                PressKey(Key.E);
                break;
            case 9:
                log.Write(LogLevel.Info, "selftest.feedback", controls.Feedback);
                PressKey(Key.Q);
                break;
            case 10:
                log.Write(LogLevel.Info, "selftest.feedback", controls.Feedback);
                OrbitCamera.DragForTest(Mathf.Pi / 2 / OrbitCamera.Sensitivity);
                break;
            case 11:
                log.Write(LogLevel.Info, "selftest.camera", $"after the right-drag the camera is at {Mathf.RadToDeg(camera.Yaw):F0}° around the character");
                Press(Key.Space, pressed: true);
                break;
            case 12:
                Press(Key.Space, pressed: false);
                log.Write(LogLevel.Info, "selftest.dodge", $"after the dodge: stamina {client.OwnVitals.Stamina}/255, health {client.OwnVitals.Health}/255");
                PressKey(Key.Z);
                break;
            default:
                Done = !CompanionStep();
                break;
        }
    }

    // Summoning, following, commands and the companion panel (M2-06 to M2-10); false when every step is done.
    private bool CompanionStep()
    {
        switch (_step)
        {
            case 13:
                log.Write(LogLevel.Info, "selftest.summon", $"action {(SumonerWorld.Core.Combat.CombatAction)client.OwnVitals.Action}, mana {client.OwnVitals.Mana}/255; {controls.Feedback}");
                capture("sigil");
                break;
            case 14:
                log.Write(LogLevel.Info, "selftest.summon", $"{OneLine(companions.Shown)} | {CompanionDistance()}");
                capture("summoned");
                Press(Key.W, pressed: true);
                break;
            case 15:
                Press(Key.W, pressed: false);
                log.Write(LogLevel.Info, "selftest.follow", $"after a run: {CompanionDistance()}");
                capture("follow");
                PressKey(Key.Key8);
                break;
            case 16:
                log.Write(LogLevel.Info, "selftest.command", $"{controls.Feedback} | companion shows {CompanionEmote()}");
                PressKey(Key.Key9);
                break;
            case 17:
                log.Write(LogLevel.Info, "selftest.command", $"{controls.Feedback} | companion shows {CompanionEmote()}");
                Press(Key.W, pressed: true);
                break;
            case 18:
                Press(Key.W, pressed: false);
                log.Write(LogLevel.Info, "selftest.command", $"held while she walked on: {CompanionDistance()}");
                PressKey(Key.Key0);
                break;
            case 19:
                log.Write(LogLevel.Info, "selftest.command", $"{controls.Feedback} | {CompanionDistance()}");
                capture("commands");
                PressKey(Key.X);
                break;
            case 20:
                log.Write(LogLevel.Info, "selftest.summon", $"{OneLine(companions.Shown)} | {controls.Feedback}");
                PressKey(Key.K);
                break;
            case 21:
                log.Write(LogLevel.Info, "selftest.skills", $"panel open {panel.IsOpen}: {OneLine(panel.Shown)}");
                capture("skills");
                PressKey(Key.Key2);
                break;
            case 22:
                log.Write(LogLevel.Info, "selftest.skills", $"{controls.Feedback} | {OneLine(panel.Shown)}");
                PressKey(Key.K);
                break;
            case 23:
                log.Write(LogLevel.Info, "selftest.skills", $"panel open {panel.IsOpen}");
                log.Write(LogLevel.Info, "selftest.bar", panels.Bar.Describe());
                PressKey(Key.Key1);
                break;
            case 24:
                log.Write(LogLevel.Info, "selftest.spells", $"thornbolt: {controls.Feedback}; mana {client.OwnVitals.Mana}/255");
                PressKey(Key.Key2);
                break;
            case 25:
                log.Write(LogLevel.Info, "selftest.spells", $"entangle: {controls.Feedback}; mana {client.OwnVitals.Mana}/255; {RootedNearby()} creatures rooted in view");
                capture("entangle");
                PressKey(Key.Key3);
                break;
            case 26:
                log.Write(LogLevel.Info, "selftest.spells", $"mend: {controls.Feedback}; action {(SumonerWorld.Core.Combat.CombatAction)client.OwnVitals.Action}");
                break;
            case 27:
                log.Write(LogLevel.Info, "selftest.spells", $"own status {(SumonerWorld.Core.Combat.StatusMarks)client.OwnVitals.Status}; mana {client.OwnVitals.Mana}/255");
                capture("mend");
                break;
            case 28:
                PressKey(Key.I);
                break;
            case 29:
                log.Write(LogLevel.Info, "selftest.items", panels.Character.Describe());
                capture("inventory");
                PressKey(Key.I);
                break;
            default:
                return GearStep();
        }

        return true;
    }

    // Crafting and gear (M2-14); false when every step is done.
    private bool GearStep()
    {
        switch (_step)
        {
            case 30:
                PressKey(Key.B);
                break;
            case 31:
                log.Write(LogLevel.Info, "selftest.craft", panels.Crafting.Text);
                capture("crafting");
                panels.Crafting.Craft(3); // the wooden staff, 3 seconds
                break;
            case 32:
                log.Write(LogLevel.Info, "selftest.craft", $"{controls.Feedback}; making #{client.Inventory.Crafting}");
                break;
            case 35:
                log.Write(LogLevel.Info, "selftest.craft", $"{controls.Feedback}; making #{client.Inventory.Crafting}");
                PressKey(Key.B);
                PressKey(Key.I);
                break;
            case 36:
                log.Write(LogLevel.Info, "selftest.gear", panels.Character.Describe());
                panels.Character.Drop(BagSpotOf("mossback_charm"), panels.Character.WornSpot(Core.Items.EquipPosition.Charm1));
                break;
            case 37:
                log.Write(LogLevel.Info, "selftest.gear", $"{controls.Feedback}; {panels.Character.Describe()}");
                capture("character");
                PressKey(Key.Z);
                break;
            case 39:
                panels.Character.Choose(1);
                log.Write(LogLevel.Info, "selftest.bag", $"{OneLine(companions.Shown)}; {controls.Feedback}");
                break;
            case 40:
                log.Write(LogLevel.Info, "selftest.bag", panels.Character.Describe());
                panels.Character.Drop(BagSpotOf("sweet_berries"), new DragSpot(DragPlace.CreatureBag, panels.Character.Wearer, 0));
                break;
            case 41:
                log.Write(LogLevel.Info, "selftest.bag", $"{controls.Feedback}; {panels.Character.Describe()}");
                capture("companion-bag");
                PressKey(Key.I);
                break;
            case 42:
                string? move = LearnedMove();
                log.Write(LogLevel.Info, "selftest.skillbar", $"owl's learned move: {move ?? "none"}");
                if (move is not null)
                {
                    panels.Bar.Drop(new DragSpot(DragPlace.Action, 0, 0, new ActionRef(ActionKind.CompanionSkill, move).ToString()), 3);
                    PressKey(Key.Key4);
                }

                break;
            case 43:
                log.Write(LogLevel.Info, "selftest.skillbar", $"{panels.Bar.Describe()} | {controls.Feedback}");
                capture("skill-bar");
                break;
            case 44:
                PressKey(Key.J);
                log.Write(LogLevel.Info, "selftest.quests", panels.Quests.Describe());
                panels.Quests.Accept(1);
                panels.Quests.Accept(4);
                break;
            case 45:
                log.Write(LogLevel.Info, "selftest.quests", $"{controls.Feedback} | {panels.Quests.Describe()} | tracker: {OneLine(panels.Quests.Tracker)}");
                capture("quests");
                PressKey(Key.J);
                break;
            case 33 or 34 or 38:
                break;
            default:
                return false;
        }

        return true;
    }

    // The ranking at the Academy gate before anything else (M2-21): talk to the Headmaster, answer the first four questions (the
    // first answer each time), then talk to the Arena Master, who sends the student to the arena (the fight there is covered by
    // the tests: the self-test does not walk to it).
    private void AcademyStep(Ui.AcademyDialogue dialogue)
    {
        switch (_academy)
        {
            case 0:
                log.Write(LogLevel.Info, "selftest.academy", $"talk: {(dialogue.Talk() ? "the Headmaster is in reach" : "nobody in reach")}");
                break;
            case 1:
                log.Write(LogLevel.Info, "selftest.academy", dialogue.Text);
                capture("academy-question");
                dialogue.Choose(0);
                break;
            case 5:
                log.Write(LogLevel.Info, "selftest.academy", dialogue.Text);
                dialogue.Talk(1);
                break;
            case 6:
                log.Write(LogLevel.Info, "selftest.academy", $"{dialogue.Text} | tracker: {OneLine(panels.Quests.Tracker)}");
                capture("arena-master");
                break;
            case AcademyLastStep:
                dialogue.Close();
                break;
            default:
                dialogue.Choose(0);
                break;
        }
    }

    // A move the first companion has learned (from its skill tree and the companion list), or null.
    private string? LearnedMove()
    {
        if (client.Companions.Count == 0 || panels.Data.SkillTreeOf(client.Companions[0].Species) is not Core.Data.Definitions.SkillTreeDefinition tree)
        {
            return null;
        }

        for (int n = 0; n < tree.Nodes.Count; n++)
        {
            if ((client.Companions[0].Learned & (1UL << n)) != 0 && tree.Nodes[n].Move is string move)
            {
                return move;
            }
        }

        return null;
    }

    private DragSpot BagSpotOf(string item)
    {
        int index = panels.Data.ItemIndex(item);
        return new DragSpot(DragPlace.Bag, 0, DragRules.SlotOf(client.Inventory, index));
    }

    private static void PressKey(Key key)
    {
        Press(key, pressed: true);
        Press(key, pressed: false);
    }

    private static string OneLine(string text) => text.Replace("\n", "; ", StringComparison.Ordinal);

    private int RootedNearby() =>
        client.Entities.Values.Count(entity => ((SumonerWorld.Core.Combat.StatusMarks)entity.Status).HasFlag(SumonerWorld.Core.Combat.StatusMarks.Rooted));

    private string CompanionEmote()
    {
        foreach (SumonerWorld.Net.Messages.CompanionEntry companion in client.Companions)
        {
            if (client.Entities.TryGetValue((SumonerWorld.Net.Messages.SnapshotEntityKind.Creature, companion.Id), out SumonerWorld.Net.Messages.SnapshotEntity entity))
            {
                return ((SumonerWorld.Core.Cognition.Appraisal.Emote)entity.Emote).ToString();
            }
        }

        return "nothing";
    }

    private string CompanionDistance()
    {
        foreach (SumonerWorld.Net.Messages.CompanionEntry companion in client.Companions)
        {
            if (client.Entities.TryGetValue((SumonerWorld.Net.Messages.SnapshotEntityKind.Creature, companion.Id), out SumonerWorld.Net.Messages.SnapshotEntity entity))
            {
                return $"companion #{companion.Id} {System.Numerics.Vector3.Distance(entity.Position, client.Prediction.Position):F1} m away";
            }
        }

        return "no companion in view";
    }

    private string TargetEmote() =>
        client.Entities.TryGetValue((SumonerWorld.Net.Messages.SnapshotEntityKind.Creature, controls.Target.Id), out SumonerWorld.Net.Messages.SnapshotEntity entity)
            ? ((SumonerWorld.Core.Cognition.Appraisal.Emote)entity.Emote).ToString()
            : "nothing (out of reach)";

    private int TargetHealth() =>
        client.Entities.TryGetValue((SumonerWorld.Net.Messages.SnapshotEntityKind.Creature, controls.Target.Id), out SumonerWorld.Net.Messages.SnapshotEntity entity) ? entity.Health : -1;

    private static void Press(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
}

/// <summary>The item and bag panels the self-test drives (M2-13, M2-14, M2-14b).</summary>
/// <param name="Items">Loot and gathering.</param>
/// <param name="Crafting">The crafting window.</param>
/// <param name="Character">The character window.</param>
/// <param name="Bar">The action bar.</param>
/// <param name="Quests">The quest log and tracker.</param>
/// <param name="Dialogue">Talking to the Academy's people (null without an Academy in the data).</param>
/// <param name="Data">The data both sides load.</param>
public sealed record SelfTestPanels(
    Items.ItemControls Items,
    Items.CraftingPanel Crafting,
    Ui.CharacterWindow Character,
    Ui.ActionBar Bar,
    Ui.QuestLog Quests,
    Ui.AcademyDialogue? Dialogue,
    Core.Data.IDataRegistry Data);
