using System.Numerics;
using SumonerWorld.Net.Client;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Tests.Net;

public class RemoteInterpolationTests
{
    private const double Frame = 1.0 / 30;

    [Fact]
    public void Steady_snapshots_are_shown_100_ms_late_on_the_exact_path_with_the_right_speed()
    {
        var remote = new RemoteInterpolation();
        for (ulong tick = 2; tick <= 300; tick++)
        {
            if (tick % 3 != 1)
            {
                remote.Add(tick, Creature(new Vector3(tick * 0.2f, 0, 5), facing: 64));
            }

            remote.Advance(Frame);
        }

        Assert.True(remote.TrySample(SnapshotEntityKind.Creature, 7, out RemoteState state));
        // The clock runs at real time inside the dead zone around "newest − delay".
        double target = 300 - RemoteInterpolation.DelayTicks;
        Assert.InRange(remote.RenderTick, target - RemoteInterpolation.DeadZoneTicks - 0.5, target + RemoteInterpolation.DeadZoneTicks + 0.5);
        Assert.Equal(remote.RenderTick * 0.2, state.Position.X, 3);
        Assert.Equal(6f, state.Speed, 2);
        Assert.Equal(MathF.PI / 2, state.Yaw, 4);
        Assert.Equal(2, state.Species);
    }

    [Fact]
    public void Facing_turns_the_short_way_across_north()
    {
        var remote = new RemoteInterpolation();
        remote.Add(100, Creature(Vector3.Zero, facing: 250));
        remote.Add(102, Creature(Vector3.Zero, facing: 6));
        remote.Add(104, Creature(Vector3.Zero, facing: 6));

        // The first snapshot puts the clock at 97; ease it to 101 (half-way between the two headings).
        remote.Advance(4 * Frame);
        while (remote.RenderTick < 101)
        {
            remote.Advance(0.001);
        }

        remote.TrySample(SnapshotEntityKind.Creature, 7, out RemoteState state);
        float turns = state.Yaw / MathF.Tau;
        Assert.True(turns > 0.99f || turns < 0.01f, $"yaw {turns} turns should be near north");
    }

    [Fact]
    public void Past_the_newest_sample_the_entity_holds_and_a_long_silence_forgets_it_and_jumps_the_clock()
    {
        var remote = new RemoteInterpolation();
        remote.Add(10, Creature(new Vector3(1, 0, 0), facing: 0));
        remote.Add(12, Creature(new Vector3(2, 0, 0), facing: 0));
        for (int i = 0; i < 6; i++)
        {
            remote.Advance(Frame);
        }

        // Render tick ≈ 13: one tick past the newest sample, still moving at 0.5 m per tick.
        remote.TrySample(SnapshotEntityKind.Creature, 7, out RemoteState moving);
        Assert.Equal(2 + ((remote.RenderTick - 12) * 0.5), moving.Position.X, 3);
        Assert.Equal(15f, moving.Speed, 2);

        for (int i = 0; i < 24; i++)
        {
            remote.Advance(Frame);
        }

        remote.TrySample(SnapshotEntityKind.Creature, 7, out RemoteState held);
        Assert.Equal(new Vector3(2, 0, 0), held.Position);
        Assert.Equal(0, held.Speed);

        remote.Add(100, Creature(new Vector3(9, 0, 0), facing: 0) with { Id = 8 });
        remote.Advance(Frame);
        Assert.InRange(remote.RenderTick, 96.9, 97.1);
        Assert.False(remote.TrySample(SnapshotEntityKind.Creature, 7, out _));
        Assert.Equal(1, remote.Count);

        var states = new List<RemoteState>();
        remote.Sample(states);
        Assert.Equal(8UL, Assert.Single(states).Id);

        remote.Clear();
        Assert.Equal(0, remote.Count);
        Assert.Equal(0, remote.RenderTick);
    }

    private static SnapshotEntity Creature(Vector3 position, byte facing) =>
        new(SnapshotEntityKind.Creature, 7, position, facing, Emote: 1, Species: 2);
}
