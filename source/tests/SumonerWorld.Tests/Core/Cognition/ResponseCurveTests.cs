using SumonerWorld.Core.Cognition.Decision;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Tests.Core.Cognition;

public class ResponseCurveTests
{
    [Theory]
    [InlineData(0, 200)]
    [InlineData(500, 600)]
    [InlineData(1000, 1000)]
    public void Linear_is_offset_plus_slope(int input, int expected)
    {
        Assert.Equal(expected, ResponseCurve.Evaluate(new CurveDefinition { Type = CurveType.Linear, Slope = 800, Offset = 200 }, input));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(500, 250)]
    [InlineData(1000, 1000)]
    public void Quadratic_grows_slowly_then_fast(int input, int expected)
    {
        Assert.Equal(expected, ResponseCurve.Evaluate(new CurveDefinition { Type = CurveType.Quadratic, Slope = 1000 }, input));
    }

    [Fact]
    public void Logistic_is_an_s_curve_around_the_centre()
    {
        var curve = new CurveDefinition { Type = CurveType.Logistic, Centre = 500, Width = 200 };

        Assert.Equal(0, ResponseCurve.Evaluate(curve, 300));
        Assert.Equal(500, ResponseCurve.Evaluate(curve, 500));
        Assert.Equal(1000, ResponseCurve.Evaluate(curve, 700));
        int previous = -1;
        for (int x = 0; x <= 1000; x++)
        {
            int value = ResponseCurve.Evaluate(curve, x);
            Assert.True(value >= previous, $"S-curve must never fall (x={x}).");
            previous = value;
        }
    }

    [Fact]
    public void Step_switches_at_the_threshold()
    {
        var curve = new CurveDefinition { Type = CurveType.Step, Threshold = 400, Low = 100, High = 900 };

        Assert.Equal(100, ResponseCurve.Evaluate(curve, 399));
        Assert.Equal(900, ResponseCurve.Evaluate(curve, 400));
    }

    [Fact]
    public void Invert_mirrors_and_everything_is_clamped()
    {
        var steep = new CurveDefinition { Type = CurveType.Linear, Slope = 2000, Offset = -500 };
        var inverted = steep with { Invert = true };

        Assert.Equal(0, ResponseCurve.Evaluate(steep, 100));
        Assert.Equal(1000, ResponseCurve.Evaluate(steep, 900));
        Assert.Equal(1000, ResponseCurve.Evaluate(inverted, 100));
        Assert.Equal(1000, ResponseCurve.Evaluate(steep, 5000));
        Assert.Equal(0, ResponseCurve.Evaluate(steep, -5000));
    }
}
