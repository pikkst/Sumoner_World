using System.Numerics;
using SumonerWorld.Core.Combat;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Items;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Quests;

/// <summary>A player takes a quest offered in the quest log (quests spec §3). Atomic and idempotent.</summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player.</param>
/// <param name="Quest">Quest index.</param>
public record struct AcceptQuestTransaction(CreatureWorld Creatures, Entity Avatar, int Quest) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        if (!Creatures.World.Store<PlayerAvatar>().Has(Avatar))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        ulong account = Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId;
        int level = Creatures.World.GetComponent<PlayerProgress>(Avatar).Level;
        QuestRefusal refusal = QuestBook.CanAccept(Creatures.Data, Creatures.Economy.Quests.Of(account), Quest, level);
        return refusal == QuestRefusal.None
            ? IntentResult.Applied()
            : new IntentResult(IntentStatus.Rejected, refusal == QuestRefusal.Unknown ? IntentRejectReason.InvalidArguments : IntentRejectReason.WrongState, (byte)refusal);
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Creatures.Economy.Quests.Of(Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId).Take(Quest);
        return IntentResult.Applied();
    }
}

/// <summary>A player leaves a quest they took; its progress is lost and it is offered again (quests spec §3).</summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player.</param>
/// <param name="Quest">Quest index.</param>
public record struct AbandonQuestTransaction(CreatureWorld Creatures, Entity Avatar, int Quest) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        if (!Creatures.World.Store<PlayerAvatar>().Has(Avatar))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        return Creatures.Economy.Quests.Of(Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId).Find(Quest) is null
            ? IntentResult.Reject(IntentRejectReason.WrongState)
            : IntentResult.Applied();
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        Creatures.Economy.Quests.Of(Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId).Drop(Quest);
        return IntentResult.Applied();
    }
}

/// <summary>
/// Counts quest objectives from what happened this tick (quests spec §6): defeats (every fighter), gathers, crafts and Pacts
/// (<see cref="QuestEvent"/>), feeds and helps; places twice a second. Ready quests give their rewards (experience, gold,
/// items) as soon as the items fit. Runs after crafting and loot.
/// </summary>
public sealed class QuestSystem : ISimulationSystem
{
    private const ulong ReachEveryTicks = 15;

    private readonly CreatureWorld _world;
    private readonly ComponentStore<PlayerAvatar> _players;
    private readonly ComponentStore<Position> _positions;

    /// <summary>Creates the system for a world.</summary>
    public QuestSystem(CreatureWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _players = world.World.Store<PlayerAvatar>();
        _positions = world.World.Store<Position>();
    }

    /// <summary>Quests done since the start.</summary>
    public int Completed { get; private set; }

    /// <inheritdoc />
    public SimulationStage Stage => SimulationStage.Transactions;

    /// <inheritdoc />
    public void Execute(in SimulationTick tick)
    {
        QuestBook book = _world.Economy.Quests;
        foreach (QuestEvent happened in _world.Events.Read<QuestEvent>())
        {
            string id = happened.Type is ObjectiveType.Gather or ObjectiveType.Craft
                ? _world.Data.Items[happened.Key].Id
                : _world.Data.Species[happened.Key].Id;
            book.Record(_world.Data, happened.Account, happened.Type, id, happened.Count);
        }

        foreach (PlayerInteracted action in _world.Events.Read<PlayerInteracted>())
        {
            if (action.Kind is InteractionKind.Feed or InteractionKind.Help)
            {
                ObjectiveType type = action.Kind == InteractionKind.Feed ? ObjectiveType.Feed : ObjectiveType.Help;
                book.Record(_world.Data, action.Account, type, _world.Data.Species[action.Species].Id, 1);
            }
        }

        foreach (CombatantDefeated defeat in _world.Events.Read<CombatantDefeated>())
        {
            for (int i = 0; i < 4; i++)
            {
                if (defeat.Fighters[i] != 0)
                {
                    book.Record(_world.Data, defeat.Fighters[i], ObjectiveType.Defeat, _world.Data.Species[defeat.Species].Id, 1);
                }
            }
        }

        if (tick.Index % ReachEveryTicks == 0)
        {
            Players(book);
        }
    }

    // Places, and rewards for ready quests, for each player in the world.
    private void Players(QuestBook book)
    {
        ReadOnlySpan<Entity> avatars = _players.Entities;
        for (int p = 0; p < avatars.Length; p++)
        {
            ulong account = _players.Components[p].AccountId;
            if (book.Find(account) is not PlayerQuests quests)
            {
                continue;
            }

            Vector3 at = _positions.Get(avatars[p]).Value;
            IReadOnlyList<QuestProgress> active = quests.Active;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                QuestProgress progress = active[i];
                if (progress.Status == QuestStatus.Active)
                {
                    Reach(progress, at);
                }

                if (progress.Status == QuestStatus.Ready && Reward(avatars[p], account, progress.Quest))
                {
                    quests.Finish(progress.Quest);
                    Completed++;
                }
            }
        }
    }

    private void Reach(QuestProgress progress, Vector3 at)
    {
        IReadOnlyList<QuestObjectiveDefinition> objectives = _world.Data.Quests[progress.Quest].Steps[progress.Step].Objectives;
        for (int o = 0; o < objectives.Count; o++)
        {
            QuestObjectiveDefinition objective = objectives[o];
            if (objective.Type == ObjectiveType.Reach && progress.Counts[o] == 0
                && new Vector2(at.X - objective.X, at.Z - objective.Z).LengthSquared() <= objective.Radius * objective.Radius)
            {
                progress.Counts[o] = 1;
                QuestBook.Advance(_world.Data, progress);
                return;
            }
        }
    }

    // Gives the rewards if the items fit; false (nothing given) when they do not.
    private bool Reward(Entity avatar, ulong account, int quest)
    {
        QuestRewardsDefinition rewards = _world.Data.Quests[quest].Rewards;
        var items = new List<ItemCount>();
        foreach (RecipePart part in rewards.Items)
        {
            items.Add(new ItemCount(_world.Data.ItemIndex(part.Item), part.Count));
        }

        Inventory bag = _world.Economy.Inventories.Of(account);
        if (!bag.CanAdd(_world.Data, items))
        {
            return false;
        }

        bag.Add(_world.Data, items);
        bag.AddGold(rewards.Gold);
        if (rewards.Xp > 0)
        {
            PlayerStats.Earn(_world, avatar, rewards.Xp);
        }

        return true;
    }
}
