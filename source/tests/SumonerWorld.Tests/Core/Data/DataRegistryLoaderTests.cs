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

    [Fact]
    public void The_shipped_game_data_is_valid()
    {
        // Guards every data change in CI: the server refuses to start on invalid data (AI Bible §12).
        DataRegistry registry = DataRegistryLoader.Load(ReadShippedData());

        Assert.NotEmpty(registry.MemoryEventTypes);
        Assert.NotEmpty(registry.Species);
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
        Assert.Equal(18, registry.MemoryEventTypes.Count);
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
    [InlineData("\"kind\": \"memoryEventTypes\"", "\"kind\": \"spells\"", "unknown kind 'spells'")]
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
            [new DataFile("events.json", ValidEventTypes), new DataFile("weights.json", ValidOpinionWeights), new DataFile("s.json", noNeeds)]));

        Assert.Contains(error.Errors, message => message.Contains("needs is required", StringComparison.Ordinal));
    }

    private static string SpeciesJson(string id) => $$"""
        { "schemaVersion": 1, "kind": "species", "entries": [ {
          "id": "{{id}}", "nameKey": "species.{{id}}.name", "role": "grazer", "bodyType": "quadruped", "group": "herd",
          "boldness": { "mean": -30, "spread": 25 }, "loyalty": { "mean": 40, "spread": 30 },
          "curiosity": { "mean": 10, "spread": 30 }, "temper": { "mean": -40, "spread": 20 },
          "needs": { "hunger": 60, "rest": 40, "safety": 50, "social": 70, "territory": 10 },
          "pactThreshold": 450 } ] }
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
