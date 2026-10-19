using System.Text.Json;
using System.Text.Json.Serialization;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Data;

/// <summary>
/// Builds a validated <see cref="DataRegistry"/> from data files (R08, AI Bible §12).
/// Every file is an envelope <c>{ "schemaVersion": 1, "kind": "...", "entries": [ ... ] }</c>. Unknown fields, missing fields,
/// unknown enum values, out-of-range numbers and duplicate ids are all errors; every error in every file is reported together.
/// </summary>
public static class DataRegistryLoader
{
    /// <summary>File kind for <see cref="MemoryEventTypeDefinition"/> (exactly one file).</summary>
    public const string MemoryEventTypesKind = "memoryEventTypes";

    /// <summary>File kind for <see cref="SpeciesDefinition"/> (one or more files).</summary>
    public const string SpeciesKind = "species";

    /// <summary>File kind for <see cref="OpinionWeightsDefinition"/> (exactly one file).</summary>
    public const string OpinionWeightsKind = "opinionWeights";

    /// <summary>Schema version this build reads, for every kind.</summary>
    public const int CurrentSchemaVersion = 1;

    private const int MinTrait = -100;
    private const int MaxTrait = 100;
    private const int MaxTraitSpread = 100;
    private const int MaxNeedRatePerDay = 500;
    private const int MaxPactThreshold = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>Parses and validates all files. Throws <see cref="DataValidationException"/> listing every problem.</summary>
    public static DataRegistry Load(IEnumerable<DataFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var errors = new List<string>();
        var memoryEventTypes = new List<MemoryEventTypeDefinition>();
        var species = new List<SpeciesDefinition>();
        var opinionWeights = new List<(OpinionWeightsDefinition Entry, string Where)>();
        var memoryEventFiles = new List<string>();
        var opinionWeightFiles = new List<string>();
        var speciesSources = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (DataFile file in files)
        {
            string? kind = ReadKind(file, errors);
            switch (kind)
            {
                case null:
                    break;
                case MemoryEventTypesKind:
                    memoryEventFiles.Add(file.Path);
                    foreach ((MemoryEventTypeDefinition entry, int index) in ReadEntries<MemoryEventTypeDefinition>(file, errors))
                    {
                        ValidateMemoryEventType(entry, $"{file.Path}: entries[{index}]", errors);
                        memoryEventTypes.Add(entry);
                    }

                    break;
                case SpeciesKind:
                    foreach ((SpeciesDefinition entry, int index) in ReadEntries<SpeciesDefinition>(file, errors))
                    {
                        string where = $"{file.Path}: entries[{index}]";
                        ValidateSpecies(entry, where, errors);
                        if (entry.Id is not null && !speciesSources.TryAdd(entry.Id, where))
                        {
                            errors.Add($"{where}: duplicate species id '{entry.Id}' (first defined at {speciesSources[entry.Id]}).");
                        }

                        species.Add(entry);
                    }

                    break;
                case OpinionWeightsKind:
                    opinionWeightFiles.Add(file.Path);
                    foreach ((OpinionWeightsDefinition entry, int index) in ReadEntries<OpinionWeightsDefinition>(file, errors))
                    {
                        string where = $"{file.Path}: entries[{index}]";
                        ValidateOpinionWeights(entry, where, errors);
                        opinionWeights.Add((entry, where));
                    }

                    break;
                default:
                    errors.Add($"{file.Path}: unknown kind '{kind}'. Known kinds: {MemoryEventTypesKind}, {SpeciesKind}, {OpinionWeightsKind}.");
                    break;
            }
        }

        if (memoryEventFiles.Count != 1)
        {
            errors.Add($"Expected exactly one '{MemoryEventTypesKind}' file, found {memoryEventFiles.Count} ({string.Join(", ", memoryEventFiles)}).");
        }

        if (opinionWeightFiles.Count != 1)
        {
            errors.Add($"Expected exactly one '{OpinionWeightsKind}' file, found {opinionWeightFiles.Count} ({string.Join(", ", opinionWeightFiles)}).");
        }

        ValidateUniqueMemoryEventTypes(memoryEventTypes, errors);
        ValidateOpinionWeightReferences(opinionWeights, memoryEventTypes, errors);

        if (errors.Count > 0)
        {
            throw new DataValidationException(errors);
        }

        return new DataRegistry(
            [.. memoryEventTypes.OrderBy(definition => definition.Id)],
            [.. species.OrderBy(definition => definition.Id, StringComparer.Ordinal)],
            [.. opinionWeights.Select(item => item.Entry).OrderBy(definition => definition.EventType)]);
    }

    private static string? ReadKind(DataFile file, List<string> errors)
    {
        try
        {
            using var document = JsonDocument.Parse(file.Content, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out JsonElement kind)
                || kind.ValueKind != JsonValueKind.String)
            {
                errors.Add($"{file.Path}: missing string field 'kind'.");
                return null;
            }

            if (!root.TryGetProperty("schemaVersion", out JsonElement version) || !version.TryGetInt32(out int schemaVersion))
            {
                errors.Add($"{file.Path}: missing integer field 'schemaVersion'.");
                return null;
            }

            if (schemaVersion != CurrentSchemaVersion)
            {
                errors.Add($"{file.Path}: schemaVersion {schemaVersion} is not supported; this build reads version {CurrentSchemaVersion}.");
                return null;
            }

            return kind.GetString();
        }
        catch (JsonException exception)
        {
            errors.Add($"{file.Path}: invalid JSON: {exception.Message}");
            return null;
        }
    }

    private static List<(T Entry, int Index)> ReadEntries<T>(DataFile file, List<string> errors)
        where T : class
    {
        var result = new List<(T, int)>();
        try
        {
            DataFileEnvelope<T>? envelope = JsonSerializer.Deserialize<DataFileEnvelope<T>>(file.Content, JsonOptions);
            if (envelope?.Entries is null)
            {
                errors.Add($"{file.Path}: 'entries' must be an array.");
                return result;
            }

            for (int i = 0; i < envelope.Entries.Count; i++)
            {
                if (envelope.Entries[i] is T entry)
                {
                    result.Add((entry, i));
                }
                else
                {
                    errors.Add($"{file.Path}: entries[{i}] is null.");
                }
            }
        }
        catch (JsonException exception)
        {
            errors.Add($"{file.Path}: {exception.Message}");
        }

        return result;
    }

    private static void ValidateMemoryEventType(MemoryEventTypeDefinition entry, string where, List<string> errors)
    {
        if (!IsPascalCaseLetters(entry.Key))
        {
            errors.Add($"{where}: key '{entry.Key}' must be PascalCase letters, e.g. 'FedBy'.");
        }

        if (entry.Id == 0)
        {
            errors.Add($"{where}: id 0 is reserved for 'none'.");
        }

        if (entry.Subjects is null || entry.Subjects.Count == 0 || entry.Subjects.Distinct().Count() != entry.Subjects.Count)
        {
            errors.Add($"{where}: subjects must be a non-empty list without duplicates.");
        }

        CheckRange(entry.Valence, MemoryEventTypeDefinition.MinValence, MemoryEventTypeDefinition.MaxValence, "valence", where, errors);
        CheckRange(entry.BaseSalience, 1, MemoryEventTypeDefinition.MaxSalience, "baseSalience", where, errors);
        if (entry.HalfLifeDays is int halfLife && halfLife < 1)
        {
            errors.Add($"{where}: halfLifeDays must be at least 1, or null for memories that never decay.");
        }

        if (entry.TraitDrift is TraitDrift drift)
        {
            if (!entry.IsCore)
            {
                errors.Add($"{where}: traitDrift is only allowed on core memory types (AI Bible §3).");
            }

            if (drift.Amount == 0 || Math.Abs(drift.Amount) > TraitDrift.MaxAmount)
            {
                errors.Add($"{where}: traitDrift.amount = {drift.Amount} must be between 1 and {TraitDrift.MaxAmount} points in either direction.");
            }
        }
    }

    private static void ValidateUniqueMemoryEventTypes(List<MemoryEventTypeDefinition> definitions, List<string> errors)
    {
        foreach (IGrouping<ushort, MemoryEventTypeDefinition> duplicate in definitions.GroupBy(definition => definition.Id).Where(group => group.Count() > 1))
        {
            errors.Add($"Memory event type id {duplicate.Key} is defined {duplicate.Count()} times; ids must be unique and never reused.");
        }

        foreach (IGrouping<string, MemoryEventTypeDefinition> duplicate in definitions
                     .Where(definition => definition.Key is not null)
                     .GroupBy(definition => definition.Key, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"Memory event type key '{duplicate.Key}' is defined {duplicate.Count()} times.");
        }
    }

    private static void ValidateOpinionWeights(OpinionWeightsDefinition entry, string where, List<string> errors)
    {
        CheckRange(entry.Trust, OpinionWeightsDefinition.MinWeight, OpinionWeightsDefinition.MaxWeight, "trust", where, errors);
        CheckRange(entry.Fear, OpinionWeightsDefinition.MinWeight, OpinionWeightsDefinition.MaxWeight, "fear", where, errors);
        CheckRange(entry.Affection, OpinionWeightsDefinition.MinWeight, OpinionWeightsDefinition.MaxWeight, "affection", where, errors);
        CheckRange(entry.Respect, OpinionWeightsDefinition.MinWeight, OpinionWeightsDefinition.MaxWeight, "respect", where, errors);
    }

    private static void ValidateOpinionWeightReferences(
        List<(OpinionWeightsDefinition Entry, string Where)> weights, List<MemoryEventTypeDefinition> eventTypes, List<string> errors)
    {
        var knownIds = eventTypes.Select(definition => definition.Id).ToHashSet();
        var seen = new HashSet<ushort>();
        foreach ((OpinionWeightsDefinition entry, string where) in weights)
        {
            if (!knownIds.Contains(entry.EventType))
            {
                errors.Add($"{where}: eventType {entry.EventType} is not a known memory event type.");
            }

            if (!seen.Add(entry.EventType))
            {
                errors.Add($"{where}: eventType {entry.EventType} has more than one row of opinion weights.");
            }
        }
    }

    private static void ValidateSpecies(SpeciesDefinition entry, string where, List<string> errors)
    {
        if (!IsSnakeCaseId(entry.Id))
        {
            errors.Add($"{where}: id '{entry.Id}' must be snake_case, e.g. 'mossback'.");
        }

        if (string.IsNullOrWhiteSpace(entry.NameKey))
        {
            errors.Add($"{where}: nameKey must be a localization key (R39).");
        }

        CheckTrait(entry.Boldness, "boldness", where, errors);
        CheckTrait(entry.Loyalty, "loyalty", where, errors);
        CheckTrait(entry.Curiosity, "curiosity", where, errors);
        CheckTrait(entry.Temper, "temper", where, errors);

        if (entry.Needs is null)
        {
            errors.Add($"{where}: needs is required.");
        }
        else
        {
            CheckRange(entry.Needs.Hunger, 0, MaxNeedRatePerDay, "needs.hunger", where, errors);
            CheckRange(entry.Needs.Rest, 0, MaxNeedRatePerDay, "needs.rest", where, errors);
            CheckRange(entry.Needs.Safety, 0, MaxNeedRatePerDay, "needs.safety", where, errors);
            CheckRange(entry.Needs.Social, 0, MaxNeedRatePerDay, "needs.social", where, errors);
            CheckRange(entry.Needs.Territory, 0, MaxNeedRatePerDay, "needs.territory", where, errors);
        }

        CheckRange(entry.PactThreshold, 0, MaxPactThreshold, "pactThreshold", where, errors);
    }

    private static void CheckTrait(TraitRange? trait, string name, string where, List<string> errors)
    {
        if (trait is null)
        {
            errors.Add($"{where}: {name} is required.");
            return;
        }

        CheckRange(trait.Mean, MinTrait, MaxTrait, $"{name}.mean", where, errors);
        CheckRange(trait.Spread, 0, MaxTraitSpread, $"{name}.spread", where, errors);
    }

    private static void CheckRange(int value, int min, int max, string field, string where, List<string> errors)
    {
        if (value < min || value > max)
        {
            errors.Add($"{where}: {field} = {value} is outside [{min}, {max}].");
        }
    }

    // ^[A-Z][A-Za-z]*$
    private static bool IsPascalCaseLetters(string? value) =>
        !string.IsNullOrEmpty(value) && char.IsAsciiLetterUpper(value[0]) && value.All(char.IsAsciiLetter);

    // ^[a-z][a-z0-9_]*$
    private static bool IsSnakeCaseId(string? value) =>
        !string.IsNullOrEmpty(value)
        && char.IsAsciiLetterLower(value[0])
        && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');

    private sealed class DataFileEnvelope<T>
        where T : class
    {
        public required int SchemaVersion { get; init; }

        public required string Kind { get; init; }

        public required List<T?> Entries { get; init; }
    }
}
