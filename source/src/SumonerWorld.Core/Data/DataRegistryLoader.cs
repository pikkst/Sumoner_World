using System.Text.Json;
using System.Text.Json.Serialization;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Data;

/// <summary>
/// Builds a validated <see cref="DataRegistry"/> from data files (R08, AI Bible §12).
/// Every file is an envelope <c>{ "schemaVersion": 1, "kind": "...", "entries": [ ... ] }</c>. Unknown fields, missing fields,
/// unknown enum values, out-of-range numbers and duplicate ids are all errors; every error in every file is reported together.
/// </summary>
public static partial class DataRegistryLoader
{
    /// <summary>File kind for <see cref="MemoryEventTypeDefinition"/> (exactly one file).</summary>
    public const string MemoryEventTypesKind = "memoryEventTypes";

    /// <summary>File kind for <see cref="SpeciesDefinition"/> (one or more files).</summary>
    public const string SpeciesKind = "species";

    /// <summary>File kind for <see cref="OpinionWeightsDefinition"/> (exactly one file).</summary>
    public const string OpinionWeightsKind = "opinionWeights";

    /// <summary>File kind for <see cref="GoalDefinition"/> (exactly one file).</summary>
    public const string GoalsKind = "goals";

    /// <summary>File kind for <see cref="BehaviourTreeDefinition"/> (one or more files).</summary>
    public const string BehaviourTreesKind = "behaviourTrees";

    /// <summary>File kind for <see cref="PactTermDefinition"/> (exactly one file).</summary>
    public const string PactTermsKind = "pactTerms";

    /// <summary>File kind for <see cref="BondLevelDefinition"/> (exactly one file).</summary>
    public const string BondLevelsKind = "bondLevels";

    /// <summary>File kind for <see cref="MoveDefinition"/> (one or more files).</summary>
    public const string MovesKind = "moves";

    /// <summary>File kind for <see cref="CombatDefinition"/> (exactly one file).</summary>
    public const string CombatKind = "combat";

    /// <summary>File kind for <see cref="SkillTreeDefinition"/> (any number of files, M2-10).</summary>
    public const string SkillTreesKind = "skillTrees";

    /// <summary>File kind for <see cref="RuneDefinition"/> (M2-11).</summary>
    public const string RunesKind = "runes";

    /// <summary>File kind for <see cref="SpellDefinition"/> (M2-11).</summary>
    public const string SpellsKind = "spells";

    /// <summary>File kind for <see cref="EffectDefinition"/> (M2-11).</summary>
    public const string EffectsKind = "effects";

    private const int MaxTermValue = 300;

    private const int MaxSpeedPercent = 500;
    private const int MaxParallelChildren = 16;

    private const int CurveLimit = 2000;

    /// <summary>Schema version this build reads, for every kind.</summary>
    public const int CurrentSchemaVersion = 1;

    private const int MinTrait = -100;
    private const int MaxTrait = 100;
    private const int MaxTraitSpread = 100;
    private const int MaxNeedRatePerDay = 500;
    private const int MaxPactThreshold = 1000;
    private const int MinBodyRadiusCm = 10;
    private const int MaxBodyRadiusCm = 500;

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

        var collected = new Collected();
        foreach (DataFile file in files)
        {
            string? kind = ReadKind(file, collected.Errors);
            if (kind is not null)
            {
                collected.Add(kind, file);
            }
        }

        return collected.Build();
    }

    // Everything read so far, per kind; Build runs the cross-file checks and creates the registry.
    private sealed class Collected
    {
        private static readonly string[] SingleFileKinds = [MemoryEventTypesKind, OpinionWeightsKind, GoalsKind, PactTermsKind, BondLevelsKind, CombatKind];

        private readonly List<MemoryEventTypeDefinition> _memoryEventTypes = [];
        private readonly List<SpeciesDefinition> _species = [];
        private readonly List<(OpinionWeightsDefinition Entry, string Where)> _opinionWeights = [];
        private readonly List<(GoalDefinition Entry, string Where)> _goals = [];
        private readonly List<(string Key, string Where)> _speciesGoalRefs = [];
        private readonly List<(BehaviourTreeDefinition Entry, string Where)> _trees = [];
        private readonly List<PactTermDefinition> _pactTerms = [];
        private readonly List<BondLevelDefinition> _bondLevels = [];
        private readonly List<(MoveDefinition Entry, string Where)> _moves = [];
        private readonly List<(CombatStatsDefinition Stats, string Where)> _combatants = [];
        private CombatDefinition? _combat;
        private readonly List<(SkillTreeDefinition Entry, string Where)> _skillTrees = [];
        private readonly List<RuneDefinition> _runes = [];
        private readonly List<(SpellDefinition Entry, string Where)> _spells = [];
        private readonly List<EffectDefinition> _effects = [];
        private readonly Dictionary<string, string> _speciesSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _filesByKind = new(StringComparer.Ordinal);

        public List<string> Errors { get; } = [];

        public void Add(string kind, DataFile file)
        {
            if (!_filesByKind.TryGetValue(kind, out List<string>? paths))
            {
                _filesByKind[kind] = paths = [];
            }

            paths.Add(file.Path);
            switch (kind)
            {
                case MemoryEventTypesKind:
                    foreach ((MemoryEventTypeDefinition entry, int index) in ReadEntries<MemoryEventTypeDefinition>(file, Errors))
                    {
                        ValidateMemoryEventType(entry, $"{file.Path}: entries[{index}]", Errors);
                        _memoryEventTypes.Add(entry);
                    }

                    break;
                case SpeciesKind:
                    AddSpecies(file);
                    break;
                case OpinionWeightsKind:
                    foreach ((OpinionWeightsDefinition entry, int index) in ReadEntries<OpinionWeightsDefinition>(file, Errors))
                    {
                        string where = $"{file.Path}: entries[{index}]";
                        ValidateOpinionWeights(entry, where, Errors);
                        _opinionWeights.Add((entry, where));
                    }

                    break;
                case GoalsKind:
                    foreach ((GoalDefinition entry, int index) in ReadEntries<GoalDefinition>(file, Errors))
                    {
                        string where = $"{file.Path}: entries[{index}]";
                        ValidateGoal(entry, where, Errors);
                        _goals.Add((entry, where));
                    }

                    break;
                case BehaviourTreesKind:
                    foreach ((BehaviourTreeDefinition entry, int index) in ReadEntries<BehaviourTreeDefinition>(file, Errors))
                    {
                        string where = $"{file.Path}: entries[{index}]";
                        ValidateTree(entry, where, Errors);
                        _trees.Add((entry, where));
                    }

                    break;
                case PactTermsKind:
                    List<(PactTermDefinition Entry, int Index)> terms = ReadEntries<PactTermDefinition>(file, Errors);
                    ValidatePactTerms(terms, file.Path, Errors);
                    _pactTerms.AddRange(terms.Select(term => term.Entry));
                    break;
                case BondLevelsKind:
                    List<(BondLevelDefinition Entry, int Index)> levels = ReadEntries<BondLevelDefinition>(file, Errors);
                    ValidateBondLevels(levels, file.Path, Errors);
                    _bondLevels.AddRange(levels.Select(level => level.Entry));
                    break;
                case MovesKind:
                    foreach ((MoveDefinition entry, int index) in ReadEntries<MoveDefinition>(file, Errors))
                    {
                        string where = $"{file.Path}: entries[{index}]";
                        ValidateMove(entry, where, Errors);
                        _moves.Add((entry, where));
                    }

                    break;
                case CombatKind:
                    AddCombat(file);
                    break;
                case RunesKind:
                    foreach ((RuneDefinition entry, int index) in ReadEntries<RuneDefinition>(file, Errors))
                    {
                        ValidateRune(entry, $"{file.Path}: entries[{index}]", Errors);
                        _runes.Add(entry);
                    }

                    break;
                case SpellsKind:
                    foreach ((SpellDefinition entry, int index) in ReadEntries<SpellDefinition>(file, Errors))
                    {
                        _spells.Add((entry, $"{file.Path}: entries[{index}]"));
                    }

                    break;
                case EffectsKind:
                    foreach ((EffectDefinition entry, int index) in ReadEntries<EffectDefinition>(file, Errors))
                    {
                        ValidateEffect(entry, $"{file.Path}: entries[{index}]", Errors);
                        _effects.Add(entry);
                    }

                    break;
                case SkillTreesKind:
                    foreach ((SkillTreeDefinition entry, int index) in ReadEntries<SkillTreeDefinition>(file, Errors))
                    {
                        string where = $"{file.Path}: entries[{index}]";
                        ValidateSkillTree(entry, where, Errors);
                        _skillTrees.Add((entry, where));
                    }

                    break;
                default:
                    Errors.Add($"{file.Path}: unknown kind '{kind}'. Known kinds: {MemoryEventTypesKind}, {SpeciesKind}, {OpinionWeightsKind}, {GoalsKind}, {BehaviourTreesKind}, {PactTermsKind}, {BondLevelsKind}, {MovesKind}, {CombatKind}, {SkillTreesKind}, {RunesKind}, {SpellsKind}, {EffectsKind}.");
                    break;
            }
        }

        public DataRegistry Build()
        {
            foreach (string kind in SingleFileKinds)
            {
                List<string> paths = _filesByKind.GetValueOrDefault(kind) ?? [];
                if (paths.Count != 1)
                {
                    Errors.Add($"Expected exactly one '{kind}' file, found {paths.Count} ({string.Join(", ", paths)}).");
                }
            }

            ValidateGoalReferences(_goals, _speciesGoalRefs, Errors);
            ValidateTreeReferences(_trees, _goals, Errors);
            ValidateUniqueMemoryEventTypes(_memoryEventTypes, Errors);
            ValidateOpinionWeightReferences(_opinionWeights, _memoryEventTypes, Errors);
            _moves.AddRange(ComposeSpells(_spells, _runes, _effects, Errors));
            ValidateMoveReferences(_moves, _combatants, Errors);
            ValidateSkillTreeReferences(_skillTrees, _species, _moves, Errors);

            if (Errors.Count > 0)
            {
                throw new DataValidationException(Errors);
            }

            return new DataRegistry(
                [.. _memoryEventTypes.OrderBy(definition => definition.Id)],
                [.. _species.OrderBy(definition => definition.Id, StringComparer.Ordinal)],
                [.. _opinionWeights.Select(item => item.Entry).OrderBy(definition => definition.EventType)],
                [.. _goals.Select(item => item.Entry).OrderBy(definition => definition.Id)],
                [.. _trees.Select(item => item.Entry).OrderBy(definition => definition.Goal, StringComparer.Ordinal)],
                _pactTerms,
                [.. _bondLevels.OrderBy(level => level.Level)],
                [.. _moves.Select(item => item.Entry).OrderBy(definition => definition.Id, StringComparer.Ordinal)],
                _combat!,
                [.. _skillTrees.Select(item => item.Entry).OrderBy(definition => definition.Species, StringComparer.Ordinal)],
                [.. _spells.Select(item => item.Entry).OrderBy(definition => definition.Id, StringComparer.Ordinal)],
                [.. _effects.OrderBy(definition => definition.Id, StringComparer.Ordinal)]);
        }

        private void AddSpecies(DataFile file)
        {
            foreach ((SpeciesDefinition entry, int index) in ReadEntries<SpeciesDefinition>(file, Errors))
            {
                string where = $"{file.Path}: entries[{index}]";
                ValidateSpecies(entry, where, Errors);
                if (entry.Combat is null)
                {
                    Errors.Add($"{where}: combat is required.");
                }
                else
                {
                    ValidateCombatStats(entry.Combat, $"{where}: combat", Errors);
                    _combatants.Add((entry.Combat, $"{where}: combat"));
                }
                foreach (string goalKey in entry.Goals ?? [])
                {
                    _speciesGoalRefs.Add((goalKey, where));
                }

                if (entry.Id is not null && !_speciesSources.TryAdd(entry.Id, where))
                {
                    Errors.Add($"{where}: duplicate species id '{entry.Id}' (first defined at {_speciesSources[entry.Id]}).");
                }

                _species.Add(entry);
            }
        }

        private void AddCombat(DataFile file)
        {
            List<(CombatDefinition Entry, int Index)> entries = ReadEntries<CombatDefinition>(file, Errors);
            if (entries.Count != 1)
            {
                Errors.Add($"{file.Path}: a combat file has exactly one entry, found {entries.Count}.");
                return;
            }

            string where = $"{file.Path}: entries[0]";
            ValidateCombatRules(entries[0].Entry, where, Errors);
            if (entries[0].Entry.Player is not null)
            {
                _combatants.Add((entries[0].Entry.Player, $"{where}: player"));
            }

            _combat = entries[0].Entry;
        }
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

        CheckRange(entry.ReputationDelta, -MemoryEventTypeDefinition.MaxReputationDelta, MemoryEventTypeDefinition.MaxReputationDelta, "reputationDelta", where, errors);
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

    private static void ValidateGoal(GoalDefinition entry, string where, List<string> errors)
    {
        if (entry.Id == 0)
        {
            errors.Add($"{where}: goal id 0 is reserved for 'idle'.");
        }

        if (!IsPascalCaseLetters(entry.Key))
        {
            errors.Add($"{where}: key '{entry.Key}' must be PascalCase letters, e.g. 'Flee'.");
        }

        CheckRange(entry.Weight, 1, GoalDefinition.MaxWeight, "weight", where, errors);

        if (entry.Personality is null)
        {
            errors.Add($"{where}: personality must be a list (it may be empty).");
        }
        else
        {
            for (int i = 0; i < entry.Personality.Count; i++)
            {
                CheckRange(entry.Personality[i].Factor, -PersonalityFactor.MaxFactor, PersonalityFactor.MaxFactor, $"personality[{i}].factor", where, errors);
            }
        }

        if (entry.Considerations is null || entry.Considerations.Count == 0)
        {
            errors.Add($"{where}: a goal needs at least one consideration.");
            return;
        }

        for (int i = 0; i < entry.Considerations.Count; i++)
        {
            ValidateCurve(entry.Considerations[i]?.Curve, $"{where}: considerations[{i}].curve", errors);
        }
    }

    private static void ValidateCurve(CurveDefinition? curve, string where, List<string> errors)
    {
        if (curve is null)
        {
            errors.Add($"{where} is required.");
            return;
        }

        CheckRange(curve.Slope, -CurveLimit, CurveLimit, "slope", where, errors);
        CheckRange(curve.Offset, -CurveLimit, CurveLimit, "offset", where, errors);
        switch (curve.Type)
        {
            case CurveType.Logistic:
                CheckRange(curve.Centre, 0, 1000, "centre", where, errors);
                CheckRange(curve.Width, 1, 1000, "width", where, errors);
                break;
            case CurveType.Step:
                CheckRange(curve.Threshold, 0, 1000, "threshold", where, errors);
                CheckRange(curve.Low, 0, 1000, "low", where, errors);
                CheckRange(curve.High, 0, 1000, "high", where, errors);
                break;
            default:
                break;
        }
    }

    private static void ValidateGoalReferences(
        List<(GoalDefinition Entry, string Where)> goals, List<(string Key, string Where)> speciesGoalRefs, List<string> errors)
    {
        var ids = new HashSet<ushort>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach ((GoalDefinition entry, string where) in goals)
        {
            if (!ids.Add(entry.Id))
            {
                errors.Add($"{where}: goal id {entry.Id} is defined more than once.");
            }

            if (entry.Key is not null && !keys.Add(entry.Key))
            {
                errors.Add($"{where}: goal key '{entry.Key}' is defined more than once.");
            }
        }

        foreach ((string key, string where) in speciesGoalRefs)
        {
            if (!keys.Contains(key))
            {
                errors.Add($"{where}: goal '{key}' is not defined in the goals file.");
            }
        }
    }

    private static void ValidateTree(BehaviourTreeDefinition entry, string where, List<string> errors)
    {
        if (entry.Root is null)
        {
            errors.Add($"{where}: root is required.");
            return;
        }

        int nodes = 0;
        ValidateNode(entry.Root, $"{where}: root", depth: 1, ref nodes, errors);
        if (nodes > BehaviourTreeDefinition.MaxNodes)
        {
            errors.Add($"{where}: {nodes} nodes; a tree may have at most {BehaviourTreeDefinition.MaxNodes}.");
        }
    }

    private static void ValidateNode(BehaviourNodeDefinition? node, string where, int depth, ref int nodes, List<string> errors)
    {
        if (node is null)
        {
            errors.Add($"{where} is null.");
            return;
        }

        nodes++;
        if (depth > BehaviourTreeDefinition.MaxDepth)
        {
            errors.Add($"{where}: nested deeper than {BehaviourTreeDefinition.MaxDepth} levels.");
            return;
        }

        switch (node.Type)
        {
            case BehaviourNodeType.Sequence or BehaviourNodeType.Selector or BehaviourNodeType.Parallel:
                if (node.Children is null || node.Children.Count == 0)
                {
                    errors.Add($"{where}: a {node.Type} needs at least one child.");
                    break;
                }

                if (node.Type == BehaviourNodeType.Parallel && node.Children.Count > MaxParallelChildren)
                {
                    errors.Add($"{where}: a parallel node may have at most {MaxParallelChildren} children.");
                }

                for (int i = 0; i < node.Children.Count; i++)
                {
                    ValidateNode(node.Children[i], $"{where}.children[{i}]", depth + 1, ref nodes, errors);
                }

                break;
            case BehaviourNodeType.Cooldown or BehaviourNodeType.Repeat:
                if (node.Child is null)
                {
                    errors.Add($"{where}: a {node.Type} needs a child.");
                    break;
                }

                if (node.Type == BehaviourNodeType.Cooldown)
                {
                    CheckRange(node.Ticks, 1, int.MaxValue, "ticks", where, errors);
                }
                else
                {
                    CheckRange(node.Times, 0, ushort.MaxValue, "times", where, errors);
                }

                ValidateNode(node.Child, $"{where}.child", depth + 1, ref nodes, errors);
                break;
            case BehaviourNodeType.Condition:
                if (node.Input is null)
                {
                    errors.Add($"{where}: a condition needs an input.");
                }

                CheckRange(node.Value, 0, 1000, "value", where, errors);
                break;
            case BehaviourNodeType.Wait:
                CheckRange(node.Ticks, 1, int.MaxValue, "ticks", where, errors);
                break;
            case BehaviourNodeType.Action:
                ValidateAction(node, where, errors);
                break;
            default:
                errors.Add($"{where}: unknown node type.");
                break;
        }
    }

    private static void ValidateAction(BehaviourNodeDefinition node, string where, List<string> errors)
    {
        switch (node.Intent)
        {
            case null:
                errors.Add($"{where}: an action needs an intent.");
                break;
            case IntentKind.MoveTo:
                CheckRange(node.Speed, 1, MaxSpeedPercent, "speed", where, errors);
                CheckRange(node.Timeout, 1, int.MaxValue, "timeout", where, errors);
                break;
            case IntentKind.Emote when node.Emote is null:
                errors.Add($"{where}: an emote action needs an emote.");
                break;
            default:
                break;
        }
    }

    private static void ValidateTreeReferences(
        List<(BehaviourTreeDefinition Entry, string Where)> trees, List<(GoalDefinition Entry, string Where)> goals, List<string> errors)
    {
        var goalKeys = goals.Select(goal => goal.Entry.Key).Where(key => key is not null).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((BehaviourTreeDefinition entry, string where) in trees)
        {
            if (entry.Goal is null || !goalKeys.Contains(entry.Goal))
            {
                errors.Add($"{where}: tree for unknown goal '{entry.Goal}'.");
            }
            else if (!seen.Add(entry.Goal))
            {
                errors.Add($"{where}: goal '{entry.Goal}' has more than one tree.");
            }
        }

        foreach (string missing in goalKeys.Where(key => !seen.Contains(key)).Order(StringComparer.Ordinal))
        {
            errors.Add($"Goal '{missing}' has no behaviour tree; every goal needs exactly one (AI Bible §7.3).");
        }
    }

    // File order is each term's bit in saved Pacts, so the order itself is part of the save format (R26).
    private static void ValidatePactTerms(List<(PactTermDefinition Entry, int Index)> terms, string path, List<string> errors)
    {
        if (terms.Count == 0 || terms.Count > PactTermDefinition.MaxTerms)
        {
            errors.Add($"{path}: needs 1 to {PactTermDefinition.MaxTerms} pact terms, found {terms.Count}.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach ((PactTermDefinition term, int index) in terms)
        {
            string where = $"{path}: entries[{index}]";
            if (!IsPascalCaseLetters(term.Key) || !keys.Add(term.Key))
            {
                errors.Add($"{where}: key '{term.Key}' must be unique PascalCase letters.");
            }

            CheckRange(term.Base, 0, MaxTermValue, "base", where, errors);
            CheckRange(term.NeedWeight, 0, MaxTermValue, "needWeight", where, errors);
            if (term.Personality is null)
            {
                errors.Add($"{where}: personality must be a list (it may be empty).");
                continue;
            }

            for (int i = 0; i < term.Personality.Count; i++)
            {
                CheckRange(term.Personality[i].Factor, -PersonalityFactor.MaxFactor, PersonalityFactor.MaxFactor, $"personality[{i}].factor", where, errors);
            }
        }
    }

    private static void ValidateBondLevels(List<(BondLevelDefinition Entry, int Index)> levels, string path, List<string> errors)
    {
        BondLevelDefinition? previous = null;
        foreach ((BondLevelDefinition level, int index) in levels.OrderBy(item => item.Entry.Level))
        {
            string where = $"{path}: entries[{index}]";
            if (level.Level != (previous?.Level ?? -1) + 1)
            {
                errors.Add($"{where}: bond levels must be numbered 0, 1, 2… without gaps; found {level.Level}.");
            }

            CheckRange(level.MinTrust, -100, 100, "minTrust", where, errors);
            CheckRange(level.MinAffection, -100, 100, "minAffection", where, errors);
            CheckRange(level.CoreMemoriesWithOwner, 0, 8, "coreMemoriesWithOwner", where, errors);
            CheckRange(level.MinDaysBonded, 0, 3650, "minDaysBonded", where, errors);
            if (previous is not null && (level.MinTrust < previous.MinTrust || level.MinAffection < previous.MinAffection
                || level.CoreMemoriesWithOwner < previous.CoreMemoriesWithOwner || level.MinDaysBonded < previous.MinDaysBonded))
            {
                errors.Add($"{where}: requirements must not be lower than level {previous.Level}'s.");
            }

            previous = level;
        }

        if (levels.Count == 0)
        {
            errors.Add($"{path}: at least bond level 0 is required.");
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
        if (!Enum.IsDefined(entry.Class) || !Enum.IsDefined(entry.Tier))
        {
            errors.Add($"{where}: class and tier must be known values.");
        }

        if (entry.Levels is null || entry.Levels.Min < 1 || entry.Levels.Max < entry.Levels.Min || entry.Levels.Max > Progression.GrowthRules.Cap(entry.Tier))
        {
            errors.Add($"{where}: levels must be 1 <= min <= max <= the tier's cap.");
        }
        CheckRange(entry.BodyRadiusCm, MinBodyRadiusCm, MaxBodyRadiusCm, "bodyRadiusCm", where, errors);

        if (entry.Perception is null)
        {
            errors.Add($"{where}: perception is required.");
        }
        else
        {
            CheckRange(entry.Perception.SightRange, 1, PerceptionDefinition.MaxRange, "perception.sightRange", where, errors);
            CheckRange(entry.Perception.SightHalfAngle, 1, 180, "perception.sightHalfAngle", where, errors);
            CheckRange(entry.Perception.HearingRange, 1, PerceptionDefinition.MaxRange, "perception.hearingRange", where, errors);
        }

        if (entry.Goals is null || entry.Goals.Count == 0 || entry.Goals.Distinct(StringComparer.Ordinal).Count() != entry.Goals.Count)
        {
            errors.Add($"{where}: goals must be a non-empty list of goal keys without duplicates.");
        }
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
