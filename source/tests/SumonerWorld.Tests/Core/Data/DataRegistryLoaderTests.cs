using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Tests.Core.Data;

public class DataRegistryLoaderTests
{
    private const string GameDataRelativePath = "source/project-sumoner-worl/data";

    private const string ValidEventTypes = """
        { "schemaVersion": 1, "kind": "memoryEventTypes", "entries": [
          { "id": 1, "key": "FedBy", "subjects": ["player"], "valence": 20, "baseSalience": 150, "halfLifeDays": 2, "isCore": false },
          { "id": 3, "key": "SavedLifeOf", "subjects": ["player", "creature"], "valence": 90, "baseSalience": 900, "halfLifeDays": null, "isCore": true }
        ] }
        """;

    private const string ValidOpinionWeights = """
        { "schemaVersion": 1, "kind": "opinionWeights", "entries": [
          { "eventType": 1, "trust": 40, "fear": -10, "affection": 30, "respect": 0 }
        ] }
        """;

    private const string ValidGoals = """
        { "schemaVersion": 1, "kind": "goals", "entries": [
          { "id": 4, "key": "Eat", "weight": 900, "interrupt": false, "personality": [],
            "considerations": [ { "input": "needHunger", "curve": { "type": "quadratic", "slope": 1000 } } ] }
        ] }
        """;

    private const string ValidTree = """
        { "schemaVersion": 1, "kind": "behaviourTrees", "entries": [
          { "goal": "Eat", "root": { "type": "sequence", "children": [
            { "type": "condition", "input": "needHunger", "op": "gte", "value": 100 },
            { "type": "action", "intent": "moveTo", "target": "wander", "speed": 50, "timeout": 300 },
            { "type": "action", "intent": "interact", "verb": "eat" },
            { "type": "wait", "ticks": 90 } ] } }
        ] }
        """;

    private const string ValidPactTerms = """
        { "schemaVersion": 1, "kind": "pactTerms", "entries": [
          { "key": "FeedDaily", "base": 60, "need": "hunger", "needWeight": 120, "personality": [] }
        ] }
        """;

    private const string ValidMoves = """
        { "schemaVersion": 1, "kind": "moves", "entries": [
          { "id": "bite", "windupTicks": 9, "activeTicks": 3, "recoveryTicks": 12, "shape": { "kind": "arc", "reachCm": 170, "angleDeg": 90 },
            "damage": 7, "damageType": "physical", "stamina": 10, "poise": 10 }
        ] }
        """;

    private const string ValidCombat = """
        { "schemaVersion": 1, "kind": "combat", "entries": [ {
          "player": { "health": 100, "stamina": 100, "mana": 60, "power": 100, "guard": 0, "poise": 20,
            "resist": { "physical": 0, "primal": 0 }, "moves": ["bite"] },
          "staminaRegenPerSecond": 20, "staminaRegenDelayTicks": 15, "manaRegenPerSecond": 3, "healthRegenPerMillePerSecond": 20,
          "healthRegenDelayTicks": 300, "yieldBelowPerMille": 200, "yieldMaxBoldness": 50, "spareAfterTicks": 150, "playerDownTicks": 150,
          "blockDamagePerMille": 300, "blockArcDeg": 120, "parryWindowTicks": 6, "dodgeTicks": 9, "dodgeStamina": 25,
          "dodgeCooldownTicks": 15, "dodgeSpeedPercent": 250, "staggerTicks": 12,
          "summonManaCost": 20, "summonCastTicks": 30, "bondCapacity": 2 } ] }
        """;

    private const string ValidBondLevels = """
        { "schemaVersion": 1, "kind": "bondLevels", "entries": [
          { "level": 0, "key": "Wary", "minTrust": -100, "minAffection": -100, "coreMemoriesWithOwner": 0, "minDaysBonded": 0 },
          { "level": 1, "key": "Accepting", "minTrust": 20, "minAffection": 20, "coreMemoriesWithOwner": 0, "minDaysBonded": 0 }
        ] }
        """;

    [Fact]
    public void The_shipped_game_data_is_valid()
    {
        // Guards every data change in CI: the server refuses to start on invalid data (AI Bible §12).
        DataRegistry registry = DataRegistryLoader.Load(ReadShippedData());

        Assert.NotEmpty(registry.MemoryEventTypes);
        Assert.NotEmpty(registry.Species);
        Assert.All(registry.Species, species => Assert.NotNull(registry.SkillTreeOf(registry.Species.ToList().IndexOf(species))));
    }

    [Fact]
    public void The_shipped_memory_event_table_matches_the_ai_bible()
    {
        DataRegistry registry = DataRegistryLoader.Load(ReadShippedData());

        MemoryEventTypeDefinition savedLife = registry.GetMemoryEventType(3);
        Assert.Equal("SavedLifeOf", savedLife.Key);
        Assert.Equal(90, savedLife.Valence);
        Assert.Null(savedLife.HalfLifeDays);
        Assert.True(savedLife.IsCore);

        MemoryEventTypeDefinition pactBroken = registry.GetMemoryEventType(11);
        Assert.Equal(-80, pactBroken.Valence);
        Assert.Equal(90, pactBroken.HalfLifeDays);
        Assert.Equal(22, registry.MemoryEventTypes.Count);
    }

    [Fact]
    public void Lists_are_ordered_by_id_and_lookups_work()
    {
        DataRegistry registry = DataRegistryLoader.Load(ReadShippedData());

        Assert.Equal(registry.MemoryEventTypes.OrderBy(definition => definition.Id), registry.MemoryEventTypes);
        Assert.Equal(registry.Species.OrderBy(definition => definition.Id, StringComparer.Ordinal), registry.Species);
        Assert.Equal(CreatureRole.Grazer, registry.GetSpecies("mossback").Role);
        Assert.Throws<KeyNotFoundException>(() => registry.GetSpecies("pikachu"));
        Assert.Throws<KeyNotFoundException>(() => registry.GetMemoryEventType(999));
    }

    [Theory]
    [InlineData("\"valence\": 20", "\"valence\": 20, \"colour\": \"red\"", "colour")]
    [InlineData("\"valence\": 20, ", "", "valence")]
    [InlineData("\"valence\": 20", "\"valence\": 250", "valence = 250")]
    [InlineData("\"subjects\": [\"player\"]", "\"subjects\": [\"dragon\"]", "subjects[0]")]
    [InlineData("\"key\": \"FedBy\"", "\"key\": \"fed by\"", "PascalCase")]
    [InlineData("\"id\": 3", "\"id\": 1", "id 1 is defined 2 times")]
    [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": 2", "schemaVersion 2 is not supported")]
    [InlineData("\"kind\": \"memoryEventTypes\"", "\"kind\": \"potions\"", "unknown kind 'potions'")]
    [InlineData("] }", "", "invalid JSON")]
    [InlineData("\"halfLifeDays\": 2, \"isCore\": false", "\"halfLifeDays\": 2, \"isCore\": false, \"traitDrift\": { \"trait\": \"loyalty\", \"amount\": 2 }", "only allowed on core")]
    [InlineData("\"halfLifeDays\": null, \"isCore\": true", "\"halfLifeDays\": null, \"isCore\": true, \"traitDrift\": { \"trait\": \"loyalty\", \"amount\": 9 }", "traitDrift.amount = 9")]
    public void Invalid_memory_event_data_is_rejected_with_a_clear_reason(string find, string replace, string expectedFragment)
    {
        string broken = ValidEventTypes.Replace(find, replace, StringComparison.Ordinal);

        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load([new DataFile("events.json", broken)]));

        Assert.Contains(error.Errors, message => message.Contains(expectedFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_problem_in_every_file_is_reported_together()
    {
        string badSpecies = SpeciesJson("mossback").Replace("\"pactThreshold\": 450", "\"pactThreshold\": 5000", StringComparison.Ordinal);
        string duplicate = SpeciesJson("mossback");
        string badTrait = SpeciesJson("bogfox").Replace("\"mean\": -30", "\"mean\": -300", StringComparison.Ordinal);

        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(
        [
            new DataFile("events.json", ValidEventTypes),
            new DataFile("weights.json", ValidOpinionWeights),
            new DataFile("goals.json", ValidGoals),
            new DataFile("tree.json", ValidTree),
            new DataFile("terms.json", ValidPactTerms),
            new DataFile("levels.json", ValidBondLevels),
            new DataFile("moves.json", ValidMoves),
            new DataFile("combat.json", ValidCombat),
            new DataFile("a.json", badSpecies),
            new DataFile("b.json", duplicate),
            new DataFile("c.json", badTrait),
        ]));

        Assert.Equal(3, error.Errors.Count);
        Assert.Contains(error.Errors, message => message.StartsWith("a.json", StringComparison.Ordinal) && message.Contains("pactThreshold", StringComparison.Ordinal));
        Assert.Contains(error.Errors, message => message.StartsWith("b.json", StringComparison.Ordinal) && message.Contains("duplicate species id", StringComparison.Ordinal));
        Assert.Contains(error.Errors, message => message.StartsWith("c.json", StringComparison.Ordinal) && message.Contains("boldness.mean", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"eventType\": 1", "\"eventType\": 99", "eventType 99 is not a known memory event type")]
    [InlineData("\"trust\": 40", "\"trust\": 140", "trust = 140")]
    [InlineData("] }", ", { \"eventType\": 1, \"trust\": 1, \"fear\": 1, \"affection\": 1, \"respect\": 1 } ] }", "more than one row")]
    public void Invalid_opinion_weights_are_rejected(string find, string replace, string expectedFragment)
    {
        string broken = ValidOpinionWeights.Replace(find, replace, StringComparison.Ordinal);

        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(
            [new DataFile("events.json", ValidEventTypes), new DataFile("weights.json", broken)]));

        Assert.Contains(error.Errors, message => message.Contains(expectedFragment, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"weight\": 900", "\"weight\": 0", "weight = 0")]
    [InlineData("\"considerations\": [ { \"input\": \"needHunger\", \"curve\": { \"type\": \"quadratic\", \"slope\": 1000 } } ]", "\"considerations\": []", "at least one consideration")]
    [InlineData("\"type\": \"quadratic\", \"slope\": 1000", "\"type\": \"logistic\", \"centre\": 500, \"width\": 0", "width = 0")]
    [InlineData("\"input\": \"needHunger\"", "\"input\": \"moonPhase\"", "input")]
    [InlineData("\"personality\": []", "\"personality\": [ { \"trait\": \"boldness\", \"factor\": 50 } ]", "factor = 50")]
    public void Invalid_goals_are_rejected(string find, string replace, string expectedFragment)
    {
        string broken = ValidGoals.Replace(find, replace, StringComparison.Ordinal);

        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(
            [new DataFile("events.json", ValidEventTypes), new DataFile("weights.json", ValidOpinionWeights), new DataFile("goals.json", broken)]));

        Assert.Contains(error.Errors, message => message.Contains(expectedFragment, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"children\": [\n", "\"children\": [ { \"type\": \"sequence\", \"children\": [] },\n", "needs at least one child")]
    [InlineData("\"value\": 100", "\"value\": 2000", "value = 2000")]
    [InlineData("\"speed\": 50", "\"speed\": 0", "speed = 0")]
    [InlineData("\"intent\": \"interact\", \"verb\": \"eat\"", "\"intent\": \"emote\"", "needs an emote")]
    [InlineData("\"ticks\": 90", "\"ticks\": 0", "ticks = 0")]
    [InlineData("\"goal\": \"Eat\"", "\"goal\": \"Dance\"", "unknown goal 'Dance'")]
    [InlineData("\"type\": \"wait\"", "\"type\": \"teleport\"", "type")]
    public void Invalid_behaviour_trees_are_rejected(string find, string replace, string expectedFragment)
    {
        string broken = ValidTree.Replace(find, replace, StringComparison.Ordinal);

        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(
        [
            new DataFile("events.json", ValidEventTypes), new DataFile("weights.json", ValidOpinionWeights),
            new DataFile("goals.json", ValidGoals), new DataFile("tree.json", broken),
        ]));

        Assert.Contains(error.Errors, message => message.Contains(expectedFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_goal_needs_exactly_one_tree_and_trees_have_a_size_limit()
    {
        var missing = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(
            [new DataFile("events.json", ValidEventTypes), new DataFile("weights.json", ValidOpinionWeights), new DataFile("goals.json", ValidGoals)]));
        var twice = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(
        [
            new DataFile("events.json", ValidEventTypes), new DataFile("weights.json", ValidOpinionWeights),
            new DataFile("goals.json", ValidGoals), new DataFile("a.json", ValidTree), new DataFile("b.json", ValidTree),
        ]));
        string wait = "{ \"type\": \"wait\", \"ticks\": 1 }";
        string huge = $"{{ \"schemaVersion\": 1, \"kind\": \"behaviourTrees\", \"entries\": [ {{ \"goal\": \"Eat\", \"root\": {{ \"type\": \"sequence\", \"children\": [ {string.Join(", ", Enumerable.Repeat(wait, 40))} ] }} }} ] }}";
        var tooBig = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(
        [
            new DataFile("events.json", ValidEventTypes), new DataFile("weights.json", ValidOpinionWeights),
            new DataFile("goals.json", ValidGoals), new DataFile("huge.json", huge),
        ]));

        Assert.Contains(missing.Errors, message => message.Contains("Goal 'Eat' has no behaviour tree", StringComparison.Ordinal));
        Assert.Contains(twice.Errors, message => message.Contains("more than one tree", StringComparison.Ordinal));
        Assert.Contains(tooBig.Errors, message => message.Contains("41 nodes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("terms", "\"base\": 60", "\"base\": 900", "base = 900")]
    [InlineData("terms", "\"key\": \"FeedDaily\"", "\"key\": \"feed daily\"", "unique PascalCase")]
    [InlineData("terms", "\"need\": \"hunger\"", "\"need\": \"gold\"", "need")]
    [InlineData("levels", "\"level\": 1", "\"level\": 3", "without gaps")]
    [InlineData("levels", "\"minTrust\": 20", "\"minTrust\": -200", "minTrust = -200")]
    public void Invalid_pact_terms_and_bond_levels_are_rejected(string file, string find, string replace, string expectedFragment)
    {
        string terms = file == "terms" ? ValidPactTerms.Replace(find, replace, StringComparison.Ordinal) : ValidPactTerms;
        string levels = file == "levels" ? ValidBondLevels.Replace(find, replace, StringComparison.Ordinal) : ValidBondLevels;

        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(
            [new DataFile("terms.json", terms), new DataFile("levels.json", levels)]));

        Assert.Contains(error.Errors, message => message.Contains(expectedFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void A_species_using_an_unknown_goal_is_rejected()
    {
        string species = SpeciesJson("mossback").Replace("\"goals\": [\"Eat\"]", "\"goals\": [\"Eat\", \"Fly\"]", StringComparison.Ordinal);

        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(
        [
            new DataFile("events.json", ValidEventTypes), new DataFile("weights.json", ValidOpinionWeights),
            new DataFile("goals.json", ValidGoals), new DataFile("s.json", species),
        ]));

        Assert.Contains(error.Errors, message => message.Contains("goal 'Fly' is not defined", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_memory_event_file_is_an_error()
    {
        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load([new DataFile("s.json", SpeciesJson("mossback"))]));

        Assert.Contains(error.Errors, message => message.Contains("exactly one 'memoryEventTypes' file", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_nested_objects_are_errors_not_crashes()
    {
        string noNeeds = SpeciesJson("mossback").Replace("\"needs\": { \"hunger\": 60, \"rest\": 40, \"safety\": 50, \"social\": 70, \"territory\": 10 },", "\"needs\": null,", StringComparison.Ordinal);

        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(
            [new DataFile("events.json", ValidEventTypes), new DataFile("weights.json", ValidOpinionWeights), new DataFile("goals.json", ValidGoals), new DataFile("s.json", noNeeds)]));

        Assert.Contains(error.Errors, message => message.Contains("needs is required", StringComparison.Ordinal));
    }

    [Fact]
    public void Combat_data_must_name_known_moves_and_stay_in_range()
    {
        string unknownMove = SpeciesJson("mossback").Replace("\"moves\": [\"bite\"]", "\"moves\": [\"tail_swipe\"]", StringComparison.Ordinal);
        string noReach = ValidMoves.Replace("\"reachCm\": 170", "\"reachCm\": 0", StringComparison.Ordinal);

        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(
        [
            new DataFile("events.json", ValidEventTypes), new DataFile("weights.json", ValidOpinionWeights), new DataFile("goals.json", ValidGoals),
            new DataFile("tree.json", ValidTree), new DataFile("terms.json", ValidPactTerms), new DataFile("levels.json", ValidBondLevels),
            new DataFile("moves.json", noReach), new DataFile("combat.json", ValidCombat), new DataFile("s.json", unknownMove),
        ]));

        Assert.Equal(2, error.Errors.Count);
        Assert.Contains(error.Errors, message => message.StartsWith("s.json", StringComparison.Ordinal) && message.Contains("unknown move 'tail_swipe'", StringComparison.Ordinal));
        Assert.Contains(error.Errors, message => message.StartsWith("moves.json", StringComparison.Ordinal) && message.Contains("shape.reachCm", StringComparison.Ordinal));
    }

    [Fact]
    public void Skill_trees_must_name_known_species_branches_moves_and_earlier_skills()
    {
        const string Trees = """
            { "schemaVersion": 1, "kind": "skillTrees", "entries": [
              { "species": "mossback", "branches": [ { "id": "vanguard" } ], "nodes": [
                { "id": "horn", "branch": "vanguard", "level": 2, "move": "horn_toss" },
                { "id": "hide", "branch": "guardian", "level": 2, "passive": { "stat": "guard", "amount": 2 } },
                { "id": "ram", "branch": "vanguard", "level": 3, "requires": ["later"], "passive": { "stat": "power", "amount": 10 } },
                { "id": "later", "branch": "vanguard", "level": 4, "passive": { "stat": "poise", "amount": 5 }, "move": "bite" } ] },
              { "species": "dragon", "branches": [ { "id": "fire" } ], "nodes": [ { "id": "breath", "branch": "fire", "passive": { "stat": "power", "amount": 5 } } ] }
            ] }
            """;

        var error = Assert.Throws<DataValidationException>(() => DataRegistryLoader.Load(
        [
            new DataFile("events.json", ValidEventTypes), new DataFile("weights.json", ValidOpinionWeights), new DataFile("goals.json", ValidGoals),
            new DataFile("tree.json", ValidTree), new DataFile("terms.json", ValidPactTerms), new DataFile("levels.json", ValidBondLevels),
            new DataFile("moves.json", ValidMoves), new DataFile("combat.json", ValidCombat), new DataFile("s.json", SpeciesJson("mossback")),
            new DataFile("skills.json", Trees),
        ]));

        string[] expected = ["unknown branch 'guardian'", "requires 'later'", "exactly one of 'move' or 'passive'", "species 'dragon'", "unknown move 'horn_toss'"];
        Assert.All(expected, fragment => Assert.Contains(error.Errors, message => message.Contains(fragment, StringComparison.Ordinal)));
    }

    private static string SpeciesJson(string id) => $$"""
        { "schemaVersion": 1, "kind": "species", "entries": [ {
          "id": "{{id}}", "nameKey": "species.{{id}}.name", "role": "grazer", "bodyType": "quadruped", "group": "herd",
          "boldness": { "mean": -30, "spread": 25 }, "loyalty": { "mean": 40, "spread": 30 },
          "curiosity": { "mean": 10, "spread": 30 }, "temper": { "mean": -40, "spread": 20 },
          "needs": { "hunger": 60, "rest": 40, "safety": 50, "social": 70, "territory": 10 },
          "bodyRadiusCm": 120, "pactThreshold": 450, "perception": { "sightRange": 50, "sightHalfAngle": 120, "hearingRange": 30 },
          "combat": { "health": 100, "stamina": 50, "mana": 0, "power": 100, "guard": 0, "poise": 10,
            "resist": { "physical": 0, "primal": 0 }, "moves": ["bite"] },
          "goals": ["Eat"] } ] }
        """;

    private static List<DataFile> ReadShippedData()
    {
        string root = FindGameDataDirectory();
        return [.. Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => new DataFile(Path.GetRelativePath(root, path), File.ReadAllText(path)))];
    }

    private static string FindGameDataDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, GameDataRelativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"Could not find {GameDataRelativePath} above {AppContext.BaseDirectory}.");
    }
}
