using System.Numerics;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Bonding;

/// <summary>Commands v1 a summoner gives their companions (companions spec §8, M2-08). Values are part of the protocol.</summary>
public enum CompanionCommand : byte
{
    /// <summary>No command.</summary>
    None = 0,

    /// <summary>Fight that target.</summary>
    Attack = 1,

    /// <summary>Stay close and attack whoever attacks the summoner.</summary>
    GuardMe = 2,

    /// <summary>Stay at this place and defend it.</summary>
    Hold = 3,

    /// <summary>Leave the fight and come back to the summoner.</summary>
    Retreat = 4,
}

/// <summary>
/// A companion's last command and how it answered (M2-08). <c>Active</c> is 1 while the command is being carried out. Laid out
/// without padding (hashed as raw bytes).
/// </summary>
/// <param name="Target">The creature to attack (Attack).</param>
/// <param name="Since">Tick the command was given.</param>
/// <param name="HoldAt">Where to stay (Hold).</param>
/// <param name="Command">A <see cref="CompanionCommand"/>.</param>
/// <param name="Response">A <see cref="CommandResponse"/>.</param>
/// <param name="Obedience">The obedience score.</param>
/// <param name="Reason">An <see cref="ObedienceReason"/>.</param>
/// <param name="Active">1 while being carried out.</param>
public record struct CompanionOrder(Entity Target, ulong Since, Vector3 HoldAt, int Command, int Response, int Obedience, int Reason, int Active);

/// <summary>
/// A summoner commands every companion summoned at their side (companions spec §8, AI Bible §7.4, M2-08). Validation: the player
/// has a summoned companion; an Attack names a creature within <see cref="AttackRange"/> that is not one of the player's own.
/// The commit asks each companion: the <see cref="ObedienceModel"/> answers from its bond level, Trust, Respect, Loyalty, Fear,
/// the danger of the command (Attack: the foe's strength against its own) and whether it goes against its memories (Attack on
/// its own species). Its answer becomes its <see cref="CompanionOrder"/>, which the <see cref="CompanionSystem"/> carries out and
/// shows. <see cref="IntentResult.Detail"/> is how many companions do it (obey, improvise or hesitate) in the low four bits and how
/// many refuse in the high four.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The summoner's avatar.</param>
/// <param name="Command">What they command.</param>
/// <param name="Target">Attack: the target's stable id.</param>
public record struct CommandTransaction(CreatureWorld Creatures, Entity Avatar, CompanionCommand Command, StableId Target) : ITransaction
{
    /// <summary>Farthest an attack target may be from the summoner, in metres.</summary>
    public const float AttackRange = 30f;

    private const int GuardDanger = 100;
    private const int HoldDanger = 150;
    private const int EvenFightDanger = 500;
    private const int MaxDanger = 1000;
    private const int Nibble = 4;
    private const int MaxCount = 15;

    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        EcsWorld world = Creatures.World;
        if (!world.Store<PlayerAvatar>().Has(Avatar))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if (Command is < CompanionCommand.Attack or > CompanionCommand.Retreat)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        ulong account = world.GetComponent<PlayerAvatar>(Avatar).AccountId;
        if (Companions.Summoned(Creatures, account) == 0)
        {
            return IntentResult.Reject(IntentRejectReason.WrongState);
        }

        if (Command != CompanionCommand.Attack)
        {
            return IntentResult.Applied();
        }

        if (!Creatures.TryFindCreature(Target, out Entity target) || IsOwn(target, account))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        return Vector3.Distance(world.GetComponent<Position>(Avatar).Value, world.GetComponent<Position>(target).Value) > AttackRange
            ? IntentResult.Reject(IntentRejectReason.OutOfRange)
            : IntentResult.Applied();
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        EcsWorld world = Creatures.World;
        ulong account = world.GetComponent<PlayerAvatar>(Avatar).AccountId;
        Entity target = Command == CompanionCommand.Attack && Creatures.TryFindCreature(Target, out Entity found) ? found : default;
        var obedience = new ObedienceModel(Creatures.Minds, Creatures.Opinions, ObedienceSettings.Default);
        var owner = new MemorySubject(SubjectKind.Player, account);
        int doing = 0;
        int refusing = 0;
        ComponentStore<MindRef> minds = world.Store<MindRef>();
        ReadOnlySpan<Entity> creatures = minds.Entities;
        for (int i = 0; i < creatures.Length; i++)
        {
            Entity companion = creatures[i];
            BondState bond = Creatures.Services.Bonding.GetBond(minds.Components[i].Handle);
            if (!bond.Summoned || bond.Owner != account)
            {
                continue;
            }

            ObedienceResult answer = obedience.Evaluate(
                minds.Components[i].Handle, owner, bond.BondLevel, Danger(companion, target), ConflictsWithMemory(companion, target), knowsBetterTactic: false, now);
            var order = new CompanionOrder(
                target, now, world.GetComponent<Position>(companion).Value, (int)Command, (int)answer.Response, answer.Obedience, (int)answer.Reason, 1);
            if (world.Has<CompanionOrder>(companion))
            {
                world.GetComponent<CompanionOrder>(companion) = order;
            }
            else
            {
                world.Add(companion, order);
            }

            doing += answer.Response == CommandResponse.Refuse ? 0 : 1;
            refusing += answer.Response == CommandResponse.Refuse ? 1 : 0;
        }

        return IntentResult.Applied((byte)(Math.Min(doing, MaxCount) | (Math.Min(refusing, MaxCount) << Nibble)));
    }

    private readonly bool IsOwn(Entity creature, ulong account)
    {
        BondState bond = Creatures.Services.Bonding.GetBond(Creatures.World.GetComponent<MindRef>(creature).Handle);
        return bond.State == OwnershipState.Pacted && bond.Owner == account;
    }

    // 0–1000: an attack on a foe as strong as itself is 500; guarding and holding are a little risky; coming back is safe.
    private readonly int Danger(Entity companion, Entity target)
    {
        switch (Command)
        {
            case CompanionCommand.GuardMe:
                return GuardDanger;
            case CompanionCommand.Hold:
                return HoldDanger;
            case CompanionCommand.Attack:
                Combatant me = Creatures.World.GetComponent<Combatant>(companion);
                Combatant foe = Creatures.World.GetComponent<Combatant>(target);
                long mine = Math.Max(1L, (long)me.Health * me.Power);
                long theirs = (long)foe.Health * foe.Power;
                return (int)Math.Clamp(theirs * EvenFightDanger / mine, 0, MaxDanger);
            default:
                return 0;
        }
    }

    // Fighting its own species goes against what it is.
    private readonly bool ConflictsWithMemory(Entity companion, Entity target) =>
        Command == CompanionCommand.Attack
        && Creatures.World.GetComponent<SpeciesRef>(companion).Index == Creatures.World.GetComponent<SpeciesRef>(target).Index;
}
