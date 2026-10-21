using SumonerWorld.PlaytestReport;

namespace SumonerWorld.Tests.Playtest;

public class UspReportTests
{
    [Fact]
    public void Wilson_interval_matches_the_qa_plan_example()
    {
        (double low, double high) = PlaytestStats.Wilson(14, 20);

        Assert.Equal(0.48, low, 2);
        Assert.Equal(0.85, high, 2);
    }

    [Fact]
    public void Kappa_is_one_for_full_agreement_and_zero_for_chance_agreement()
    {
        Assert.Equal(1.0, PlaytestStats.CohensKappa([true, false, true], [true, false, true]));
        Assert.Equal(0.0, PlaytestStats.CohensKappa([true, true, false, false], [true, false, true, false]), 6);
        // Textbook example: 20 yes/yes, 15 no/no, 5 + 10 disagreements of 50 → κ = 0.4.
        bool[] a = [.. Enumerable.Repeat(true, 25), .. Enumerable.Repeat(false, 25)];
        bool[] b = [.. Enumerable.Repeat(true, 20), .. Enumerable.Repeat(false, 5), .. Enumerable.Repeat(true, 10), .. Enumerable.Repeat(false, 15)];
        Assert.Equal(0.4, PlaytestStats.CohensKappa(a, b), 6);
    }

    [Theory]
    [InlineData(14, 20, UspVerdict.Readable)]
    [InlineData(13, 20, UspVerdict.PartlyReadable)]
    [InlineData(11, 20, UspVerdict.PartlyReadable)]
    [InlineData(10, 20, UspVerdict.NotReadable)]
    public void The_b7_rule_splits_at_70_and_55_percent(int noticed, int testers, UspVerdict expected) =>
        Assert.Equal(expected, UspReport.Decide(noticed, testers));

    [Fact]
    public void Raters_agreeing_settle_the_code_and_rater_c_settles_disagreements()
    {
        const string sheet = """
            session,rater,noticed,time_to_notice,reaction,notes
            aaaaaa,A,yes,6:10,calf follows,"said ""it remembers me"", laughed"
            aaaaaa,B,yes,5:40,calf follows,
            bbbbbb,A,yes,8:00,herd leaves,
            bbbbbb,B,no,,,
            bbbbbb,C,no,,,
            cccccc,A,no,,,
            """;

        IReadOnlyList<RaterCode> codes = Coding.ReadCodes(new StringReader(sheet));
        IReadOnlyList<FinalCode> finals = Coding.Settle(codes);

        Assert.Equal(new FinalCode("aaaaaa", true, 340, Disagreed: false, Unresolved: false), finals[0]);
        Assert.Equal(new FinalCode("bbbbbb", false, null, Disagreed: true, Unresolved: false), finals[1]);
        Assert.True(finals[2].Unresolved);
    }

    [Fact]
    public void The_report_joins_telemetry_and_codes_and_flags_what_needs_a_second_look()
    {
        const string staged = """
            {"t":0,"type":"session_start","session":"aaaaaa0123456789","build":"b","creatures":14}
            {"t":10.4,"type":"player_action","action":"help","target_species":"mossback","affected":1}
            {"t":10.5,"type":"memory_behaviour_shown","emote":"affection","in_view":true,"distance":5.2,"species":"mossback","memory_type":"SavedLifeOf","rumour":false}
            {"t":900,"type":"session_end","duration":900}
            """;
        const string unstaged = """
            {"t":0,"type":"session_start","session":"bbbbbb0123456789","build":"b","creatures":14}
            {"t":30,"type":"player_action","action":"feed","target_species":"bogfox","affected":1}
            {"t":31,"type":"memory_behaviour_shown","emote":"trust","in_view":false,"distance":3,"species":"bogfox","memory_type":"FedBy","rumour":false}
            {"t":900,"type":"session_end","duration":900}
            """;
        SessionLog[] logs = [SessionLog.Read(new StringReader(staged)), SessionLog.Read(new StringReader(unstaged))];
        const string sheet = """
            session,rater,noticed,time_to_notice
            aaaaaa,A,yes,1:00
            aaaaaa,B,yes,0:50
            bbbbbb,A,yes,2:00
            bbbbbb,B,yes,2:30
            """;
        const string answers = """
            session,q2,q3,q4
            aaaaaa,yes,6,5
            bbbbbb,no,4,
            """;

        string report = UspReport.Build(logs, Coding.ReadCodes(new StringReader(sheet)), Coding.ReadResponses(new StringReader(answers)));

        Assert.Contains("below the gate size of 20", report, StringComparison.Ordinal);
        Assert.Contains("**2 of 2 (100%)**", report, StringComparison.Ordinal);
        Assert.Contains("Median time-to-notice (noticed testers) | 1:25", report, StringComparison.Ordinal);
        Assert.Contains("of which on screen 1 (50%)", report, StringComparison.Ordinal);
        Assert.Contains("no memory moment on screen by 10:00 (a staging problem, not readability): 1 (bbbbbb)", report, StringComparison.Ordinal);
        Assert.Contains("re-check the video): 1 (bbbbbb)", report, StringComparison.Ordinal);
        Assert.Contains("feed 1, help 1", report, StringComparison.Ordinal);
        Assert.Contains("Q3 care (1–7): median 5", report, StringComparison.Ordinal);
    }
}
