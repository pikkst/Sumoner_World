using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Core.Cognition.Decision;

/// <summary>Evaluates data-defined response curves with integer maths (AI Bible §7.2, R11). Input and output are in [0, 1000].</summary>
public static class ResponseCurve
{
    /// <summary>Highest input and output value.</summary>
    public const int Max = 1000;

    /// <summary>The curve's output for <paramref name="input"/> (clamped to [0, 1000] first).</summary>
    public static int Evaluate(CurveDefinition curve, int input)
    {
        ArgumentNullException.ThrowIfNull(curve);

        long x = Math.Clamp(input, 0, Max);
        long value = curve.Type switch
        {
            CurveType.Linear => curve.Offset + (curve.Slope * x / Max),
            CurveType.Quadratic => curve.Offset + (curve.Slope * x * x / ((long)Max * Max)),
            CurveType.Logistic => SmoothStep(x, curve.Centre, curve.Width),
            CurveType.Step => x >= curve.Threshold ? curve.High : curve.Low,
            _ => throw new ArgumentOutOfRangeException(nameof(curve), curve.Type, "Unknown curve type."),
        };

        int clamped = (int)Math.Clamp(value, 0, Max);
        return curve.Invert ? Max - clamped : clamped;
    }

    // 0 below centre − width, 1000 above centre + width, smooth S in between: 3t² − 2t³ in fixed point.
    private static long SmoothStep(long x, int centre, int width)
    {
        long t = Math.Clamp((x - (centre - width)) * Max / (2L * width), 0, Max);
        return t * t * ((3L * Max) - (2 * t)) / ((long)Max * Max);
    }
}
