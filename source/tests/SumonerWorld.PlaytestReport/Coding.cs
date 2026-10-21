using System.Globalization;

namespace SumonerWorld.PlaytestReport;

/// <summary>One rater's code for one session (coding guide, <c>coding_sheet.csv</c>).</summary>
public sealed record RaterCode(string Session, string Rater, bool Noticed, double? TimeToNotice);

/// <summary>A tester's questionnaire answers that the report counts (<c>responses.csv</c>): Q2 yes/no, Q3 care and Q4 keep playing (1–7).</summary>
public sealed record Responses(string Session, bool? TreatedDifferently, int? Care, int? KeepPlaying);

/// <summary>A session's final code after the two raters and, where they disagreed, the third (QA plan B5).</summary>
public sealed record FinalCode(string Session, bool Noticed, double? TimeToNotice, bool Disagreed, bool Unresolved);

/// <summary>Reads the sheets and settles each session's code.</summary>
public static class Coding
{
    /// <summary>Raters A and B code every session independently; rater C codes only the sessions where they disagree.</summary>
    public const string ThirdRater = "C";

    /// <summary>Reads <c>coding_sheet.csv</c>: <c>session, rater, noticed, time_to_notice</c> (plus free columns the report ignores).</summary>
    public static IReadOnlyList<RaterCode> ReadCodes(TextReader reader) =>
        [.. Csv.Read(reader).Select(row => new RaterCode(
            row["session"],
            row["rater"].ToUpperInvariant(),
            ParseYesNo(row["noticed"]) ?? throw new FormatException($"Session {row["session"]}, rater {row["rater"]}: noticed must be yes or no."),
            ParseTime(row.GetValueOrDefault("time_to_notice", string.Empty))))];

    /// <summary>Reads <c>responses.csv</c>: <c>session, q2, q3, q4</c>.</summary>
    public static IReadOnlyList<Responses> ReadResponses(TextReader reader) =>
        [.. Csv.Read(reader).Select(row => new Responses(
            row["session"],
            ParseYesNo(row.GetValueOrDefault("q2", string.Empty)),
            ParseScale(row.GetValueOrDefault("q3", string.Empty)),
            ParseScale(row.GetValueOrDefault("q4", string.Empty))))];

    /// <summary>
    /// Settles each session. When A and B agree, that is the code, and the time is the earlier of their two times (time-to-notice is
    /// the earlier of two signals, QA plan B5). When they disagree, C's code is used, or the session stays unresolved.
    /// </summary>
    public static IReadOnlyList<FinalCode> Settle(IEnumerable<RaterCode> codes)
    {
        var result = new List<FinalCode>();
        foreach (IGrouping<string, RaterCode> session in codes.GroupBy(code => code.Session, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            RaterCode? a = session.FirstOrDefault(code => code.Rater == "A");
            RaterCode? b = session.FirstOrDefault(code => code.Rater == "B");
            RaterCode? c = session.FirstOrDefault(code => code.Rater == ThirdRater);
            if (a is null || b is null)
            {
                result.Add(new FinalCode(session.Key, false, null, Disagreed: false, Unresolved: true));
            }
            else if (a.Noticed == b.Noticed)
            {
                double? time = a.Noticed ? Earlier(a.TimeToNotice, b.TimeToNotice) : null;
                result.Add(new FinalCode(session.Key, a.Noticed, time, Disagreed: false, Unresolved: false));
            }
            else
            {
                result.Add(c is null
                    ? new FinalCode(session.Key, false, null, Disagreed: true, Unresolved: true)
                    : new FinalCode(session.Key, c.Noticed, c.Noticed ? c.TimeToNotice : null, Disagreed: true, Unresolved: false));
            }
        }

        return result;
    }

    /// <summary>Cohen's κ between raters A and B over the sessions both coded, or null if there are none.</summary>
    public static double? Kappa(IEnumerable<RaterCode> codes)
    {
        var pairs = codes.GroupBy(code => code.Session, StringComparer.Ordinal)
            .Select(g => (A: g.FirstOrDefault(code => code.Rater == "A"), B: g.FirstOrDefault(code => code.Rater == "B")))
            .Where(pair => pair.A is not null && pair.B is not null)
            .ToList();
        return pairs.Count == 0 ? null : PlaytestStats.CohensKappa([.. pairs.Select(p => p.A!.Noticed)], [.. pairs.Select(p => p.B!.Noticed)]);
    }

    /// <summary>Parses a time as seconds (<c>425</c>) or minutes:seconds (<c>7:05</c>); blank is null.</summary>
    public static double? ParseTime(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Split(':');
        return parts.Length == 2
            ? (int.Parse(parts[0], CultureInfo.InvariantCulture) * 60) + double.Parse(parts[1], CultureInfo.InvariantCulture)
            : double.Parse(text, CultureInfo.InvariantCulture);
    }

    private static double? Earlier(double? x, double? y) => x is null ? y : y is null ? x : Math.Min(x.Value, y.Value);

    private static bool? ParseYesNo(string text) => text.Trim().ToLowerInvariant() switch
    {
        "yes" or "y" or "1" or "true" => true,
        "no" or "n" or "0" or "false" => false,
        _ => null,
    };

    private static int? ParseScale(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value is >= 1 and <= 7 ? value : null;
}
