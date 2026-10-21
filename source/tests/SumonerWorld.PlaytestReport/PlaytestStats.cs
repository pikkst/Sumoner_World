namespace SumonerWorld.PlaytestReport;

/// <summary>The two statistics the QA plan asks for: the Wilson interval of the noticed share (B7) and Cohen's κ between raters (B5).</summary>
public static class PlaytestStats
{
    /// <summary>The 95% Wilson score interval for <paramref name="successes"/> out of <paramref name="trials"/>.</summary>
    public static (double Low, double High) Wilson(int successes, int trials, double z = 1.959964)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(successes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(successes, trials);
        if (trials == 0)
        {
            return (0, 1);
        }

        double p = (double)successes / trials;
        double z2 = z * z;
        double centre = (p + (z2 / (2 * trials))) / (1 + (z2 / trials));
        double half = z * Math.Sqrt((p * (1 - p) / trials) + (z2 / (4.0 * trials * trials))) / (1 + (z2 / trials));
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }

    /// <summary>
    /// Cohen's κ for two raters' yes/no codes of the same sessions. When both raters gave every session the same single code the
    /// expected agreement is 1 and κ is undefined; then it returns 1 if they agree everywhere (they do, by construction).
    /// </summary>
    public static double CohensKappa(IReadOnlyList<bool> first, IReadOnlyList<bool> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Count != second.Count || first.Count == 0)
        {
            throw new ArgumentException("Both raters must code the same, non-empty set of sessions.", nameof(second));
        }

        int n = first.Count;
        int agree = 0;
        int firstYes = 0;
        int secondYes = 0;
        for (int i = 0; i < n; i++)
        {
            agree += first[i] == second[i] ? 1 : 0;
            firstYes += first[i] ? 1 : 0;
            secondYes += second[i] ? 1 : 0;
        }

        double observed = (double)agree / n;
        double expected = (((double)firstYes / n) * ((double)secondYes / n)) + (((double)(n - firstYes) / n) * ((double)(n - secondYes) / n));
        return expected >= 1 ? 1 : (observed - expected) / (1 - expected);
    }

    /// <summary>The median, or null for no values.</summary>
    public static double? Median(IEnumerable<double> values)
    {
        double[] sorted = [.. values.Order()];
        if (sorted.Length == 0)
        {
            return null;
        }

        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
