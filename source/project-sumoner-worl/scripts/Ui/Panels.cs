using Godot;
using SumonerWorld.Core.Data;
using SumonerWorld.Game.Combat;
using SumonerWorld.Game.Playtest;
using SumonerWorld.Net.Client;

namespace SumonerWorld.Game.Ui;

/// <summary>Creates the item and bag panels and the action bar of a client (M2-13, M2-14, M2-14b).</summary>
public static class Panels
{
    /// <summary>
    /// Adds loot and gathering, the crafting window, the character window, the action bar, the ability book and the quest log under
    /// <paramref name="parent"/>; <paramref name="layoutFile"/> is where the bar's layout is kept (empty: not kept).
    /// </summary>
    public static SelfTestPanels AddTo(
        Node parent,
        GameClient client,
        DataRegistry data,
        InteractionControls controls,
        CombatControls combat,
        CompanionBar companions,
        Func<Vector3> feet,
        string layoutFile)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var items = new Items.ItemControls(client, data, controls, feet);
        var crafting = new Items.CraftingPanel(client, data, controls);
        var character = new CharacterWindow(client, data, controls);
        var bar = new ActionBar(client, data, controls, combat, companions, layoutFile);
        var book = new AbilityBook(data, controls, bar.Run);
        var quests = new QuestLog(client, data, controls);
        foreach (Node panel in new Node[] { items, crafting, character, bar, book, quests })
        {
            parent.AddChild(panel);
        }

        AcademyDialogue? dialogue = null;
        if (data.Academy is Core.Data.Definitions.AcademyDefinition academy)
        {
            var view = new AcademyView(academy, World.DevWorld.Ground.HeightAt);
            dialogue = new AcademyDialogue(client, data, controls, view, feet);
            parent.AddChild(view);
            parent.AddChild(dialogue);
        }

        return new SelfTestPanels(items, crafting, character, bar, quests, dialogue, data);
    }
}
