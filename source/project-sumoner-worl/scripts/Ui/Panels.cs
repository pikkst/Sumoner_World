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
        Func<ulong> creature,
        string layoutFile)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var items = new Items.ItemControls(client, data, controls, feet);
        var crafting = new Items.CraftingPanel(client, data, controls);
        var character = new CharacterWindow(client, data, controls);
        var bar = new ActionBar(client, data, controls, combat, companions, layoutFile);
        var book = new AbilityBook(data, controls, bar.Run, () => (Core.Progression.SummonerClass)client.OwnVitals.Class);
        var quests = new QuestLog(client, data, controls, feet);
        foreach (Node panel in new Node[] { items, crafting, character, bar, book, quests })
        {
            parent.AddChild(panel);
        }

        if (data.Region is Core.Data.Definitions.RegionDefinition region)
        {
            parent.AddChild(new RegionView(region, World.DevWorld.Ground.HeightAt, feet));
        }

        if (data.Academy is Core.Data.Definitions.AcademyDefinition academy)
        {
            parent.AddChild(new AcademyView(academy, World.DevWorld.Ground.HeightAt));
        }

        AcademyDialogue? dialogue = null;
        if (data.People.Count > 0)
        {
            var people = new PeopleView(data, World.DevWorld.Ground.HeightAt, () => client.LastSnapshotTick);
            dialogue = new AcademyDialogue(client, data, controls, people, feet, creature);
            parent.AddChild(people);
            parent.AddChild(dialogue);
        }

        return new SelfTestPanels(items, crafting, character, bar, quests, dialogue, data);
    }
}
