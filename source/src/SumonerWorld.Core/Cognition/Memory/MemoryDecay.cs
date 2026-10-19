namespace SumonerWorld.Core.Cognition.Memory;

/// <summary>
/// Integer exponential decay (AI Bible §4.3): <c>salience × 2^(−elapsed / halfLife)</c>. Whole half-lives are a bit shift; the
/// rest comes from a 16-step fixed-point table with linear interpolation. Bit-exact on every platform (R11).
/// </summary>
public static class MemoryDecay
{
    private const int FractionBits = 16;
    private const int StepBits = 4;
    private const int InterpolationBits = FractionBits - StepBits;
    private const int InterpolationMask = (1 << InterpolationBits) - 1;
    private const int MaxHalvings = 16;

    // round(65536 × 2^(−i/16)) for i = 0..16. Hard-coded so no floating-point maths runs at start-up.
    private static readonly int[] Table =
    [
        65536, 62757, 60097, 57549, 55109, 52773, 50535, 48393, 46341,
        44376, 42495, 40693, 38968, 37316, 35734, 34219, 32768,
    ];

    /// <summary>
    /// Strength of <paramref name="salience"/> after <paramref name="elapsedTicks"/>; <paramref name="halfLifeTicks"/> 0 means
    /// the memory never decays. Never increases as time passes.
    /// </summary>
    public static int Apply(int salience, uint elapsedTicks, uint halfLifeTicks)
    {
        if (halfLifeTicks == 0 || elapsedTicks == 0)
        {
            return salience;
        }

        uint halvings = elapsedTicks / halfLifeTicks;
        if (halvings >= MaxHalvings)
        {
            return 0;
        }

        // Position inside the current half-life, as a 16-bit fraction.
        long fraction = ((long)(elapsedTicks % halfLifeTicks) << FractionBits) / halfLifeTicks;
        int step = (int)(fraction >> InterpolationBits);
        int within = (int)(fraction & InterpolationMask);
        long factor = Table[step] - (((long)(Table[step] - Table[step + 1]) * within) >> InterpolationBits);

        return (int)((salience * factor) >> (FractionBits + (int)halvings));
    }
}
