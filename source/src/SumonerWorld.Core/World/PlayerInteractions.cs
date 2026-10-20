using System.Numerics;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecosystem;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Transactions;

namespace SumonerWorld.Core.World;

/// <summary>What a player did to creatures (M1-23 Memory Grove, QA plan B2). Values are saved in telemetry: never renumber.</summary>
public enum InteractionKind : byte
{
    /// <summary>Gave food to one creature.</summary>
    Feed = 1,

    /// <summary>Struck one creature; the third hit kills it.</summary>
    Hit = 2,

    /// <summary>Freed a snared creature.</summary>
    Help = 3,

    /// <summary>Shouted at every predator nearby.</summary>
    Scare = 4,

    /// <summary>Offered a Pact (see <see cref="OfferPactTransaction"/>).</summary>
    Pact = 5,
}

/// <summary>A creature is caught in a snare: it cannot move until a player frees it (M1-23 Memory Grove).</summary>
/// <param name="Since">Tick it was snared.</param>
public record struct Snared(ulong Since);

/// <summary>Hits a creature has taken (not saved; a wounded creature heals when the server restarts).</summary>
/// <param name="Count">Hits so far.</param>
public record struct Wounds(int Count);

/// <summary>Published when a player interaction is applied (telemetry, M1-23).</summary>
/// <param name="Account">The player.</param>
/// <param name="Kind">What they did.</param>
/// <param name="Species">Target species index (the first predator for Scare); <see cref="ushort.MaxValue"/> when none.</param>
/// <param name="Affected">Creatures affected.</param>
public readonly record struct PlayerInteracted(ulong Account, InteractionKind Kind, ushort Species, int Affected);

/// <summary>Published when an interaction formed or strengthened a memory about a player (telemetry, M1-23).</summary>
/// <param name="Creature">The remembering creature.</param>
/// <param name="EventType">Memory event type id.</param>
/// <param name="Account">The player it is about.</param>
/// <param name="Outcome">Created, reinforced, or a new core memory.</param>
public readonly record struct MemoryFormed(StableId Creature, ushort EventType, ulong Account, MemoryOutcome Outcome);

/// <summary>
/// Shared rules of the Memory Grove interactions (QA plan B2): ranges, cooldowns and what each one does to a creature's mind.
/// Every interaction leaves a memory about the player (AI Bible §4.2), a short emotion change, and a reputation record for the
/// creature's kind in the region (§4.5). No randomness (R11).
/// </summary>
public static class PlayerInteractions
{
    /// <summary>Farthest distance for feeding, hitting and helping, in metres (ground plane, between body edges).</summary>
    public const float TouchRange = 3f;

    /// <summary>Radius a shout reaches, in metres.</summary>
    public const float ScareRadius = 12f;

    /// <summary>Hits that kill a creature.</summary>
    public const int HitsToKill = 3;

    /// <summary>Same-species creatures this close to a kill witness it, in metres.</summary>
    public const float WitnessRadius = 30f;

    /// <summary>Hunger a meal removes (0–100).</summary>
    public const int MealSize = 40;

    /// <summary>Most witnesses or scared predators per interaction.</summary>
    public const int MaxAffected = 32;

    internal const int EmotionNudge = 40;

    /// <summary>True when the avatar is within <paramref name="range"/> of the creature's body edge.</summary>
    internal static bool InReach(CreatureWorld world, Entity avatar, Entity creature, float range, BodySizes sizes)
    {
        Vector3 a = world.World.GetComponent<Position>(avatar).Value;
        Vector3 b = world.World.GetComponent<Position>(creature).Value;
        float ground = new Vector2(a.X - b.X, a.Z - b.Z).Length();
        return ground - sizes.RadiusOf(creature) - BodySizes.PlayerRadius <= range;
    }

    /// <summary>Validation shared by the single-target interactions.</summary>
    internal static IntentResult ValidateTouch(CreatureWorld world, CooldownTracker cooldowns, Entity avatar, StableId target, ushort kind, ulong now, out Entity creature)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(cooldowns);
        creature = default;
        if (!world.World.Store<PlayerAvatar>().Has(avatar))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if (!world.TryFindCreature(target, out creature))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if (!InReach(world, avatar, creature, TouchRange, world.Sizes))
        {
            return IntentResult.Reject(IntentRejectReason.OutOfRange);
        }

        return cooldowns.IsReady(world.World.GetComponent<PlayerAvatar>(avatar).AccountId, kind, now)
            ? IntentResult.Applied()
            : IntentResult.Reject(IntentRejectReason.OnCooldown);
    }

    /// <summary>The creature remembers an event about the player; the memory is published and recorded for reputation.</summary>
    internal static void Remember(CreatureWorld world, Entity creature, string eventKey, ulong account, ulong now)
    {
        ushort eventType = world.Rules.FindByKey(eventKey) ?? throw new InvalidOperationException($"Game data has no memory event '{eventKey}'.");
        MindHandle mind = world.World.GetComponent<MindRef>(creature).Handle;
        RememberResult result = world.Minds.Remember(mind, eventType, new MemorySubject(SubjectKind.Player, account), now);
        Vector3 where = world.World.GetComponent<Position>(creature).Value;
        world.Reputation.Record(eventType, new ReputationKey(world.World.GetComponent<SpeciesRef>(creature).Index, world.RegionOf(where), account));
        world.Events.Publish(new MemoryFormed(world.World.GetComponent<StableId>(creature), eventType, account, result.Outcome));
    }

    /// <summary>Raises fear in timid creatures and anger in bold ones (AI Bible §6.1).</summary>
    internal static void Startle(CreatureWorld world, Entity creature, int amount, ulong now)
    {
        MindHandle mind = world.World.GetComponent<MindRef>(creature).Handle;
        Emotion emotion = world.Minds.GetPersonality(mind).Boldness >= 0 ? Emotion.Anger : Emotion.Fear;
        world.Appraisal.Stimulate(mind, emotion, amount, now);
    }
}

/// <summary>A player feeds a creature: <c>FedBy</c>, less hunger, a little joy. Detail 0.</summary>
/// <param name="Creatures">The world.</param>
/// <param name="Cooldowns">Per-player cooldowns.</param>
/// <param name="Avatar">The player's avatar.</param>
/// <param name="Creature">The creature.</param>
public record struct FeedTransaction(CreatureWorld Creatures, CooldownTracker Cooldowns, Entity Avatar, StableId Creature) : ITransaction
{
    /// <summary>Cooldown kind id.</summary>
    public const ushort Kind = 2;

    /// <summary>One meal per 2 s per player.</summary>
    public const ulong CooldownTicks = 60;

    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now) => PlayerInteractions.ValidateTouch(Creatures, Cooldowns, Avatar, Creature, Kind, now, out _);

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Creatures.TryFindCreature(Creature, out Entity creature);
        ulong account = Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId;
        MindHandle mind = Creatures.World.GetComponent<MindRef>(creature).Handle;
        PlayerInteractions.Remember(Creatures, creature, "FedBy", account, now);
        Creatures.Minds.SatisfyNeed(mind, Need.Hunger, PlayerInteractions.MealSize, now);
        Creatures.Appraisal.Stimulate(mind, Emotion.Joy, PlayerInteractions.EmotionNudge, now);
        Creatures.Events.Publish(new PlayerInteracted(account, InteractionKind.Feed, Creatures.World.GetComponent<SpeciesRef>(creature).Index, 1));
        Cooldowns.Start(account, Kind, now, CooldownTicks);
        return IntentResult.Applied();
    }
}

/// <summary>
/// A player hits a creature: <c>AttackedBy</c>, fear (timid) or anger (bold). The <see cref="PlayerInteractions.HitsToKill"/>th hit
/// kills it: every same-species creature within <see cref="PlayerInteractions.WitnessRadius"/> remembers <c>KilledKin</c> (rumours
/// carry it further, AI Bible §4.5) and the creature is removed. Detail 1 when it died.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Cooldowns">Per-player cooldowns.</param>
/// <param name="Avatar">The player's avatar.</param>
/// <param name="Creature">The creature.</param>
public record struct HitTransaction(CreatureWorld Creatures, CooldownTracker Cooldowns, Entity Avatar, StableId Creature) : ITransaction
{
    /// <summary>Cooldown kind id.</summary>
    public const ushort Kind = 3;

    /// <summary>One hit per 0.7 s per player.</summary>
    public const ulong CooldownTicks = 21;

    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now) => PlayerInteractions.ValidateTouch(Creatures, Cooldowns, Avatar, Creature, Kind, now, out _);

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Creatures.TryFindCreature(Creature, out Entity creature);
        EcsWorld world = Creatures.World;
        ulong account = world.GetComponent<PlayerAvatar>(Avatar).AccountId;
        ushort species = world.GetComponent<SpeciesRef>(creature).Index;
        PlayerInteractions.Remember(Creatures, creature, "AttackedBy", account, now);
        PlayerInteractions.Startle(Creatures, creature, PlayerInteractions.EmotionNudge, now);
        Cooldowns.Start(account, Kind, now, CooldownTicks);

        int wounds = world.Has<Wounds>(creature) ? world.GetComponent<Wounds>(creature).Count + 1 : 1;
        if (wounds < PlayerInteractions.HitsToKill)
        {
            if (world.Has<Wounds>(creature))
            {
                world.GetComponent<Wounds>(creature) = new Wounds(wounds);
            }
            else
            {
                world.Add(creature, new Wounds(wounds));
            }

            Creatures.Events.Publish(new PlayerInteracted(account, InteractionKind.Hit, species, 1));
            return IntentResult.Applied();
        }

        int witnesses = Creatures.WitnessKill(creature, species, account, now);
        Creatures.DespawnCreature(creature);
        Creatures.Events.Publish(new PlayerInteracted(account, InteractionKind.Hit, species, 1 + witnesses));
        return IntentResult.Applied(1);
    }
}

/// <summary>A player frees a snared creature: <c>SavedLifeOf</c> (a core memory, Loyalty +3), joy. Rejected if it is not snared.</summary>
/// <param name="Creatures">The world.</param>
/// <param name="Cooldowns">Per-player cooldowns.</param>
/// <param name="Avatar">The player's avatar.</param>
/// <param name="Creature">The creature.</param>
public record struct HelpTransaction(CreatureWorld Creatures, CooldownTracker Cooldowns, Entity Avatar, StableId Creature) : ITransaction
{
    /// <summary>Cooldown kind id.</summary>
    public const ushort Kind = 4;

    /// <summary>One rescue per second per player.</summary>
    public const ulong CooldownTicks = 30;

    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        IntentResult touch = PlayerInteractions.ValidateTouch(Creatures, Cooldowns, Avatar, Creature, Kind, now, out Entity creature);
        return touch.IsApplied && !Creatures.World.Has<Snared>(creature) ? IntentResult.Reject(IntentRejectReason.WrongState) : touch;
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Creatures.TryFindCreature(Creature, out Entity creature);
        ulong account = Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId;
        Creatures.Release(creature);
        PlayerInteractions.Remember(Creatures, creature, "SavedLifeOf", account, now);
        Creatures.Appraisal.Stimulate(Creatures.World.GetComponent<MindRef>(creature).Handle, Emotion.Joy, PlayerInteractions.EmotionNudge, now);
        Creatures.Events.Publish(new PlayerInteracted(account, InteractionKind.Help, Creatures.World.GetComponent<SpeciesRef>(creature).Index, 1));
        Cooldowns.Start(account, Kind, now, CooldownTicks);
        return IntentResult.Applied();
    }
}

/// <summary>
/// A player shouts: every predator within <see cref="PlayerInteractions.ScareRadius"/> remembers <c>AttackedBy</c> and is
/// frightened, so it keeps its distance (AI Bible §6). Applied even when none is near (the shout happened). Detail = predators
/// scared (at most 255).
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Cooldowns">Per-player cooldowns.</param>
/// <param name="Avatar">The player's avatar.</param>
public record struct ScareTransaction(CreatureWorld Creatures, CooldownTracker Cooldowns, Entity Avatar) : ITransaction
{
    /// <summary>Cooldown kind id.</summary>
    public const ushort Kind = 5;

    /// <summary>One shout per 3 s per player.</summary>
    public const ulong CooldownTicks = 90;

    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        ArgumentNullException.ThrowIfNull(Cooldowns);
        if (!Creatures.World.Store<PlayerAvatar>().Has(Avatar))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        return Cooldowns.IsReady(Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId, Kind, now)
            ? IntentResult.Applied()
            : IntentResult.Reject(IntentRejectReason.OnCooldown);
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        ulong account = Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId;
        (int scared, ushort species) = Creatures.ScarePredators(Avatar, account, now);
        Creatures.Events.Publish(new PlayerInteracted(account, InteractionKind.Scare, species, scared));
        Cooldowns.Start(account, Kind, now, CooldownTicks);
        return IntentResult.Applied((byte)Math.Min(scared, byte.MaxValue));
    }
}
