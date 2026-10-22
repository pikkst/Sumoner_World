using System.Numerics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Combat;

/// <summary>
/// A player starts one of their moves aimed along a heading (combat spec §3, §4): no target needed, so it can hit anything in
/// the move's shape, or nothing. Slot 0 is the basic attack, slot 1 the heavy one (the player's move list in the combat data).
/// Rejected with <see cref="IntentRejectReason.InvalidArguments"/> for an unknown slot, and as <see cref="CombatRules.CanStart"/>
/// says otherwise.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player's avatar.</param>
/// <param name="Slot">Index into the player's move list.</param>
/// <param name="Heading">Aim in 1/256 turns, 0 = +Z, clockwise seen from above (the snapshot facing encoding).</param>
public record struct AttackTransaction(CreatureWorld Creatures, Entity Avatar, int Slot, byte Heading) : ITransaction
{
    /// <summary>The aim direction of a heading.</summary>
    public static Vector3 Direction(byte heading)
    {
        float angle = heading / 256f * 2f * MathF.PI;
        return new Vector3(MathF.Sin(angle), 0, MathF.Cos(angle));
    }

    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        if (!Creatures.World.Store<PlayerAvatar>().Has(Avatar) || Slot < 0 || Slot >= Creatures.Data.Combat.Player.Moves.Count)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        return Academy.ArenaRules.SafeZone(Creatures, Avatar) ?? CombatRules.CanStart(Creatures.World, Creatures.Data, Avatar, Move(Creatures, Slot));
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Vector3 aim = Direction(Heading);
        CombatRules.Start(Creatures.World, Creatures.Data, Avatar, Move(Creatures, Slot), aim, now);
        Creatures.World.GetComponent<Facing>(Avatar) = new Facing(aim);
        return IntentResult.Applied();
    }

    private static int Move(CreatureWorld creatures, int slot) => creatures.Data.MoveIndex(creatures.Data.Combat.Player.Moves[slot]);
}
