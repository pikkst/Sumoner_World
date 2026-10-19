using SumonerWorld.Core.Diagnostics;

namespace SumonerWorld.Tests.Core.Diagnostics;

public class StateHashTests
{
    [Fact]
    public void Matches_the_fnv1a_reference_values()
    {
        // Reference FNV-1a 64: empty input = offset basis; "a" = 0xAF63DC4C8601EC8C.
        var empty = default(StateHash);
        var a = default(StateHash);
        a.Add<byte>("a"u8);

        Assert.Equal(0xCBF29CE484222325UL, empty.Value);
        Assert.Equal(0xAF63DC4C8601EC8CUL, a.Value);
    }

    [Fact]
    public void Order_matters_and_equal_input_gives_equal_hashes()
    {
        var first = default(StateHash);
        first.Add(1UL);
        first.Add(2UL);
        var second = default(StateHash);
        second.Add(1UL);
        second.Add(2UL);
        var swapped = default(StateHash);
        swapped.Add(2UL);
        swapped.Add(1UL);

        Assert.Equal(first.Value, second.Value);
        Assert.NotEqual(first.Value, swapped.Value);
    }
}
