using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Data;

/// <summary>Runes, spells and status effects (combat spec §7–8, M2-11): validation, and spells composed into moves.</summary>
public static partial class DataRegistryLoader
{
    /// <summary>The status effects the game knows (each has its own rules in code).</summary>
    public static readonly IReadOnlyList<string> KnownEffects = ["rooted", "slowed", "mending"];

    private const int MaxSpellRangeCm = 5000;
    private const int MaxMana = 1000;

    private static void ValidateRune(RuneDefinition rune, string where, List<string> errors)
    {
        if (!IsSnakeCaseId(rune.Id))
        {
            errors.Add($"{where}: id '{rune.Id}' must be snake_case.");
        }

        if (rune.Slot == RuneSlot.Form)
        {
            CheckRange(rune.RangeCm, 1, MaxSpellRangeCm, "rangeCm", where, errors);
            if (rune.Form == SpellForm.Bolt)
            {
                CheckRange(rune.WidthCm, 1, MaxReachCm, "widthCm", where, errors);
            }
            else if (rune.Form == SpellForm.Burst)
            {
                CheckRange(rune.RadiusCm, 1, MaxReachCm, "radiusCm", where, errors);
            }

            return;
        }

        CheckRange(rune.Damage, 0, MaxDamage, "damage", where, errors);
        CheckRange(rune.Heal, 0, MaxStat, "heal", where, errors);
        if (rune.Effect is string effect && !KnownEffects.Contains(effect))
        {
            errors.Add($"{where}: unknown effect '{effect}' (known: {string.Join(", ", KnownEffects)}).");
        }
    }

    private static void ValidateEffect(EffectDefinition effect, string where, List<string> errors)
    {
        if (!KnownEffects.Contains(effect.Id))
        {
            errors.Add($"{where}: unknown effect '{effect.Id}' (known: {string.Join(", ", KnownEffects)}).");
        }

        CheckRange(effect.Ticks, 1, MaxStat, "ticks", where, errors);
        CheckRange(effect.SpeedPercent, 0, 100, "speedPercent", where, errors);
    }

    // Each spell is a form rune and an essence rune; it becomes a move with the spell's id: the cast time is its wind-up (the
    // telegraph), the form its shape, the essence its damage, effect and healing (GDD §4: new spells need no code).
    private static List<(MoveDefinition Move, string Where)> ComposeSpells(
        List<(SpellDefinition Entry, string Where)> spells, List<RuneDefinition> runes, List<EffectDefinition> effects, List<string> errors)
    {
        var composed = new List<(MoveDefinition, string)>();
        foreach ((SpellDefinition spell, string where) in spells)
        {
            CheckRange(spell.Mana, 0, MaxMana, "mana", where, errors);
            CheckRange(spell.CastTicks, 1, MaxMoveTicks, "castTicks", where, errors);
            CheckRange(spell.RecoveryTicks, 0, MaxMoveTicks, "recoveryTicks", where, errors);
            RuneDefinition? form = spell.Runes.Count == 2 ? runes.FirstOrDefault(rune => rune.Id == spell.Runes[0] && rune.Slot == RuneSlot.Form) : null;
            RuneDefinition? essence = spell.Runes.Count == 2 ? runes.FirstOrDefault(rune => rune.Id == spell.Runes[1] && rune.Slot == RuneSlot.Essence) : null;
            if (form is null || essence is null)
            {
                errors.Add($"{where}: a spell is [form rune, essence rune]; got [{string.Join(", ", spell.Runes)}].");
                continue;
            }

            if (essence.Effect is string effect && !effects.Any(definition => definition.Id == effect))
            {
                errors.Add($"{where}: rune '{essence.Id}' needs effect '{effect}', which effects.json does not define.");
            }

            var shape = new MoveShapeDefinition
            {
                Kind = form.Form switch
                {
                    SpellForm.Bolt => MoveShapeKind.Line,
                    SpellForm.Burst => MoveShapeKind.Burst,
                    _ => MoveShapeKind.Ward,
                },
                ReachCm = form.RangeCm,
                WidthCm = form.WidthCm,
                RadiusCm = form.RadiusCm,
            };
            composed.Add((new MoveDefinition
            {
                Id = spell.Id,
                WindupTicks = spell.CastTicks,
                ActiveTicks = 1,
                RecoveryTicks = spell.RecoveryTicks,
                Shape = shape,
                Damage = essence.Damage,
                DamageType = essence.DamageType,
                Stamina = 0,
                Mana = spell.Mana,
                Poise = 0,
                Tags = ["spell", essence.School, form.Id, essence.Id],
                Effect = essence.Effect,
                Heal = essence.Heal,
                School = essence.School,
            }, where));
        }

        return composed;
    }
}
