using System.Collections.Concurrent;
using System.Diagnostics;
using Godot;
using SumonerWorld.Core.World;

namespace SumonerWorld.Game.World;

/// <summary>
/// Client chunk streaming (TDD §5.2): each frame the <see cref="StreamingPlanner"/> decides rings from the viewer position. A chunk
/// entering the full or medium ring gets its props (<see cref="ChunkScatter"/>, put on the <see cref="Heightfield"/>) built on a
/// worker thread as ready-to-upload MultiMesh buffers; the main thread applies at most <see cref="ApplyPerFrame"/> finished chunks
/// per frame from pooled views, so loading never blocks a frame (R20). Full shows detailed trees and rocks plus bushes, medium
/// shows low-poly trees and rocks; far and none show nothing beyond the terrain (impostors come with real art).
/// </summary>
public sealed partial class ChunkStreamer : Node3D
{
    /// <summary>Finished chunks uploaded per frame.</summary>
    public const int ApplyPerFrame = 2;

    private const int FloatsPerTransform = 12;

    private readonly StreamingPlanner _planner;
    private readonly Heightfield _field;
    private readonly ulong _seed;
    private readonly List<ChunkChange> _changes = new(64);
    private readonly Dictionary<ChunkCoord, ChunkView> _views = [];
    private readonly Dictionary<ChunkCoord, int> _generation = [];
    private readonly Stack<ChunkView> _pool = new();
    private readonly ConcurrentQueue<BuiltChunk> _built = new();
    private readonly PropMeshes _meshes = PropMeshes.Create();
    private int _building;

    /// <summary>Creates the streamer for a region.</summary>
    public ChunkStreamer(StreamingSettings settings, Heightfield field, ulong seed)
    {
        _planner = new StreamingPlanner(settings);
        _field = field;
        _seed = seed;
        Name = "ChunkStreamer";
    }

    /// <summary>Whose position decides the rings.</summary>
    public Node3D? Viewer { get; set; }

    /// <summary>The viewer's position (Core vector).</summary>
    public System.Numerics.Vector3 ViewerPosition =>
        Viewer is null ? default : new System.Numerics.Vector3(Viewer.GlobalPosition.X, Viewer.GlobalPosition.Y, Viewer.GlobalPosition.Z);

    /// <summary>Chunks waiting to be planned, built or uploaded (0 once streaming has caught up).</summary>
    public int Backlog => InFlight + _planner.Pending(ViewerPosition);

    /// <summary>The planner (ring per chunk).</summary>
    public StreamingPlanner Planner => _planner;

    /// <summary>Chunks uploaded in the last frame.</summary>
    public int AppliedLastFrame { get; private set; }

    /// <summary>Main-thread time spent uploading in the last frame, in milliseconds.</summary>
    public double ApplyMsLastFrame { get; private set; }

    /// <summary>Chunks being built on worker threads or waiting to be uploaded.</summary>
    public int InFlight => Volatile.Read(ref _building) + _built.Count;

    /// <summary>Chunks uploaded since the start.</summary>
    public long Applied { get; private set; }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (Viewer is null)
        {
            return;
        }

        _planner.Update(ViewerPosition, _changes);
        foreach (ChunkChange change in _changes)
        {
            int generation = _generation.TryGetValue(change.Chunk, out int g) ? g + 1 : 1;
            _generation[change.Chunk] = generation;
            if (change.To >= StreamRing.Medium)
            {
                StartBuild(change.Chunk, change.To, generation);
            }
            else
            {
                Release(change.Chunk);
            }
        }

        long start = Stopwatch.GetTimestamp();
        int applied = 0;
        while (applied < ApplyPerFrame && _built.TryDequeue(out BuiltChunk chunk))
        {
            if (_generation.TryGetValue(chunk.Chunk, out int current) && current == chunk.Generation)
            {
                Apply(chunk);
                applied++;
            }
        }

        AppliedLastFrame = applied;
        Applied += applied;
        ApplyMsLastFrame = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void StartBuild(ChunkCoord chunk, StreamRing ring, int generation)
    {
        Interlocked.Increment(ref _building);
        Task.Run(() =>
        {
            try
            {
                _built.Enqueue(Build(chunk, ring, generation));
            }
            finally
            {
                Interlocked.Decrement(ref _building);
            }
        });
    }

    private BuiltChunk Build(ChunkCoord chunk, StreamRing ring, int generation)
    {
        var props = new ScatteredProp[ChunkScatter.Count];
        ChunkScatter.Generate(_seed, chunk, props);
        int count = ring == StreamRing.Full ? ChunkScatter.Count : ChunkScatter.MediumCount;
        var buffers = new float[3][];
        var counts = new int[3];
        foreach (ScatteredProp prop in props.AsSpan(0, count))
        {
            counts[(int)prop.Kind]++;
        }

        for (int kind = 0; kind < 3; kind++)
        {
            buffers[kind] = new float[counts[kind] * FloatsPerTransform];
            counts[kind] = 0;
        }

        float originX = chunk.X * ChunkCoord.Size;
        float originZ = chunk.Z * ChunkCoord.Size;
        foreach (ScatteredProp prop in props.AsSpan(0, count))
        {
            float x = originX + prop.X;
            float z = originZ + prop.Z;
            var basis = new Basis(Vector3.Up, prop.Yaw).Scaled(Vector3.One * prop.Scale);
            var origin = new Vector3(x, _field.HeightAt(x, z), z);
            Write(buffers[(int)prop.Kind], counts[(int)prop.Kind]++, new Transform3D(basis, origin));
        }

        return new BuiltChunk(chunk, ring, generation, buffers);
    }

    private static void Write(float[] buffer, int index, Transform3D t)
    {
        // MultiMesh buffer layout: a 3×4 row-major matrix per instance.
        int o = index * FloatsPerTransform;
        (buffer[o], buffer[o + 1], buffer[o + 2], buffer[o + 3]) = (t.Basis.X.X, t.Basis.Y.X, t.Basis.Z.X, t.Origin.X);
        (buffer[o + 4], buffer[o + 5], buffer[o + 6], buffer[o + 7]) = (t.Basis.X.Y, t.Basis.Y.Y, t.Basis.Z.Y, t.Origin.Y);
        (buffer[o + 8], buffer[o + 9], buffer[o + 10], buffer[o + 11]) = (t.Basis.X.Z, t.Basis.Y.Z, t.Basis.Z.Z, t.Origin.Z);
    }

    private void Apply(BuiltChunk chunk)
    {
        if (!_views.TryGetValue(chunk.Chunk, out ChunkView? view))
        {
            view = _pool.Count > 0 ? _pool.Pop() : ChunkView.Create(this);
            _views[chunk.Chunk] = view;
        }

        bool full = chunk.Ring == StreamRing.Full;
        view.Set(PropKind.Tree, full ? _meshes.Tree : _meshes.TreeLow, chunk.Buffers[(int)PropKind.Tree]);
        view.Set(PropKind.Rock, full ? _meshes.Rock : _meshes.RockLow, chunk.Buffers[(int)PropKind.Rock]);
        view.Set(PropKind.Bush, _meshes.Bush, chunk.Buffers[(int)PropKind.Bush]);
    }

    private void Release(ChunkCoord chunk)
    {
        if (_views.Remove(chunk, out ChunkView? view))
        {
            view.Hide();
            _pool.Push(view);
        }
    }

    private readonly record struct BuiltChunk(ChunkCoord Chunk, StreamRing Ring, int Generation, float[][] Buffers);

    /// <summary>The three MultiMesh instances that show one chunk's props.</summary>
    private sealed class ChunkView
    {
        private readonly MultiMeshInstance3D[] _instances;

        private ChunkView(MultiMeshInstance3D[] instances)
        {
            _instances = instances;
        }

        public static ChunkView Create(Node parent)
        {
            var instances = new MultiMeshInstance3D[3];
            for (int i = 0; i < instances.Length; i++)
            {
                instances[i] = new MultiMeshInstance3D { Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D } };
                parent.AddChild(instances[i]);
            }

            return new ChunkView(instances);
        }

        public void Set(PropKind kind, Mesh mesh, float[] buffer)
        {
            MultiMeshInstance3D instance = _instances[(int)kind];
            MultiMesh multimesh = instance.Multimesh;
            multimesh.InstanceCount = 0;
            multimesh.Mesh = mesh;
            multimesh.InstanceCount = buffer.Length / FloatsPerTransform;
            if (buffer.Length > 0)
            {
                multimesh.Buffer = buffer;
            }

            instance.Visible = buffer.Length > 0;
        }

        public void Hide()
        {
            foreach (MultiMeshInstance3D instance in _instances)
            {
                instance.Visible = false;
                instance.Multimesh.InstanceCount = 0;
            }
        }
    }

    /// <summary>Placeholder prop meshes (style guide greybox colours) in full and low detail.</summary>
    private sealed record PropMeshes(Mesh Tree, Mesh TreeLow, Mesh Rock, Mesh RockLow, Mesh Bush)
    {
        public static PropMeshes Create()
        {
            var leaves = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.32f, 0.18f) };
            var stone = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.44f, 0.42f) };
            var bush = new StandardMaterial3D { AlbedoColor = new Color(0.24f, 0.38f, 0.17f) };
            return new PropMeshes(
                Cone(16, 8, leaves),
                Cone(5, 1, leaves),
                new SphereMesh { Radius = 0.8f, Height = 1.0f, RadialSegments = 16, Rings = 8, Material = stone },
                new SphereMesh { Radius = 0.8f, Height = 1.0f, RadialSegments = 6, Rings = 3, Material = stone },
                new SphereMesh { Radius = 0.6f, Height = 0.8f, RadialSegments = 8, Rings = 4, Material = bush });
        }

        private static ArrayMesh Cone(int segments, int rings, Material material)
        {
            // A cone standing on the ground: CylinderMesh is centred, so move its vertices up by half its height.
            var cone = new CylinderMesh { TopRadius = 0, BottomRadius = 1.6f, Height = 7, RadialSegments = segments, Rings = rings };
            var tool = new SurfaceTool();
            tool.CreateFrom(cone, 0);
            var mesh = new ArrayMesh();
            tool.SetMaterial(material);
            Godot.Collections.Array arrays = tool.CommitToArrays();
            Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] += new Vector3(0, 3.5f, 0);
            }

            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(0, material);
            return mesh;
        }
    }
}
