using System.Numerics;
using SumonerWorld.Core.Simulation;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Net.Client;

/// <summary>How a remote entity is shown this frame.</summary>
/// <param name="Kind">Creature or player.</param>
/// <param name="Id">Stable id or account id.</param>
/// <param name="Position">Interpolated position.</param>
/// <param name="Yaw">Interpolated heading in radians (0 = +Z, positive toward +X).</param>
/// <param name="Speed">Ground speed in metres per second between the two samples used.</param>
/// <param name="Emote">Emote of the newer sample.</param>
/// <param name="Species">Species index (creatures).</param>
/// <param name="Health">Health fraction of the newer sample (255 = full, since protocol 7).</param>
/// <param name="Action">Combat action of the newer sample.</param>
/// <param name="Move">Move of the newer sample (index + 1, 0 = none).</param>
public readonly record struct RemoteState(
    SnapshotEntityKind Kind, ulong Id, Vector3 Position, float Yaw, float Speed, byte Emote, byte Species, byte Health = 255, byte Action = 0, byte Move = 0);

/// <summary>
/// Remote entity interpolation (TDD §3.2): others are shown <see cref="DelayTicks"/> (100 ms) in the past, between the two snapshots
/// around that moment, so they move smoothly although snapshots arrive at 20 Hz with jitter and loss. The render clock follows the
/// newest snapshot: it runs at real time, eases toward "newest − delay" only when more than <see cref="DeadZoneTicks"/> off (the
/// newest tick moves in steps of 1–2, which must not wobble the speed things are shown at) and jumps when more than
/// <see cref="SnapTicks"/> off (after a stall or reconnect). Past an entity's newest sample (its snapshots were lost) it keeps moving along its last velocity for up to
/// <see cref="MaxExtrapolationTicks"/>, then holds. Entities not in any snapshot for
/// <see cref="ForgetTicks"/> are removed. Presentation only: nothing here feeds back into the simulation (R38).
/// </summary>
public sealed class RemoteInterpolation
{
    /// <summary>Render delay: 3 ticks = 100 ms at 30 Hz.</summary>
    public const double DelayTicks = 3;

    /// <summary>How far past its newest sample an entity keeps moving (100 ms), bridging lost snapshots.</summary>
    public const double MaxExtrapolationTicks = 3;

    /// <summary>Clock error the render clock tolerates without correcting.</summary>
    public const double DeadZoneTicks = 1.5;

    /// <summary>Clock error at which the render clock jumps instead of easing.</summary>
    public const double SnapTicks = 15;

    /// <summary>Ticks without a snapshot after which an entity is forgotten (it left the interest radius).</summary>
    public const ulong ForgetTicks = 30;

    private const int SamplesPerEntity = 8;
    private const double EasePerSecond = 3;
    private const float RadiansPerFacingStep = MathF.Tau / 256;

    private readonly Dictionary<(SnapshotEntityKind, ulong), Track> _tracks = [];
    private readonly List<(SnapshotEntityKind, ulong)> _forget = [];
    private ulong _newest;

    /// <summary>The tick being shown (fractional).</summary>
    public double RenderTick { get; private set; }

    /// <summary>Entities being tracked.</summary>
    public int Count => _tracks.Count;

    /// <summary>Records an entity from a snapshot of <paramref name="serverTick"/>.</summary>
    public void Add(ulong serverTick, in SnapshotEntity entity)
    {
        if (!_tracks.TryGetValue((entity.Kind, entity.Id), out Track? track))
        {
            track = new Track();
            _tracks.Add((entity.Kind, entity.Id), track);
        }

        track.Add(serverTick, entity);
        if (serverTick > _newest)
        {
            if (_newest == 0)
            {
                RenderTick = serverTick - DelayTicks;
            }

            _newest = serverTick;
        }
    }

    /// <summary>Forgets everything (new session).</summary>
    public void Clear()
    {
        _tracks.Clear();
        _newest = 0;
        RenderTick = 0;
    }

    /// <summary>Moves the render clock by a frame of <paramref name="seconds"/> and forgets entities that left.</summary>
    public void Advance(double seconds)
    {
        if (_newest == 0)
        {
            return;
        }

        double target = _newest - DelayTicks;
        RenderTick += seconds * SimulationSettings.DefaultTicksPerSecond;
        double error = target - RenderTick;
        if (Math.Abs(error) > SnapTicks)
        {
            RenderTick = target;
        }
        else if (Math.Abs(error) > DeadZoneTicks)
        {
            double outside = error - (Math.Sign(error) * DeadZoneTicks);
            RenderTick += outside * (1 - Math.Exp(-EasePerSecond * seconds));
        }

        foreach (KeyValuePair<(SnapshotEntityKind, ulong), Track> pair in _tracks)
        {
            if (_newest - pair.Value.LastTick > ForgetTicks)
            {
                _forget.Add(pair.Key);
            }
        }

        foreach ((SnapshotEntityKind, ulong) key in _forget)
        {
            _tracks.Remove(key);
        }

        _forget.Clear();
    }

    /// <summary>Writes every tracked entity's state at <see cref="RenderTick"/> into <paramref name="states"/>.</summary>
    public void Sample(List<RemoteState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        states.Clear();
        foreach (KeyValuePair<(SnapshotEntityKind, ulong), Track> pair in _tracks)
        {
            states.Add(pair.Value.At(RenderTick));
        }
    }

    /// <summary>One entity's state at <see cref="RenderTick"/>.</summary>
    public bool TrySample(SnapshotEntityKind kind, ulong id, out RemoteState state)
    {
        bool found = _tracks.TryGetValue((kind, id), out Track? track);
        state = found ? track!.At(RenderTick) : default;
        return found;
    }

    private static float Yaw(byte facing) => facing * RadiansPerFacingStep;

    private sealed class Track
    {
        private readonly ulong[] _ticks = new ulong[SamplesPerEntity];
        private readonly SnapshotEntity[] _samples = new SnapshotEntity[SamplesPerEntity];
        private int _count;

        public ulong LastTick => _count == 0 ? 0 : _ticks[_count - 1];

        public void Add(ulong tick, in SnapshotEntity entity)
        {
            if (_count > 0 && tick <= _ticks[_count - 1])
            {
                return;
            }

            if (_count == SamplesPerEntity)
            {
                Array.Copy(_ticks, 1, _ticks, 0, SamplesPerEntity - 1);
                Array.Copy(_samples, 1, _samples, 0, SamplesPerEntity - 1);
                _count--;
            }

            _ticks[_count] = tick;
            _samples[_count] = entity;
            _count++;
        }

        public RemoteState At(double tick)
        {
            int newer = 0;
            while (newer < _count && _ticks[newer] < tick)
            {
                newer++;
            }

            if (newer == 0 || _count == 1)
            {
                SnapshotEntity only = _samples[newer == 0 ? 0 : _count - 1];
                return new RemoteState(only.Kind, only.Id, only.Position, Yaw(only.Facing), 0, only.Emote, only.Species, only.Health, only.Action, only.Move);
            }

            if (newer == _count)
            {
                // Lost snapshots: continue along the last velocity for a short while (the next sample corrects it).
                SnapshotEntity previous = _samples[_count - 2];
                SnapshotEntity last = _samples[_count - 1];
                double past = tick - _ticks[_count - 1];
                double lastSpan = _ticks[_count - 1] - _ticks[_count - 2];
                if (past > MaxExtrapolationTicks)
                {
                    return new RemoteState(last.Kind, last.Id, last.Position, Yaw(last.Facing), 0, last.Emote, last.Species, last.Health, last.Action, last.Move);
                }

                Vector3 velocity = (last.Position - previous.Position) / (float)lastSpan;
                float lastSpeed = new Vector2(velocity.X, velocity.Z).Length() * SimulationSettings.DefaultTicksPerSecond;
                return new RemoteState(
                    last.Kind, last.Id, last.Position + (velocity * (float)past), Yaw(last.Facing), lastSpeed, last.Emote, last.Species, last.Health, last.Action, last.Move);
            }

            SnapshotEntity a = _samples[newer - 1];
            SnapshotEntity b = _samples[newer];
            double span = _ticks[newer] - _ticks[newer - 1];
            float t = (float)((tick - _ticks[newer - 1]) / span);
            float yawA = Yaw(a.Facing);
            float turn = MathF.IEEERemainder(Yaw(b.Facing) - yawA, MathF.Tau);
            Vector3 step = b.Position - a.Position;
            float speed = new Vector2(step.X, step.Z).Length() / (float)(span / SimulationSettings.DefaultTicksPerSecond);
            return new RemoteState(b.Kind, b.Id, Vector3.Lerp(a.Position, b.Position, t), yawA + (turn * t), speed, b.Emote, b.Species, b.Health, b.Action, b.Move);
        }
    }
}
