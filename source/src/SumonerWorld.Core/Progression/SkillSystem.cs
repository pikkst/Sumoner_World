using SumonerWorld.Core.Bonding;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Appraisal;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Progression;

/// <summary>Learning a skill (companions spec §5): shared by the player's choice and the creature's own.</summary>
public static class SkillLearning
{
    /// <summary>How long a creature shows reluctance after learning a skill from a branch it dislikes (1.5 s).</summary>
    public const ulong ReluctanceTicks = 45;

    /// <summary>Where node <paramref name="node"/> stands for this creature now.</summary>
    public static SkillState StateOf(CreatureWorld world, Entity creature, int node)
    {
        ArgumentNullException.ThrowIfNull(world);
        EcsWorld ecs = world.World;
        MindRef mind = ecs.GetComponent<MindRef>(creature);
        return SkillRules.StateOf(
            world.Data.SkillTreeOf(ecs.GetComponent<SpeciesRef>(creature).Index),
            node,
            ecs.GetComponent<CreatureGrowth>(creature),
            ecs.GetComponent<CreatureSkills>(creature),
            world.Services.Bonding.GetBond(mind.Handle).BondLevel,
            world.Minds.GetPersonality(mind.Handle));
    }

    /// <summary>Learns an available node: pays its cost, raises the stats, and shows reluctance if it disliked the branch.</summary>
    public static void Learn(CreatureWorld world, Entity creature, int node, ulong now)
    {
        ArgumentNullException.ThrowIfNull(world);
        EcsWorld ecs = world.World;
        ushort species = ecs.GetComponent<SpeciesRef>(creature).Index;
        SkillTreeDefinition tree = world.Data.SkillTreeOf(species)!;
        Personality personality = world.Minds.GetPersonality(ecs.GetComponent<MindRef>(creature).Handle);
        int affinity = SkillRules.Affinity(tree.Branches[SkillRules.BranchOf(tree, node)], personality, ecs.GetComponent<CreatureGrowth>(creature));
        ref CreatureSkills skills = ref ecs.GetComponent<CreatureSkills>(creature);
        skills.Learned |= 1UL << node;
        skills.Spent += SkillRules.Cost(affinity);
        skills.UnspentSince = 0;
        if (affinity <= SkillRules.ColdAt)
        {
            skills.ReluctantUntil = now + ReluctanceTicks;
        }

        ref Combatant combatant = ref ecs.GetComponent<Combatant>(creature);
        ProgressionComponents.Restat(world.Growth, world.Data, ecs, creature, world.Data.Species[species].Combat, ref combatant);
    }
}

/// <summary>
/// The owner teaches a pacted creature a skill of its species' tree (companions spec §5, M2-10). Validation: owner, and the node
/// is available (level, bond level, stage, earlier skills, points). A refusal carries the node's <see cref="SkillState"/> as detail.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The owner's avatar.</param>
/// <param name="Creature">The creature, by stable id.</param>
/// <param name="Node">The node index in its species' tree.</param>
public record struct LearnSkillTransaction(CreatureWorld Creatures, Entity Avatar, StableId Creature, int Node) : ITransaction
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

        SkillState state = SkillLearning.StateOf(Creatures, creature, Node);
        return state == SkillState.Available
            ? IntentResult.Applied()
            : new IntentResult(IntentStatus.Rejected, state == SkillState.Unknown ? IntentRejectReason.InvalidArguments : IntentRejectReason.WrongState, (byte)state);
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Creatures.TryFindCreature(Creature, out Entity creature);
        SkillLearning.Learn(Creatures, creature, Node, now);
        return IntentResult.Applied();
    }
}

/// <summary>
/// Skill upkeep (companions spec §5): a pacted creature with points its summoner has not spent for
/// <see cref="PickAfterDays"/> in-game days picks a skill itself, from the branch it likes most (the cheapest available node there,
/// else any branch by liking); a creature that learned a disliked skill shows reluctance (Grudge) for a moment, and one that started a learned combo by itself the first time shows pride (Affection, M2-12).
/// </summary>
public sealed class SkillSystem : ISimulationSystem
{
    /// <summary>In-game days of unspent points before a creature picks for itself.</summary>
    public const int PickAfterDays = 3;

    private const int CheckEveryTicks = 300;
    private const int MaxBranches = SkillTreeDefinition.MaxBranches;

    private readonly CreatureWorld _world;
    private readonly ComponentStore<CreatureSkills> _skills;
    private readonly ComponentStore<CreatureGrowth> _growth;
    private readonly ComponentStore<MindRef> _minds;
    private readonly ComponentStore<SpeciesRef> _species;
    private readonly ComponentStore<CurrentEmote> _emotes;
    private readonly ComponentStore<CreatureCombos> _combos;
    private readonly int[] _order = new int[MaxBranches];
    private readonly int[] _affinity = new int[MaxBranches];

    /// <summary>Creates the system for a world.</summary>
    public SkillSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _skills = world.World.Store<CreatureSkills>();
        _growth = world.World.Store<CreatureGrowth>();
        _minds = world.World.Store<MindRef>();
        _species = world.World.Store<SpeciesRef>();
        _emotes = world.World.Store<CurrentEmote>();
        _combos = world.World.Store<CreatureCombos>();
    }

    /// <summary>Skills creatures picked for themselves since the start.</summary>
    public int PickedItself { get; private set; }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Behaviour;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        ReadOnlySpan<Entity> creatures = _skills.Entities;
        bool check = tick.Index % CheckEveryTicks == 0;
        for (int i = creatures.Length - 1; i >= 0; i--)
        {
            Entity creature = creatures[i];
            if (tick.Index < _skills.Components[i].ReluctantUntil)
            {
                _emotes.Get(creature) = new CurrentEmote(Emote.Grudge);
            }
            else if (_combos.Has(creature) && tick.Index < _combos.Get(creature).ProudUntil)
            {
                _emotes.Get(creature) = new CurrentEmote(Emote.Affection); // it learned its summoner's style (M2-12)
            }

            if (check)
            {
                Upkeep(creature, tick.Index);
            }
        }
    }

    private void Upkeep(Entity creature, ulong now)
    {
        BondState bond = _world.Services.Bonding.GetBond(_minds.Get(creature).Handle);
        ref CreatureSkills skills = ref _skills.Get(creature);
        SkillTreeDefinition? tree = _world.Data.SkillTreeOf(_species.Get(creature).Index);
        if (bond.State != OwnershipState.Pacted || tree is null || SkillRules.Points(_growth.Get(creature), skills) == 0)
        {
            skills.UnspentSince = 0;
            return;
        }

        if (skills.UnspentSince == 0)
        {
            skills.UnspentSince = now;
            return;
        }

        if (now - skills.UnspentSince < (ulong)(PickAfterDays * SimulationSettings.DefaultTicksPerDay))
        {
            return;
        }

        int node = Choose(creature, tree);
        if (node >= 0)
        {
            SkillLearning.Learn(_world, creature, node, now);
            _skills.Get(creature).PickedItself++;
            PickedItself++;
        }

        // Still points left (or nothing to learn): wait another three days.
        _skills.Get(creature).UnspentSince = SkillRules.Points(_growth.Get(creature), _skills.Get(creature)) > 0 ? now : 0;
    }

    // Branches by how much it likes them (ties by order), then the first available node in the best branch that has one.
    private int Choose(Entity creature, SkillTreeDefinition tree)
    {
        Personality personality = _world.Minds.GetPersonality(_minds.Get(creature).Handle);
        int branches = Math.Min(tree.Branches.Count, MaxBranches);
        for (int b = 0; b < branches; b++)
        {
            _affinity[b] = SkillRules.Affinity(tree.Branches[b], personality, _growth.Get(creature));
            _order[b] = b;
        }

        for (int a = 1; a < branches; a++)
        {
            for (int b = a; b > 0 && _affinity[_order[b]] > _affinity[_order[b - 1]]; b--)
            {
                (_order[b], _order[b - 1]) = (_order[b - 1], _order[b]);
            }
        }

        for (int b = 0; b < branches; b++)
        {
            string branch = tree.Branches[_order[b]].Id;
            for (int node = 0; node < tree.Nodes.Count; node++)
            {
                if (tree.Nodes[node].Branch == branch && SkillLearning.StateOf(_world, creature, node) == SkillState.Available)
                {
                    return node;
                }
            }
        }

        return -1;
    }
}
