using System.Numerics;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Bonding;

/// <summary>A player drawing a summoning sigil (M2-06); the creature arrives when it is done.</summary>
/// <param name="Since">Tick the cast began.</param>
/// <param name="Creature">The creature called.</param>
public record struct SigilCast(ulong Since, StableId Creature);

/// <summary>Why a summon was refused, in <see cref="IntentResult.Detail"/> of a <see cref="IntentRejectReason.WrongState"/> rejection.</summary>
public enum SummonRefusal : byte
{
    /// <summary>No detail.</summary>
    None = 0,

    /// <summary>Not enough mana.</summary>
    NoMana = 1,

    /// <summary>Bond Capacity reached: dismiss another companion first.</summary>
    CapacityReached = 2,

    /// <summary>The player is busy (in a move, casting, down, staggered or dodging).</summary>
    Busy = 3,

    /// <summary>It is already at the player's side.</summary>
    AlreadySummoned = 4,

    /// <summary>It is not pacted to this player (wild, at the Sanctum, fallen or someone else's).</summary>
    NotPacted = 5,
}

/// <summary>One of a player's pacted creatures, as the companion list shows it.</summary>
/// <param name="Id">The creature's stable id.</param>
/// <param name="Species">Species index.</param>
/// <param name="Summoned">At the player's side now.</param>
/// <param name="Level">Its level.</param>
/// <param name="Stage">Its life stage.</param>
public readonly record struct Companion(StableId Id, ushort Species, bool Summoned, int Level = 1, Progression.LifeStage Stage = Progression.LifeStage.Young);

/// <summary>Queries over pacted creatures (AI Bible §10.4).</summary>
public static class Companions
{
    /// <summary>Fills <paramref name="into"/> with the account's pacted creatures, in stable-id order.</summary>
    public static void Of(CreatureWorld world, ulong account, List<Companion> into)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        ComponentStore<MindRef> minds = world.World.Store<MindRef>();
        ReadOnlySpan<Entity> creatures = minds.Entities;
        for (int i = 0; i < creatures.Length; i++)
        {
            BondState bond = world.Services.Bonding.GetBond(minds.Components[i].Handle);
            if (bond.State == OwnershipState.Pacted && bond.Owner == account)
            {
                Progression.CreatureGrowth growth = world.World.GetComponent<Progression.CreatureGrowth>(creatures[i]);
                into.Add(new Companion(
                    world.World.GetComponent<StableId>(creatures[i]), world.World.GetComponent<SpeciesRef>(creatures[i]).Index, bond.Summoned, growth.Level, (Progression.LifeStage)growth.Stage));
            }
        }

        into.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
    }

    /// <summary>How many of the account's creatures are summoned now.</summary>
    public static int Summoned(CreatureWorld world, ulong account)
    {
        ArgumentNullException.ThrowIfNull(world);
        ComponentStore<MindRef> minds = world.World.Store<MindRef>();
        int count = 0;
        foreach (MindRef mind in minds.Components)
        {
            BondState bond = world.Services.Bonding.GetBond(mind.Handle);
            count += bond.Summoned && bond.Owner == account ? 1 : 0;
        }

        return count;
    }
}

/// <summary>
/// A player calls a pacted creature (AI Bible §10.4, GDD §1, M2-06): validated, then atomic (R27, R34). Validation: the avatar can
/// act (not in a move, casting, down, staggered or dodging), the creature exists and is pacted to the player, not summoned yet, the
/// player has <see cref="CombatDefinition.SummonManaCost"/> mana and fewer active summons than <see cref="CombatDefinition.BondCapacity"/>.
/// The commit pays the mana, marks the creature summoned (Bonding Contract) and starts the <see cref="SigilCast"/>; the creature
/// arrives at the player's side when the cast ends (<see cref="SummoningSystem"/>). There is no range: a summon calls the creature
/// from wherever it is.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The summoning player's avatar.</param>
/// <param name="Creature">The creature, by stable id.</param>
public record struct SummonTransaction(CreatureWorld Creatures, Entity Avatar, StableId Creature) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        EcsWorld world = Creatures.World;
        if (!world.Store<PlayerAvatar>().Has(Avatar) || !Creatures.TryFindCreature(Creature, out Entity creature))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        ulong account = world.GetComponent<PlayerAvatar>(Avatar).AccountId;
        BondState bond = Creatures.Services.Bonding.GetBond(world.GetComponent<MindRef>(creature).Handle);
        CombatDefinition rules = Creatures.Data.Combat;
        SummonRefusal refusal = bond.State != OwnershipState.Pacted || bond.Owner != account ? SummonRefusal.NotPacted
            : bond.Summoned ? SummonRefusal.AlreadySummoned
            : Busy(world, Avatar) ? SummonRefusal.Busy
            : world.GetComponent<Combatant>(Avatar).Mana < rules.SummonManaCost ? SummonRefusal.NoMana
            : Companions.Summoned(Creatures, account) >= rules.BondCapacity ? SummonRefusal.CapacityReached
            : SummonRefusal.None;
        return refusal switch
        {
            SummonRefusal.None => IntentResult.Applied(),
            SummonRefusal.NotPacted => new IntentResult(IntentStatus.Rejected, IntentRejectReason.NotOwner, (byte)refusal),
            _ => new IntentResult(IntentStatus.Rejected, IntentRejectReason.WrongState, (byte)refusal),
        };
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        EcsWorld world = Creatures.World;
        Creatures.TryFindCreature(Creature, out Entity creature);
        ulong account = world.GetComponent<PlayerAvatar>(Avatar).AccountId;
        Creatures.Services.Bonding.Summon(
            world.GetComponent<MindRef>(creature).Handle, new MemorySubject(SubjectKind.Player, account), 0, BondingRules.MaxBondCapacity);
        ref Combatant caster = ref world.GetComponent<Combatant>(Avatar);
        caster.Mana -= Creatures.Data.Combat.SummonManaCost;
        world.Remove<Blocking>(Avatar);
        world.Add(Avatar, new SigilCast(now, Creature));
        return IntentResult.Applied();
    }

    /// <summary>True while the player cannot start a summon (or a move).</summary>
    public static bool Busy(EcsWorld world, Entity avatar)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Has<ActiveMove>(avatar) || world.Has<SigilCast>(avatar) || world.Has<Downed>(avatar) || world.Has<Staggered>(avatar)
            || world.Has<Dodging>(avatar);
    }
}

/// <summary>
/// A player sends a summoned creature back (AI Bible §10.4, M2-06): it stops being summoned and goes back to its own life where it
/// stands (the Sanctum, where it would go home to, comes later). Owner only.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player's avatar.</param>
/// <param name="Creature">The creature, by stable id.</param>
public record struct DismissTransaction(CreatureWorld Creatures, Entity Avatar, StableId Creature) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        EcsWorld world = Creatures.World;
        if (!world.Store<PlayerAvatar>().Has(Avatar) || !Creatures.TryFindCreature(Creature, out Entity creature))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        BondState bond = Creatures.Services.Bonding.GetBond(world.GetComponent<MindRef>(creature).Handle);
        if (bond.State != OwnershipState.Pacted || bond.Owner != world.GetComponent<PlayerAvatar>(Avatar).AccountId)
        {
            return IntentResult.Reject(IntentRejectReason.NotOwner);
        }

        return bond.Summoned ? IntentResult.Applied() : IntentResult.Reject(IntentRejectReason.WrongState);
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        EcsWorld world = Creatures.World;
        Creatures.TryFindCreature(Creature, out Entity creature);
        ulong account = world.GetComponent<PlayerAvatar>(Avatar).AccountId;
        Creatures.Services.Bonding.Dismiss(world.GetComponent<MindRef>(creature).Handle, new MemorySubject(SubjectKind.Player, account));
        if (world.Has<SigilCast>(Avatar) && world.GetComponent<SigilCast>(Avatar).Creature == Creature)
        {
            world.Remove<SigilCast>(Avatar);
        }

        return IntentResult.Applied();
    }
}

/// <summary>
/// Finishes summoning sigils (M2-06): <see cref="CombatDefinition.SummonCastTicks"/> after the cast began, the creature appears
/// <see cref="ArrivalMetres"/> in front of the player and to its right, facing the way the player faces, showing Trust. A cast whose
/// creature is gone, or was dismissed meanwhile, ends without an arrival.
/// </summary>
public sealed class SummoningSystem : ISimulationSystem
{
    /// <summary>How far from the player the creature appears (between the bodies' edges).</summary>
    public const float ArrivalMetres = 1.5f;

    private readonly CreatureWorld _world;
    private readonly ComponentStore<SigilCast> _casts;

    /// <summary>Creates the system for a world.</summary>
    public SummoningSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _casts = world.World.Store<SigilCast>();
    }

    /// <summary>Creatures that arrived since the start.</summary>
    public int Arrivals { get; private set; }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Movement;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> casters = _casts.Entities;
        for (int i = casters.Length - 1; i >= 0; i--)
        {
            Entity caster = casters[i];
            SigilCast cast = _casts.Get(caster);
            if (tick.Index - cast.Since < (ulong)_world.Data.Combat.SummonCastTicks)
            {
                continue;
            }

            _world.World.Remove<SigilCast>(caster);
            if (_world.TryFindCreature(cast.Creature, out Entity creature)
                && _world.Services.Bonding.GetBond(_world.World.GetComponent<MindRef>(creature).Handle).Summoned)
            {
                PlaceBeside(_world, caster, creature);
                _world.World.GetComponent<CurrentEmote>(creature) = new CurrentEmote(Emote.Trust);
                Arrivals++;
            }
        }
    }

    /// <summary>Puts <paramref name="creature"/> in front of <paramref name="player"/> and to their right, facing their way, standing still.</summary>
    public static void PlaceBeside(CreatureWorld creatures, Entity player, Entity creature)
    {
        ArgumentNullException.ThrowIfNull(creatures);
        EcsWorld world = creatures.World;
        Vector3 from = world.GetComponent<Position>(player).Value;
        Vector3 facing = world.GetComponent<Facing>(player).Value;
        var ahead = new Vector3(facing.X, 0, facing.Z);
        ahead = ahead.LengthSquared() > 0 ? Vector3.Normalize(ahead) : Vector3.UnitZ;
        var right = new Vector3(-ahead.Z, 0, ahead.X);
        float distance = creatures.Sizes.RadiusOf(player) + creatures.Sizes.RadiusOf(creature) + ArrivalMetres;
        Vector3 at = Vector3.Clamp(from + (Vector3.Normalize(ahead + right) * distance), creatures.Settings.BoundsMin, creatures.Settings.BoundsMax);
        at.Y = creatures.Settings.Ground?.HeightAt(at.X, at.Z) ?? from.Y;

        world.GetComponent<Position>(creature) = new Position(at);
        world.GetComponent<Facing>(creature) = new Facing(ahead);
        world.GetComponent<MoveTarget>(creature) = world.GetComponent<MoveTarget>(creature) with { Active = 0 };
        creatures.Grid.Move(creature, at);
    }
}
