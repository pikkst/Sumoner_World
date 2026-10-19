using System.Numerics;
using BenchmarkDotNet.Attributes;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.Randomness;
using SumonerWorld.Core.Spatial;

namespace SumonerWorld.Benchmarks.Spatial;

/// <summary>
/// M1-03: one full perception pass. Every creature moves, then runs a 40 m radius query (AI tier T0 range, AI Bible §9.1)
/// and a 60 m / 60° sight cone, in the 512 m M1 test region (M0-07 B1).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SpatialGridBenchmarks
{
    private const float RegionSize = 512f;
    private const float PerceptionRadius = 40f;
    private const float SightRange = 60f;
    private const float SightHalfAngle = MathF.PI / 3;

    private readonly Entity[] _buffer = new Entity[128];
    private SpatialGrid _grid = null!;
    private Vector3[] _positions = [];
    private int _tick;

    /// <summary>Creature count: the M1 gate and a stress case.</summary>
    [Params(200, 2_000)]
    public int Count { get; set; }

    /// <summary>Places creatures at seeded random positions.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var random = new SeededRandom(42);
        _grid = new SpatialGrid(new SpatialGridSettings(0, 0, RegionSize, RegionSize, SpatialGridSettings.DefaultCellSize, Count));
        _positions = new Vector3[Count];
        for (int i = 0; i < Count; i++)
        {
            _positions[i] = new Vector3((float)(random.NextDouble() * RegionSize), 0, (float)(random.NextDouble() * RegionSize));
            _grid.Insert(new Entity(i, 0), _positions[i]);
        }
    }

    /// <summary>Move every creature 0.2 m and run its radius and cone queries.</summary>
    [Benchmark]
    public int PerceptionPass()
    {
        float step = (_tick++ % 2 == 0) ? 0.2f : -0.2f;
        int seen = 0;
        for (int i = 0; i < _positions.Length; i++)
        {
            _positions[i].X += step;
            var entity = new Entity(i, 0);
            _grid.Move(entity, _positions[i]);
            seen += _grid.QueryRadius(_positions[i], PerceptionRadius, _buffer).Count;
            seen += _grid.QueryCone(_positions[i], Vector3.UnitZ, SightRange, SightHalfAngle, _buffer).Count;
        }

        return seen;
    }
}
