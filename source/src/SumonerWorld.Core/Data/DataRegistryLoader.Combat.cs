using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Data;

/// <summary>Validation of the combat data (combat spec §2, §3, §5, §6).</summary>
public static partial class DataRegistryLoader
{
    private const int MaxMoveTicks = 300;
    private const int MaxReachCm = 3000;
    private const int MaxDamage = 10_000;
    private const int MaxStat = 100_000;
    private const int MaxPower = 1000;
    private const int MaxResist = 750;
    private const int PerMille = 1000;
    private const int MaxRegen = 1000;

    private static void ValidateMove(MoveDefinition entry, string where, List<string> errors)
    {
        if (!IsSnakeCaseId(entry.Id))
        {
            errors.Add($"{where}: id '{entry.Id}' must be snake_case, e.g. 'light'.");
        }

        CheckRange(entry.WindupTicks, 0, MaxMoveTicks, "windupTicks", where, errors);
        CheckRange(entry.ActiveTicks, 1, MaxMoveTicks, "activeTicks", where, errors);
        CheckRange(entry.RecoveryTicks, 0, MaxMoveTicks, "recoveryTicks", where, errors);
        CheckRange(entry.Damage, 0, MaxDamage, "damage", where, errors);
        CheckRange(entry.Stamina, 0, MaxStat, "stamina", where, errors);
        CheckRange(entry.Mana, 0, MaxStat, "mana", where, errors);
        CheckRange(entry.Poise, 0, MaxStat, "poise", where, errors);
        if (entry.Shape is null)
        {
            errors.Add($"{where}: shape is required.");
            return;
        }

        CheckRange(entry.Shape.ReachCm, 1, MaxReachCm, "shape.reachCm", where, errors);
        switch (entry.Shape.Kind)
        {
            case MoveShapeKind.Arc:
                CheckRange(entry.Shape.AngleDeg, 1, 360, "shape.angleDeg", where, errors);
                break;
            case MoveShapeKind.Line:
                CheckRange(entry.Shape.WidthCm, 1, MaxReachCm, "shape.widthCm", where, errors);
                break;
            default:
                break;
        }
    }

    private static void ValidateCombatStats(CombatStatsDefinition stats, string where, List<string> errors)
    {
        CheckRange(stats.Health, 1, MaxStat, "health", where, errors);
        CheckRange(stats.Stamina, 0, MaxStat, "stamina", where, errors);
        CheckRange(stats.Mana, 0, MaxStat, "mana", where, errors);
        CheckRange(stats.Power, 1, MaxPower, "power", where, errors);
        CheckRange(stats.Guard, 0, MaxDamage, "guard", where, errors);
        CheckRange(stats.Poise, 0, MaxStat, "poise", where, errors);
        if (stats.Resist is null)
        {
            errors.Add($"{where}: resist is required.");
        }
        else
        {
            CheckRange(stats.Resist.Physical, 0, MaxResist, "resist.physical", where, errors);
            CheckRange(stats.Resist.Primal, 0, MaxResist, "resist.primal", where, errors);
        }

        if (stats.Moves is null || stats.Moves.Count == 0)
        {
            errors.Add($"{where}: moves needs at least one move (the first is the basic attack).");
        }
    }

    private static void ValidateCombatRules(CombatDefinition entry, string where, List<string> errors)
    {
        if (entry.Player is null)
        {
            errors.Add($"{where}: player is required.");
        }
        else
        {
            ValidateCombatStats(entry.Player, $"{where}: player", errors);
        }

        CheckRange(entry.StaminaRegenPerSecond, 0, MaxRegen, "staminaRegenPerSecond", where, errors);
        CheckRange(entry.StaminaRegenDelayTicks, 0, MaxMoveTicks, "staminaRegenDelayTicks", where, errors);
        CheckRange(entry.ManaRegenPerSecond, 0, MaxRegen, "manaRegenPerSecond", where, errors);
        CheckRange(entry.HealthRegenPerMillePerSecond, 0, PerMille, "healthRegenPerMillePerSecond", where, errors);
        CheckRange(entry.HealthRegenDelayTicks, 0, MaxStat, "healthRegenDelayTicks", where, errors);
        CheckRange(entry.YieldBelowPerMille, 0, PerMille, "yieldBelowPerMille", where, errors);
        CheckRange(entry.YieldMaxBoldness, -100, 100, "yieldMaxBoldness", where, errors);
        CheckRange(entry.SpareAfterTicks, 1, MaxStat, "spareAfterTicks", where, errors);
        CheckRange(entry.PlayerDownTicks, 1, MaxStat, "playerDownTicks", where, errors);
    }

    private static void ValidateMoveReferences(
        List<(MoveDefinition Entry, string Where)> moves, List<(CombatStatsDefinition Stats, string Where)> combatants, List<string> errors)
    {
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((MoveDefinition move, string where) in moves)
        {
            if (move.Id is not null && !known.TryAdd(move.Id, where))
            {
                errors.Add($"{where}: duplicate move id '{move.Id}' (first defined at {known[move.Id]}).");
            }
        }

        foreach ((CombatStatsDefinition stats, string where) in combatants)
        {
            foreach (string id in stats.Moves ?? [])
            {
                if (!known.ContainsKey(id))
                {
                    errors.Add($"{where}: unknown move '{id}'.");
                }
            }
        }
    }
}
