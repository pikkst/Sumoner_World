using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Data;

/// <summary>The Summoner Academy (Academy spec, M2-21).</summary>
internal static partial class QuestValidation
{
    private static readonly string[] NpcRoles = ["headmaster", "arena_master", "keeper", "vendor"];

    internal static void ValidateAcademy(
        AcademyDefinition academy, string where, List<QuestDefinition> quests, List<SpeciesDefinition> species, List<string> errors)
    {
        if (!species.Any(definition => definition.Id == academy.ConstructSpecies))
        {
            errors.Add($"{where}: constructSpecies '{academy.ConstructSpecies}' is not a species.");
        }

        if (academy.ArenaRadius <= 0 || academy.ArenaRadius >= academy.Radius)
        {
            errors.Add($"{where}: the arena lies inside the campus (0 < arenaRadius < radius).");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (NpcDefinition npc in academy.Npcs)
        {
            if (!DataRegistryLoader.IsSnakeCaseId(npc.Id) || !ids.Add(npc.Id) || !NpcRoles.Contains(npc.Role))
            {
                errors.Add($"{where}: npc '{npc.Id}' needs a unique snake_case id and a role ({string.Join(", ", NpcRoles)}).");
            }
        }

        if (academy.Npcs.Count(npc => npc.Role == "headmaster") != 1)
        {
            errors.Add($"{where}: the Academy needs exactly one headmaster.");
        }

        DataRegistryLoader.CheckRange(academy.Questions.Count, AcademyDefinition.QuestionCount, AcademyDefinition.QuestionCount, "questions", where, errors);
        for (int q = 0; q < academy.Questions.Count; q++)
        {
            RankingQuestionDefinition question = academy.Questions[q];
            DataRegistryLoader.CheckRange(question.Answers.Count, AcademyDefinition.AnswerCount, AcademyDefinition.AnswerCount, $"questions[{q}].answers", where, errors);
            foreach (RankingAnswerDefinition answer in question.Answers)
            {
                DataRegistryLoader.CheckRange(answer.Rank, 0, 3, $"questions[{q}].answers.rank", where, errors);
                foreach (string unknown in answer.Classes.Keys.Where(key => !AcademyDefinition.ClassIds.Contains(key)))
                {
                    errors.Add($"{where}: questions[{q}] names unknown class '{unknown}'.");
                }
            }
        }

        if (academy.RankThresholds.Count != 3 || academy.RankThresholds[0] >= academy.RankThresholds[1] || academy.RankThresholds[1] >= academy.RankThresholds[2])
        {
            errors.Add($"{where}: rankThresholds are three rising scores (ranks E, D, C).");
        }

        DataRegistryLoader.CheckRange(academy.RankLines.Count, 4, 4, "rankLines", where, errors);
        DataRegistryLoader.CheckRange(academy.RandomRange, 0, 50, "randomRange", where, errors);
        if (academy.OpenClasses.Count == 0 || academy.OpenClasses.Any(open => !AcademyDefinition.ClassIds.Contains(open)))
        {
            errors.Add($"{where}: openClasses must name known classes.");
        }

        if (!quests.Any(quest => quest.Id == academy.RankingQuest))
        {
            errors.Add($"{where}: rankingQuest '{academy.RankingQuest}' is not a quest.");
        }
    }
}
