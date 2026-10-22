using System.Numerics;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Intents;
using SumonerWorld.Core.Items;
using SumonerWorld.Core.Progression;
using SumonerWorld.Core.Quests;
using SumonerWorld.Core.Transactions;
using SumonerWorld.Core.World;

namespace SumonerWorld.Core.Academy;

/// <summary>A student's answers to the ranking questions and their result (Academy spec §3, M2-21).</summary>
public sealed class AcademyStanding
{
    /// <summary>Answer per question + 1 (0 = not answered yet).</summary>
    public byte[] Answers { get; } = new byte[AcademyDefinition.QuestionCount];

    /// <summary>Practical bonus to the rank score (the arena moments, M2-21b).</summary>
    public int Bonus { get; internal set; }

    /// <summary>True once the Headmaster gave the rank.</summary>
    public bool Ranked { get; internal set; }

    /// <summary>How many questions are answered.</summary>
    public int Answered => Answers.Count(answer => answer > 0);
}

/// <summary>A saved standing (M2-21).</summary>
/// <param name="Answers">Answer per question + 1.</param>
/// <param name="Bonus">Practical bonus.</param>
/// <param name="Ranked">True once ranked.</param>
public sealed record AcademySave(IReadOnlyList<int> Answers, int Bonus, bool Ranked)
{
    /// <summary>Equal when every field is, the answers compared one by one.</summary>
    public bool Equals(AcademySave? other) => other is not null && Bonus == other.Bonus && Ranked == other.Ranked && Answers.SequenceEqual(other.Answers);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Bonus, Ranked);
}

/// <summary>Every student's standing, by account (server state, part of the state hash).</summary>
public sealed class AcademyBook
{
    private readonly Dictionary<ulong, AcademyStanding> _byAccount = [];

    /// <summary>The account's standing (created on first use).</summary>
    public AcademyStanding Of(ulong account)
    {
        if (!_byAccount.TryGetValue(account, out AcademyStanding? standing))
        {
            standing = new AcademyStanding();
            _byAccount.Add(account, standing);
        }

        return standing;
    }

    /// <summary>For saving (null when the account never answered).</summary>
    public AcademySave? Export(ulong account) =>
        _byAccount.TryGetValue(account, out AcademyStanding? standing)
            ? new AcademySave([.. standing.Answers.Select(answer => (int)answer)], standing.Bonus, standing.Ranked)
            : null;

    /// <summary>Restores a saved standing.</summary>
    public void Import(ulong account, AcademySave? saved)
    {
        _byAccount.Remove(account);
        if (saved is null)
        {
            return;
        }

        AcademyStanding standing = Of(account);
        for (int i = 0; i < Math.Min(saved.Answers.Count, standing.Answers.Length); i++)
        {
            standing.Answers[i] = (byte)Math.Clamp(saved.Answers[i], 0, AcademyDefinition.AnswerCount);
        }

        standing.Bonus = saved.Bonus;
        standing.Ranked = saved.Ranked;
    }

    /// <summary>Adds every standing, in account order, to a state hash.</summary>
    public void AddTo(ref StateHash hash)
    {
        foreach (ulong account in _byAccount.Keys.Order())
        {
            AcademyStanding standing = _byAccount[account];
            hash.Add(account);
            foreach (byte answer in standing.Answers)
            {
                hash.Add(answer);
            }

            hash.Add((ulong)standing.Bonus);
            hash.Add(standing.Ranked ? 1UL : 0UL);
        }
    }
}

/// <summary>How answers become a class and a rank (Academy spec §3.2–3.3).</summary>
public static class RankingRules
{
    /// <summary>Most practical bonus (the arena moments).</summary>
    public const int MaxBonus = 15;

    /// <summary>The class the answers point to: the most points; a tie goes to <paramref name="tieRoll"/> among the tied.</summary>
    public static SummonerClass Leaning(AcademyDefinition academy, AcademyStanding standing, int tieRoll)
    {
        ArgumentNullException.ThrowIfNull(academy);
        ArgumentNullException.ThrowIfNull(standing);
        Span<int> points = stackalloc int[AcademyDefinition.ClassIds.Count];
        for (int q = 0; q < academy.Questions.Count; q++)
        {
            if (standing.Answers[q] == 0)
            {
                continue;
            }

            foreach ((string id, int value) in academy.Questions[q].Answers[standing.Answers[q] - 1].Classes)
            {
                points[AcademyDefinition.ClassIds.ToList().IndexOf(id)] += value;
            }
        }

        int best = int.MinValue;
        int tied = 0;
        foreach (int value in points)
        {
            tied = value > best ? 1 : value == best ? tied + 1 : tied;
            best = Math.Max(best, value);
        }

        int pick = Math.Abs(tieRoll) % tied;
        for (int c = 0; c < points.Length; c++)
        {
            if (points[c] == best && pick-- == 0)
            {
                return (SummonerClass)c;
            }
        }

        return SummonerClass.Elf;
    }

    /// <summary>The rank score: answer points, the practical bonus and the random part.</summary>
    public static int Score(AcademyDefinition academy, AcademyStanding standing, int randomPart)
    {
        ArgumentNullException.ThrowIfNull(academy);
        ArgumentNullException.ThrowIfNull(standing);
        int score = 0;
        for (int q = 0; q < academy.Questions.Count; q++)
        {
            score += standing.Answers[q] == 0 ? 0 : academy.Questions[q].Answers[standing.Answers[q] - 1].Rank * academy.ScorePerAnswerPoint;
        }

        return score + Math.Min(standing.Bonus, MaxBonus) + randomPart;
    }

    /// <summary>The rank a score gives: F, E, D or C (Academy spec §3.3).</summary>
    public static Rank RankFor(AcademyDefinition academy, int score)
    {
        ArgumentNullException.ThrowIfNull(academy);
        int rank = 0;
        while (rank < academy.RankThresholds.Count && score >= academy.RankThresholds[rank])
        {
            rank++;
        }

        return (Rank)rank;
    }

    /// <summary>The class a student joins: the leaning if it is open, else the first open class (M2: Elf).</summary>
    public static SummonerClass Joined(AcademyDefinition academy, SummonerClass leaning)
    {
        ArgumentNullException.ThrowIfNull(academy);
        string id = AcademyDefinition.ClassIds[(int)leaning];
        return academy.OpenClasses.Contains(id) ? leaning : (SummonerClass)AcademyDefinition.ClassIds.ToList().IndexOf(academy.OpenClasses[0]);
    }

    /// <summary>The index of an Academy person, or −1.</summary>
    public static int NpcIndex(IDataRegistry data, string id)
    {
        ArgumentNullException.ThrowIfNull(data);
        IReadOnlyList<NpcDefinition> npcs = data.Academy?.Npcs ?? [];
        for (int i = 0; i < npcs.Count; i++)
        {
            if (npcs[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The ranking quest's index, or −1.</summary>
    public static int RankingQuest(IDataRegistry data)
    {
        ArgumentNullException.ThrowIfNull(data);
        for (int i = 0; i < data.Quests.Count; i++)
        {
            if (data.Quests[i].Id == data.Academy?.RankingQuest)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>What a talk ended with, in <see cref="IntentResult.Detail"/>: the ranking ceremony packs the rank and the leaning.</summary>
public static class TalkResult
{
    /// <summary>Set when this talk was the ranking ceremony.</summary>
    public const byte Ranked = 0x80;

    /// <summary>The detail of a ceremony: the rank in bits 0–1, the class leaning in bits 2–3.</summary>
    public static byte Ceremony(Rank rank, SummonerClass leaning) => (byte)(Ranked | ((int)rank & 3) | (((int)leaning & 3) << 2));

    /// <summary>The rank of a ceremony detail.</summary>
    public static Rank RankOf(byte detail) => (Rank)(detail & 3);

    /// <summary>The leaning of a ceremony detail.</summary>
    public static SummonerClass LeaningOf(byte detail) => (SummonerClass)((detail >> 2) & 3);
}

/// <summary>
/// A player talks to one of the Academy's people (Academy spec §1, M2-21): within <see cref="Range"/>; it counts for talk
/// objectives. The Headmaster, once every ranking question is answered, holds the ceremony: the class and rank are set from the
/// answers, the practical bonus and a seeded random part, once and for life (class) or until a rank trial (rank).
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player.</param>
/// <param name="Npc">The person's index.</param>
public record struct TalkTransaction(CreatureWorld Creatures, Entity Avatar, int Npc) : ITransaction
{
    /// <summary>How close a player must stand to talk, in metres.</summary>
    public const float Range = 4f;

    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        ArgumentNullException.ThrowIfNull(Creatures);
        if (!Creatures.World.Store<PlayerAvatar>().Has(Avatar))
        {
            return IntentResult.Reject(IntentRejectReason.InvalidTarget);
        }

        if (Creatures.Data.Academy is not AcademyDefinition academy || (uint)Npc >= (uint)academy.Npcs.Count)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        Vector3 at = Creatures.World.GetComponent<Position>(Avatar).Value;
        NpcDefinition npc = academy.Npcs[Npc];
        return new Vector2(at.X - npc.X, at.Z - npc.Z).Length() > Range ? IntentResult.Reject(IntentRejectReason.OutOfRange) : IntentResult.Applied();
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        AcademyDefinition academy = Creatures.Data.Academy!;
        ulong account = Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId;
        Creatures.Events.Publish(new QuestEvent(account, ObjectiveType.Talk, Npc, 1));
        AcademyStanding standing = Creatures.Economy.Academy.Of(account);
        if (academy.Npcs[Npc].Role != "headmaster" || standing.Ranked || standing.Answered < AcademyDefinition.QuestionCount)
        {
            return IntentResult.Applied();
        }

        SummonerClass leaning = RankingRules.Leaning(academy, standing, Creatures.Random.NextInt(0, 1000));
        Rank rank = RankingRules.RankFor(academy, RankingRules.Score(academy, standing, Creatures.Random.NextInt(-academy.RandomRange, academy.RandomRange + 1)));
        ref PlayerProgress progress = ref Creatures.World.GetComponent<PlayerProgress>(Avatar);
        progress.Class = (int)RankingRules.Joined(academy, leaning);
        progress.Rank = (int)rank;
        standing.Ranked = true;
        PlayerStats.Restat(Creatures, Avatar);
        return IntentResult.Applied(TalkResult.Ceremony(rank, leaning));
    }
}

/// <summary>
/// A student answers the Headmaster's next ranking question (Academy spec §3.1, M2-21): only while the ranking quest asks for
/// answers, in order, once each. It counts for answer objectives.
/// </summary>
/// <param name="Creatures">The world.</param>
/// <param name="Avatar">The player.</param>
/// <param name="Npc">The Headmaster's index.</param>
/// <param name="Question">The question's index.</param>
/// <param name="Answer">The answer's index.</param>
public record struct AnswerTransaction(CreatureWorld Creatures, Entity Avatar, int Npc, int Question, int Answer) : ITransaction
{
    /// <inheritdoc />
    public readonly IntentResult Validate(ulong now)
    {
        var talk = new TalkTransaction(Creatures, Avatar, Npc);
        IntentResult near = talk.Validate(now);
        if (!near.IsApplied)
        {
            return near;
        }

        AcademyDefinition academy = Creatures.Data.Academy!;
        ulong account = Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId;
        AcademyStanding standing = Creatures.Economy.Academy.Of(account);
        if (academy.Npcs[Npc].Role != "headmaster" || (uint)Answer >= AcademyDefinition.AnswerCount || Question != standing.Answered
            || standing.Ranked || Question >= AcademyDefinition.QuestionCount)
        {
            return IntentResult.Reject(IntentRejectReason.InvalidArguments);
        }

        QuestProgress? quest = Creatures.Economy.Quests.Of(account).Find(RankingRules.RankingQuest(Creatures.Data));
        bool asking = quest is { Status: QuestStatus.Active }
            && Creatures.Data.Quests[quest.Quest].Steps[quest.Step].Objectives.Any(objective => objective.Type == ObjectiveType.Answer);
        return asking ? IntentResult.Applied() : IntentResult.Reject(IntentRejectReason.WrongState);
    }

    /// <inheritdoc />
    public readonly IntentResult Commit(ulong now)
    {
        ulong account = Creatures.World.GetComponent<PlayerAvatar>(Avatar).AccountId;
        Creatures.Economy.Academy.Of(account).Answers[Question] = (byte)(Answer + 1);
        Creatures.Events.Publish(new QuestEvent(account, ObjectiveType.Answer, 0, 1));
        return IntentResult.Applied();
    }
}
