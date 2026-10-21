using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.World;

namespace SumonerWorld.Persistence.Records;

/// <summary>Converts Core save types to payload records and back.</summary>
internal static class SaveMapping
{
    public static CreatureRecord ToRecord(CreatureSave save) => new()
    {
        Species = save.Species,
        Position = [save.Position.X, save.Position.Y, save.Position.Z],
        Facing = [save.Facing.X, save.Facing.Y, save.Facing.Z],
        Group = save.Group,
        Mind = ToRecord(save.Mind),
        Bond = new BondRecord
        {
            State = (byte)save.Bond.State,
            Owner = save.Bond.Owner,
            Terms = save.Bond.Terms.Mask,
            BrokenTerms = save.Bond.BrokenTerms,
            BondLevel = save.Bond.BondLevel,
            Summoned = save.Bond.Summoned,
            PactTick = save.Bond.PactTick,
            DistrustSinceTick = save.Bond.DistrustSinceTick,
            Distrustful = save.Bond.Distrustful,
        },
        Growth = save.Growth is CreatureGrowth growth
            ? new GrowthRecord
            {
                Tier = (byte)growth.Tier,
                Level = growth.Level,
                Stage = (byte)growth.Stage,
                CombatXp = growth.CombatXp,
                WorkXp = growth.WorkXp,
                TravelXp = growth.TravelXp,
                TravelCm = growth.TravelCm,
            }
            : null,
        Equipped = save.Equipped ?? [],
        Combos = save.Combos is CreatureCombos combos
            ? new CombosRecord { Learned = combos.Learned, Landed = [combos.Landed0, combos.Landed1, combos.Landed2, combos.Landed3], SelfStarted = combos.SelfStarted }
            : null,
        Skills = save.Skills is CreatureSkills skills
            ? new SkillsRecord { Learned = skills.Learned, Spent = skills.Spent, UnspentSince = skills.UnspentSince, PickedItself = skills.PickedItself }
            : null,
    };

    public static CreatureSave FromRecord(StableId id, CreatureRecord record)
    {
        if (record.Position.Count != 3 || record.Facing.Count != 3)
        {
            throw new RecordFormatException($"Creature {id.Value} has a malformed position or facing.");
        }

        BondRecord bond = record.Bond;
        return new CreatureSave(
            id,
            record.Species,
            new Vector3(record.Position[0], record.Position[1], record.Position[2]),
            new Vector3(record.Facing[0], record.Facing[1], record.Facing[2]),
            record.Group,
            FromRecord(record.Mind),
            new BondState(
                (OwnershipState)bond.State, bond.Owner, new PactTerms(bond.Terms), bond.BrokenTerms, bond.BondLevel, bond.Summoned, bond.PactTick, bond.DistrustSinceTick, bond.Distrustful),
            record.Growth is GrowthRecord growth
                ? new CreatureGrowth(growth.Tier, growth.Level, growth.Stage, growth.CombatXp, growth.WorkXp, growth.TravelXp, growth.TravelCm)
                : null,
            record.Skills is SkillsRecord skills ? new CreatureSkills(skills.Learned, skills.UnspentSince, 0, skills.Spent, skills.PickedItself) : null,
            record.Combos is CombosRecord combos ? Combos(combos) : null,
            record.Equipped);
    }

    private static CreatureCombos Combos(CombosRecord record)
    {
        int Landed(int i) => i < record.Landed.Count ? record.Landed[i] : 0;
        return new CreatureCombos(0, record.Learned, Landed(0), Landed(1), Landed(2), Landed(3), record.SelfStarted);
    }

    private static MindRecord ToRecord(MindState mind) => new()
    {
        Birth = Traits(mind.Birth),
        Current = Traits(mind.Current),
        Core = [.. mind.CoreRecords.Select(ToRecord)],
        Episodic = [.. mind.EpisodicRecords.Select(ToRecord)],
        Baselines = [.. mind.Baselines.Select(baseline => new BaselineRecord
        {
            SubjectKind = (byte)baseline.Subject.Kind,
            SubjectId = baseline.Subject.Id,
            Trust = baseline.Tenths.Trust,
            Fear = baseline.Tenths.Fear,
            Affection = baseline.Tenths.Affection,
            Respect = baseline.Tenths.Respect,
        })],
        Needs = [.. mind.Needs.Select(need => new NeedRecord { Milli = need.Milli, Tick = need.Tick, Rate = need.Rate })],
    };

    private static MindState FromRecord(MindRecord mind) => new(
        Personality(mind.Birth),
        Personality(mind.Current),
        [.. mind.Core.Select(FromRecord)],
        [.. mind.Episodic.Select(FromRecord)],
        [.. mind.Baselines.Select(baseline => new OpinionBaseline(
            new MemorySubject((SubjectKind)baseline.SubjectKind, baseline.SubjectId),
            new Opinion(baseline.Trust, baseline.Fear, baseline.Affection, baseline.Respect)))],
        [.. mind.Needs.Select(need => new NeedState(need.Milli, need.Tick, need.Rate))]);

    private static MemoryRecordData ToRecord(MemoryRecord memory) => new()
    {
        EventType = memory.EventType,
        SubjectKind = (byte)memory.SubjectKind,
        SubjectId = memory.SubjectId,
        Valence = memory.Valence,
        Salience = memory.Salience,
        CreatedTick = memory.CreatedTick,
        LastReinforcedTick = memory.LastReinforcedTick,
        Count = memory.Count,
        Tags = (byte)memory.Tags,
    };

    private static MemoryRecord FromRecord(MemoryRecordData memory) => new(
        memory.EventType,
        new MemorySubject((SubjectKind)memory.SubjectKind, memory.SubjectId),
        memory.Valence,
        memory.Salience,
        memory.CreatedTick,
        memory.LastReinforcedTick,
        memory.Count,
        (MemoryTags)memory.Tags);

    private static sbyte[] Traits(Personality personality) => [personality.Boldness, personality.Loyalty, personality.Curiosity, personality.Temper];

    private static Personality Personality(IReadOnlyList<sbyte> traits) =>
        traits.Count == 4 ? new Personality(traits[0], traits[1], traits[2], traits[3]) : throw new RecordFormatException("A personality has 4 traits.");
}
