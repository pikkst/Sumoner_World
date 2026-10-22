using System.Numerics;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Quests;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Academy;

/// <summary>The practical moments a student has done (Academy spec §3.1, M2-21b), as bits of <see cref="AcademyStanding.Moments"/>.</summary>
[Flags]
public enum PracticalMoments
{
    /// <summary>None yet.</summary>
    None = 0,

    /// <summary>Beat the training construct.</summary>
    Construct = 1,

    /// <summary>Read a creature's feeling right.</summary>
    Read = 2,

    /// <summary>Offered the construct a Pact (the rehearsal).</summary>
    Rehearsal = 4,

    /// <summary>Read a creature wrong at least once (the reading then gives less).</summary>
    MissedRead = 8,
}

/// <summary>How a creature can feel, as the reading test asks it (plain words; several emotes share one).</summary>
public enum Feeling : byte
{
    /// <summary>Nothing special.</summary>
    Calm = 0,

    /// <summary>Fear.</summary>
    Scared = 1,

    /// <summary>Curiosity.</summary>
    Curious = 2,

    /// <summary>A grudge.</summary>
    Angry = 3,

    /// <summary>Trust or affection.</summary>
    Happy = 4,

    /// <summary>Grief.</summary>
    Sad = 5,
}

/// <summary>The Academy's arena rules (M2-21b).</summary>
public static class ArenaRules
{
    /// <summary>The farthest a creature may be to read it, in metres.</summary>
    public const float ReadRange = 20f;

    /// <summary>The feeling an emote shows.</summary>
    public static Feeling FeelingOf(Emote emote) => emote switch
    {
        Emote.Fear => Feeling.Scared,
        Emote.Curiosity => Feeling.Curious,
        Emote.Grudge => Feeling.Angry,
        Emote.Trust or Emote.Affection => Feeling.Happy,
        Emote.Grief => Feeling.Sad,
        _ => Feeling.Calm,
    };

    /// <summary>True when a player standing at <paramref name="at"/> may not fight (inside the campus, outside the arena).</summary>
    public static bool IsSafe(CreatureWorld world, Vector3 at)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Data.Academy is AcademyDefinition academy && academy.IsSafe(at.X, at.Z);
    }

    /// <summary>The detail of a fight refused in the Academy's safe zone.</summary>
    public const byte SafeZoneDetail = 200;

    /// <summary>A refusal when the player stands in the safe zone (null when they may fight).</summary>
    public static IntentResult? SafeZone(CreatureWorld world, Entity avatar)
    {
        ArgumentNullException.ThrowIfNull(world);
        return IsSafe(world, world.World.GetComponent<Position>(avatar).Value)
            ? new IntentResult(IntentStatus.Rejected, IntentRejectReason.WrongState, SafeZoneDetail)
            : null;
    }

    /// <summary>True while the student's ranking quest is still open (practical moments count for the rank only then).</summary>
    public static bool Ranking(CreatureWorld world, ulong account)
    {
        ArgumentNullException.ThrowIfNull(world);
        return !world.Economy.Academy.Of(account).Ranked
            && world.Economy.Quests.Of(account).StatusOf(RankingRules.RankingQuest(world.Data)) == QuestStatus.Active;
    }

    /// <summary>Marks a practical moment done for a student, adding its bonus once.</summary>
    public static void Mark(CreatureWorld world, ulong account, PracticalMoments moment, int bonus)
    {
        ArgumentNullException.ThrowIfNull(world);
        AcademyStanding standing = world.Economy.Academy.Of(account);
        if ((standing.Moments & moment) != 0 || !Ranking(world, account))
        {
            return;
        }

        standing.Moments |= moment;
        standing.Bonus = Math.Min(RankingRules.MaxBonus, standing.Bonus + bonus);
    }
}

/// <summary>
/// A student tells how a creature feels (Academy spec §3.1 and §6, M2-21b: reading body language). Only while the ranking quest
/// asks for it, on a creature within <see cref="ArenaRules.ReadRange"/>. A right answer counts for the read objective and adds
/// the bonus (less after a wrong try); a wrong one is refused kindly, to try again.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player.</param>
/// <param name="Creature">The creature's stable id.</param>
/// <param name="Guess">The feeling the student sees.</param>
public record struct ReadTransaction(CreatureWorld Creatures, Entity Avatar, StableId Creature, Feeling Guess) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        if (!Creatures.World.Store<PlayerAvatar>().Has(Avatar) || !Creatures.TryFindCreature(Creature, out Entity creature))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if (Vector3.Distance(Creatures.World.GetComponent<Position>(Avatar).Value, Creatures.World.GetComponent<Position>(creature).Value) > ArenaRules.ReadRange)
        {
            return IntentResult.Reject(IntentRejectReason.OutOfRange);
        }

        ulong account = Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId;
        QuestProgress? quest = Creatures.Economy.Quests.Of(account).Find(RankingRules.RankingQuest(Creatures.Data));
        bool asking = quest is { Status: QuestStatus.Active }
            && Creatures.Data.Quests[quest.Quest].Steps[quest.Step].Objectives.Any(objective => objective.Type == ObjectiveType.Read);
        if (!asking || (uint)Guess > (uint)Feeling.Sad)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        return IntentResult.Applied();
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        ulong account = Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId;
        Creatures.TryFindCreature(Creature, out Entity creature);
        Feeling shown = ArenaRules.FeelingOf(Creatures.World.GetComponent<CurrentEmote>(creature).Value);
        AcademyDefinition academy = Creatures.Data.Academy!;
        if (shown != Guess)
        {
            Creatures.Economy.Academy.Of(account).Moments |= PracticalMoments.MissedRead;
            return IntentResult.Applied(0);
        }

        bool missed = (Creatures.Economy.Academy.Of(account).Moments & PracticalMoments.MissedRead) != 0;
        ArenaRules.Mark(Creatures, account, PracticalMoments.Read, missed ? academy.LateReadBonus : academy.MomentBonus);
        Creatures.Events.Publish(new QuestEvent(account, ObjectiveType.Read, 0, 1));
        return IntentResult.Applied(1);
    }
}

/// <summary>
/// The Grand Arena (M2-21b): keeps one training construct standing in it (a new one after a fight), and gives the practical
/// moments' bonuses: beating the construct, and offering it a Pact (the rehearsal). Runs after the players' intents.
/// </summary>
public sealed class ArenaSystem : ISimulationSystem
{
    private const ulong CheckEveryTicks = 90;

    private readonly CreatureWorld _world;
    private readonly ushort _construct;
    private readonly AcademyDefinition? _academy;

    /// <summary>Creates the system for a world (it does nothing when the data has no Academy).</summary>
    public ArenaSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _academy = world.Data.Academy;
        _construct = _academy is null ? ushort.MaxValue : world.SpeciesIndex(_academy.ConstructSpecies);
    }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.EventDispatch;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        if (_academy is not AcademyDefinition academy)
        {
            return;
        }

        foreach (CombatantDefeated defeat in _world.Events.Read<CombatantDefeated>())
        {
            for (int i = 0; i < 4 && defeat.Species == _construct; i++)
            {
                if (defeat.Fighters[i] != 0)
                {
                    ArenaRules.Mark(_world, defeat.Fighters[i], PracticalMoments.Construct, academy.MomentBonus);
                }
            }
        }

        foreach (PlayerInteracted action in _world.Events.Read<PlayerInteracted>())
        {
            if (action.Kind == InteractionKind.Pact && action.Species == _construct)
            {
                ArenaRules.Mark(_world, action.Account, PracticalMoments.Rehearsal, academy.MomentBonus);
            }
        }

        if (tick.Index % CheckEveryTicks == 0 && !ConstructStands(academy))
        {
            var at = new Vector3(academy.ArenaX, 0, academy.ArenaZ);
            at.Y = _world.Settings.Ground?.HeightAt(at.X, at.Z) ?? 0;
            _world.SpawnCreature(_construct, at, new Personality(0, 0, 0, 0), group: 0);
        }
    }

    // A construct alive (not down) in the arena.
    private bool ConstructStands(AcademyDefinition academy)
    {
        ComponentStore<SpeciesRef> species = _world.World.Store<SpeciesRef>();
        ReadOnlySpan<Entity> creatures = species.Entities;
        for (int i = 0; i < creatures.Length; i++)
        {
            if (species.Components[i].Index != _construct || _world.World.GetComponent<Combatant>(creatures[i]).Health <= 0)
            {
                continue;
            }

            Vector3 at = _world.World.GetComponent<Position>(creatures[i]).Value;
            if (((at.X - academy.ArenaX) * (at.X - academy.ArenaX)) + ((at.Z - academy.ArenaZ) * (at.Z - academy.ArenaZ)) <= academy.ArenaRadius * academy.ArenaRadius * 4)
            {
                return true;
            }
        }

        return false;
    }
}
