using System.Numerics;
using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Combat;

/// <summary>
/// A player casts a spell (combat spec §4, §8, M2-11): validated like any move (<see cref="CombatRules.CanStart"/>: not busy,
/// enough mana), then started as the spell's move. A bolt flies along <paramref name="Heading"/>; a burst lands
/// <paramref name="Distance"/> metres ahead (at most its range); a ward goes on <paramref name="Target"/> when that is one of the
/// player's own creatures, otherwise on the player. <b>Resonance</b>: with a summoned companion whose class shares the spell's
/// school within <see cref="ResonanceMetres"/>, the spell costs <see cref="ResonanceDiscountPercent"/>% less.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The caster.</param>
/// <param name="Spell">Index into the spells data.</param>
/// <param name="Heading">Aim in 1/256 turns (0 = +Z).</param>
/// <param name="Distance">Burst: metres ahead.</param>
/// <param name="Target">Ward: the creature to cast it on (0 = self).</param>
public record struct CastTransaction(CreatureWorld Creatures, Entity Avatar, int Spell, byte Heading, float Distance, StableId Target) : ITransaction
{
    /// <summary>How close a resonating companion must be.</summary>
    public const float ResonanceMetres = 20f;

    /// <summary>How much resonance takes off the cost.</summary>
    public const int ResonanceDiscountPercent = 10;

    private const float StepsPerTurn = 256f;
    private const int Percent = 100;

    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        if (!Creatures.World.Store<PlayerAvatar>().Has(Avatar))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if ((uint)Spell >= (uint)Creatures.Data.Spells.Count || float.IsNaN(Distance) || Distance < 0)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        int move = Creatures.Data.MoveIndex(Creatures.Data.Spells[Spell].Id);
        return Academy.ArenaRules.SafeZone(Creatures, Avatar) ?? CombatRules.CanStart(Creatures.World, Creatures.Data, Avatar, move, Cost(move));
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        int move = Creatures.Data.MoveIndex(Creatures.Data.Spells[Spell].Id);
        float angle = Heading / StepsPerTurn * MathF.Tau;
        var aim = new Vector3(MathF.Sin(angle), 0, MathF.Cos(angle));
        Entity ally = Creatures.TryFindCreature(Target, out Entity creature) && Owns(creature) ? creature : default;
        CombatRules.Start(Creatures.World, Creatures.Data, Avatar, move, aim, now, Distance, ally, Cost(move));
        Creatures.World.GetComponent<Facing>(Avatar) = new Facing(aim);
        return IntentResult.Applied();
    }

    // The mana the spell costs this player now (resonance, combat spec §8).
    private readonly int Cost(int move)
    {
        MoveDefinition spell = Creatures.Data.Moves[move];
        return Resonates(spell.School) ? spell.Mana * (Percent - ResonanceDiscountPercent) / Percent : spell.Mana;
    }

    private readonly bool Resonates(string school)
    {
        EcsWorld world = Creatures.World;
        ulong account = world.GetComponent<PlayerAvatar>(Avatar).AccountId;
        Vector3 at = world.GetComponent<Position>(Avatar).Value;
        ComponentStore<MindRef> minds = world.Store<MindRef>();
        ReadOnlySpan<Entity> creatures = minds.Entities;
        for (int i = 0; i < creatures.Length; i++)
        {
            BondState bond = Creatures.Services.Bonding.GetBond(minds.Components[i].Handle);
            if (bond.Summoned && bond.Owner == account
                && SchoolOf(Creatures.Data.Species[world.GetComponent<SpeciesRef>(creatures[i]).Index].Class) == school
                && Vector3.Distance(world.GetComponent<Position>(creatures[i]).Value, at) <= ResonanceMetres)
            {
                return true;
            }
        }

        return false;
    }

    private readonly bool Owns(Entity creature)
    {
        BondState bond = Creatures.Services.Bonding.GetBond(Creatures.World.GetComponent<MindRef>(creature).Handle);
        return bond.State == OwnershipState.Pacted && bond.Owner == Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId;
    }

    // The school each class's creatures resonate with (GDD §11: Elves are Primal; the others arrive with their schools).
    private static string SchoolOf(Progression.SummonerClass summonerClass) => summonerClass switch
    {
        Progression.SummonerClass.Elf => "primal",
        Progression.SummonerClass.Necromancer => "umbral",
        Progression.SummonerClass.Dwarf => "ember",
        _ => "storm",
    };
}
