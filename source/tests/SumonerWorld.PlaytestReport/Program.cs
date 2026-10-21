namespace SumonerWorld.PlaytestReport;

/// <summary>
/// Entry point:
/// <c>dotnet run --project tests/SumonerWorld.PlaytestReport -- &lt;telemetry folder&gt; &lt;coding_sheet.csv&gt; [responses.csv] [report.md]</c>.
/// Reads every <c>session-*.jsonl</c> in the folder and prints (or writes) the numbers section of the USP report.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: <telemetry folder> <coding_sheet.csv> [responses.csv] [report.md]");
            return 2;
        }

        SessionLog[] logs = [.. Directory.EnumerateFiles(args[0], "session-*.jsonl").Order(StringComparer.Ordinal).Select(path =>
        {
            using var reader = new StreamReader(path);
            return SessionLog.Read(reader);
        })];
        IReadOnlyList<RaterCode> codes;
        using (var reader = new StreamReader(args[1]))
        {
            codes = Coding.ReadCodes(reader);
        }

        IReadOnlyList<Responses> responses = [];
        if (args.Length > 2)
        {
            using var reader = new StreamReader(args[2]);
            responses = Coding.ReadResponses(reader);
        }

        string report = UspReport.Build(logs, codes, responses);
        if (args.Length > 3)
        {
            File.WriteAllText(args[3], report);
        }
        else
        {
            Console.Write(report);
        }

        return 0;
    }
}
