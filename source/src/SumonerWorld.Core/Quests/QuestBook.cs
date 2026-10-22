using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;

namespace SumonerWorld.Core.Quests;

/// <summary>Where a quest stands for a player (quests spec §1).</summary>
public enum QuestStatus
{
    /// <summary>Not taken (or abandoned).</summary>
    None = 0,

    /// <summary>Being done.</summary>
    Active = 1,

    /// <summary>Every step done; waiting for room in the bag for the reward items.</summary>
    Ready = 2,

    /// <summary>Done, rewards given.</summary>
    Done = 3,
}

/// <summary>Something that may count for an objective, published by the code where it happened (quests spec §6).</summary>
/// <param name="Account">The player.</param>
/// <param name="Type">What happened.</param>
/// <param name="Key">The species or item index.</param>
/// <param name="Count">How many.</param>
public readonly record struct QuestEvent(ulong Account, ObjectiveType Type, int Key, int Count);

/// <summary>One quest a player has taken: its step and the counts of the step's objectives.</summary>
public sealed class QuestProgress(int quest)
{
    /// <summary>Quest index.</summary>
    public int Quest { get; } = quest;

    /// <summary>The current step.</summary>
    public int Step { get; internal set; }

    /// <summary>Active or ready.</summary>
    public QuestStatus Status { get; internal set; } = QuestStatus.Active;

    /// <summary>Counts of the current step's objectives.</summary>
    public int[] Counts { get; } = new int[QuestStepDefinition.MaxObjectives];
}

/// <summary>A player's quests: the ones taken (in quest order) and the ones done.</summary>
public sealed class PlayerQuests
{
    /// <summary>Most active quests at once (quests spec §3).</summary>
    public const int MaxActive = 10;

    private readonly List<QuestProgress> _active = [];
    private readonly HashSet<int> _done = [];

    /// <summary>Taken quests, in quest order.</summary>
    public IReadOnlyList<QuestProgress> Active => _active;

    /// <summary>Done quests.</summary>
    public IReadOnlyCollection<int> Done => _done;

    /// <summary>The status of a quest.</summary>
    public QuestStatus StatusOf(int quest) =>
        _done.Contains(quest) ? QuestStatus.Done : Find(quest)?.Status ?? QuestStatus.None;

    /// <summary>The progress of a taken quest, or null.</summary>
    public QuestProgress? Find(int quest)
    {
        for (int i = 0; i < _active.Count; i++)
        {
            if (_active[i].Quest == quest)
            {
                return _active[i];
            }
        }

        return null;
    }

    internal QuestProgress Take(int quest)
    {
        var progress = new QuestProgress(quest);
        int at = 0;
        while (at < _active.Count && _active[at].Quest < quest)
        {
            at++;
        }

        _active.Insert(at, progress);
        return progress;
    }

    internal void Drop(int quest) => _active.RemoveAll(progress => progress.Quest == quest);

    internal void Finish(int quest)
    {
        Drop(quest);
        _done.Add(quest);
    }

    internal void AddTo(ref StateHash hash)
    {
        foreach (QuestProgress progress in _active)
        {
            hash.Add((ulong)progress.Quest);
            hash.Add((ulong)progress.Step);
            hash.Add((ulong)progress.Status);
            foreach (int count in progress.Counts)
            {
                hash.Add((ulong)count);
            }
        }

        foreach (int quest in _done.Order())
        {
            hash.Add((ulong)quest);
        }
    }
}

/// <summary>A saved quest, by id (quests spec §6).</summary>
/// <param name="Quest">Quest id.</param>
/// <param name="Step">Step.</param>
/// <param name="Counts">Counts of the step's objectives.</param>
/// <param name="Done">True when done.</param>
public sealed record QuestSave(string Quest, int Step, IReadOnlyList<int> Counts, bool Done)
{
    /// <summary>Equal when every field is, the counts compared one by one.</summary>
    public bool Equals(QuestSave? other) =>
        other is not null && Quest == other.Quest && Step == other.Step && Done == other.Done && Counts.SequenceEqual(other.Counts);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Quest, Step, Done);
}

/// <summary>Every player's quests, by account (quests spec, M2-19). Server state; it changes only in transactions and the <see cref="QuestSystem"/>.</summary>
public sealed class QuestBook
{
    private readonly Dictionary<ulong, PlayerQuests> _byAccount = [];

    /// <summary>The account's quests (created empty on first use).</summary>
    public PlayerQuests Of(ulong account)
    {
        if (!_byAccount.TryGetValue(account, out PlayerQuests? quests))
        {
            quests = new PlayerQuests();
            _byAccount.Add(account, quests);
        }

        return quests;
    }

    /// <summary>True when <paramref name="quest"/> is for every class or for <paramref name="summonerClass"/> (E6).</summary>
    public static bool IsFor(QuestDefinition quest, int summonerClass)
    {
        ArgumentNullException.ThrowIfNull(quest);
        return quest.Class.Length == 0
            || ((uint)summonerClass < (uint)AcademyDefinition.ClassIds.Count && AcademyDefinition.ClassIds[summonerClass] == quest.Class);
    }

    /// <summary>The account's quests if it has any, without creating them.</summary>
    public PlayerQuests? Find(ulong account) => _byAccount.GetValueOrDefault(account);

    /// <summary>
    /// Why a quest cannot be taken now (<see cref="QuestRefusal.None"/> when it can); <paramref name="summonerClass"/> is the
    /// player's class (a class's prologue is only for it, E6).
    /// </summary>
    public static QuestRefusal CanAccept(IDataRegistry data, PlayerQuests quests, int quest, int level, int summonerClass)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(quests);
        if ((uint)quest >= (uint)data.Quests.Count)
        {
            return QuestRefusal.Unknown;
        }

        QuestDefinition definition = data.Quests[quest];
        if (quests.StatusOf(quest) != QuestStatus.None)
        {
            return QuestRefusal.AlreadyTaken;
        }

        if (level < definition.MinLevel)
        {
            return QuestRefusal.LevelTooLow;
        }

        if (!IsFor(definition, summonerClass))
        {
            return QuestRefusal.OtherClass;
        }

        foreach (string required in definition.Requires)
        {
            if (quests.StatusOf(IndexOf(data, required)) != QuestStatus.Done)
            {
                return QuestRefusal.NeedsEarlierQuest;
            }
        }

        return quests.Active.Count >= PlayerQuests.MaxActive ? QuestRefusal.TooMany : QuestRefusal.None;
    }

    /// <summary>
    /// Counts something that happened for every matching objective of the account's current steps; steps that are done move on,
    /// quests whose last step is done become <see cref="QuestStatus.Ready"/>. Returns how many quests changed.
    /// </summary>
    public int Record(IDataRegistry data, ulong account, ObjectiveType type, string id, int count)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!_byAccount.TryGetValue(account, out PlayerQuests? quests))
        {
            return 0;
        }

        int changed = 0;
        IReadOnlyList<QuestProgress> active = quests.Active;
        for (int i = 0; i < active.Count; i++)
        {
            QuestProgress progress = active[i];
            if (progress.Status != QuestStatus.Active)
            {
                continue;
            }

            IReadOnlyList<QuestObjectiveDefinition> objectives = data.Quests[progress.Quest].Steps[progress.Step].Objectives;
            bool any = false;
            for (int o = 0; o < objectives.Count; o++)
            {
                QuestObjectiveDefinition objective = objectives[o];
                if (objective.Type == type && (objective.Target.Length == 0 || objective.Target == id) && progress.Counts[o] < objective.Count)
                {
                    progress.Counts[o] = Math.Min(objective.Count, progress.Counts[o] + count);
                    any = true;
                }
            }

            if (any)
            {
                changed++;
                Advance(data, progress);
            }
        }

        return changed;
    }

    /// <summary>The quests of every account, in account order, for saving.</summary>
    public IReadOnlyList<QuestSave> Export(ulong account, IDataRegistry data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!_byAccount.TryGetValue(account, out PlayerQuests? quests))
        {
            return [];
        }

        var saves = new List<QuestSave>();
        foreach (QuestProgress progress in quests.Active)
        {
            saves.Add(new QuestSave(data.Quests[progress.Quest].Id, progress.Step, [.. progress.Counts], Done: false));
        }

        foreach (int quest in quests.Done.Order())
        {
            saves.Add(new QuestSave(data.Quests[quest].Id, 0, [], Done: true));
        }

        return saves;
    }

    /// <summary>Restores saved quests; ids the data no longer knows, and steps it no longer has, are dropped.</summary>
    public void Import(ulong account, IReadOnlyList<QuestSave>? saved, IDataRegistry data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _byAccount.Remove(account);
        PlayerQuests quests = Of(account);
        foreach (QuestSave save in saved ?? [])
        {
            int quest = IndexOf(data, save.Quest);
            if (quest < 0)
            {
                continue;
            }

            if (save.Done)
            {
                quests.Finish(quest);
            }
            else if (save.Step >= 0 && save.Step < data.Quests[quest].Steps.Count && quests.Find(quest) is null)
            {
                QuestProgress progress = quests.Take(quest);
                progress.Step = save.Step;
                for (int i = 0; i < Math.Min(save.Counts.Count, progress.Counts.Length); i++)
                {
                    progress.Counts[i] = save.Counts[i];
                }

                Advance(data, progress);
            }
        }
    }

    /// <summary>Adds every account's quests, in account order, to a state hash.</summary>
    public void AddTo(ref StateHash hash)
    {
        foreach (ulong account in _byAccount.Keys.Order())
        {
            hash.Add(account);
            _byAccount[account].AddTo(ref hash);
        }
    }

    /// <summary>True when every objective of the progress's current step is done.</summary>
    public static bool StepDone(IDataRegistry data, QuestProgress progress)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(progress);
        IReadOnlyList<QuestObjectiveDefinition> objectives = data.Quests[progress.Quest].Steps[progress.Step].Objectives;
        for (int o = 0; o < objectives.Count; o++)
        {
            if (progress.Counts[o] < objectives[o].Count)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Moves on from done steps; the quest becomes ready after its last.</summary>
    internal static void Advance(IDataRegistry data, QuestProgress progress)
    {
        while (progress.Status == QuestStatus.Active && StepDone(data, progress))
        {
            if (progress.Step + 1 >= data.Quests[progress.Quest].Steps.Count)
            {
                progress.Status = QuestStatus.Ready;
                return;
            }

            progress.Step++;
            Array.Clear(progress.Counts);
        }
    }

    private static int IndexOf(IDataRegistry data, string id)
    {
        for (int i = 0; i < data.Quests.Count; i++)
        {
            if (data.Quests[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>Why a quest cannot be taken, in <c>IntentResult.Detail</c> (M2-19).</summary>
public enum QuestRefusal : byte
{
    /// <summary>It can.</summary>
    None = 0,

    /// <summary>No such quest.</summary>
    Unknown = 1,

    /// <summary>Taken or done already.</summary>
    AlreadyTaken = 2,

    /// <summary>The player's level is too low.</summary>
    LevelTooLow = 3,

    /// <summary>An earlier quest must be done first.</summary>
    NeedsEarlierQuest = 4,

    /// <summary>Too many quests at once.</summary>
    TooMany = 5,

    /// <summary>The quest is another class's (a prologue, E6).</summary>
    OtherClass = 6,
}
