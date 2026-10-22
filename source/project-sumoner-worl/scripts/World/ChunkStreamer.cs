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
/// shows low-poly trees and rocks; far and none show nothing beyond the terrain (impostors come with real art). With a region
/// (E4b) each prop takes the look of the biome it stands in: trees and bushes are thinned out where plants are sparse (the
/// highlands, the ash), grow bigger where they are dense (the Elderwood) and take the biome's plant colour.
/// </summary>
public sealed partial class ChunkStreamer : Node3D
{
    /// <summary>Finished chunks uploaded per frame.</summary>
    public const int ApplyPerFrame = 2;

    private const int FloatsPerTransform = 12;
    private const int FloatsPerInstance = FloatsPerTransform + 4;
    private static readonly Color Stone = new(0.45f, 0.44f, 0.42f);

    private readonly StreamingPlanner _planner;
    private readonly Heightfield _field;
    private readonly ulong _seed;
    private readonly Core.Data.Definitions.RegionDefinition? _region;
    private readonly List<ChunkChange> _changes = new(64);
    private readonly Dictionary<ChunkCoord, ChunkView> _views = [];
    private readonly Dictionary<ChunkCoord, int> _generation = [];
    private readonly Stack<ChunkView> _pool = new();
    private readonly ConcurrentQueue<BuiltChunk> _built = new();
    private readonly PropMeshes _meshes = PropMeshes.Create();
    private int _building;

    /// <summary>Creates the streamer for a region (<paramref name="region"/>: its biomes, or null for the plain M1 look).</summary>
    public ChunkStreamer(StreamingSettings settings, Heightfield field, ulong seed, Core.Data.Definitions.RegionDefinition? region = null)
    {
        _planner = new StreamingPlanner(settings);
        _field = field;
        _seed = seed;
        _region = region;
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
        float originX = chunk.X * ChunkCoord.Size;
        float originZ = chunk.Z * ChunkCoord.Size;
        var kept = new List<(ScatteredProp Prop, float Scale, Color Colour)>(count);
        for (int i = 0; i < count; i++)
        {
            ScatteredProp prop = props[i];
            (float plants, Color colour) = PlantsAt(originX + prop.X, originZ + prop.Z);
            if (prop.Kind == PropKind.Rock)
            {
                kept.Add((prop, prop.Scale, Stone));
            }
            else if (Keep(chunk, i) < plants)
            {
                // Dense biomes grow bigger plants (the scatter has a fixed count per chunk).
                Color shade = prop.Kind == PropKind.Bush ? colour.Lightened(0.15f) : colour;
                kept.Add((prop, prop.Scale * (1 + (MathF.Max(0, plants - 1) * 0.5f)), shade));
            }
        }

        var buffers = new float[3][];
        var counts = new int[3];
        foreach ((ScatteredProp prop, float _, Color _) in kept)
        {
            counts[(int)prop.Kind]++;
        }

        for (int kind = 0; kind < 3; kind++)
        {
            buffers[kind] = new float[counts[kind] * FloatsPerInstance];
            counts[kind] = 0;
        }

        foreach ((ScatteredProp prop, float scale, Color colour) in kept)
        {
            float x = originX + prop.X;
            float z = originZ + prop.Z;
            var basis = new Basis(Vector3.Up, prop.Yaw).Scaled(Vector3.One * scale);
            var origin = new Vector3(x, _field.HeightAt(x, z), z);
            Write(buffers[(int)prop.Kind], counts[(int)prop.Kind]++, new Transform3D(basis, origin), colour);
        }

        return new BuiltChunk(chunk, ring, generation, buffers);
    }

    // The plant cover and plant colour where a prop stands: the biomes' weighted look, the plain M1 look outside them.
    private (float Plants, Color Colour) PlantsAt(float x, float z)
    {
        var plain = new Color(0.16f, 0.32f, 0.18f);
        if (_region is null)
        {
            return (1, plain);
        }

        float total = 0;
        float plants = 0;
        Vector3 colour = Vector3.Zero;
        foreach (Core.Data.Definitions.BiomeDefinition biome in _region.Biomes)
        {
            float weight = RegionGround.Weight(biome, x, z);
            Color tint = Color.FromHtml(biome.PlantColour);
            total += weight;
            plants += weight * biome.Plants;
            colour += weight * new Vector3(tint.R, tint.G, tint.B);
        }

        float rest = MathF.Max(0, 1 - total);
        float sum = total + rest;
        colour = (colour + (rest * new Vector3(plain.R, plain.G, plain.B))) / sum;
        return ((plants + rest) / sum, new Color(colour.X, colour.Y, colour.Z));
    }

    // A stable 0–1 value per prop, so the same props stay when a chunk is built again.
    private float Keep(ChunkCoord chunk, int index)
    {
        ulong hash = (_seed ^ ((ulong)(uint)chunk.X * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)chunk.Z * 0xC2B2AE3D27D4EB4FUL)) + ((ulong)index * 0x165667B19E3779F9UL);
        hash ^= hash >> 29;
        hash *= 0xBF58476D1CE4E5B9UL;
        hash ^= hash >> 32;
        return (hash & 0xFFFFFF) / (float)0x1000000;
    }

    private static void Write(float[] buffer, int index, Transform3D t, Color colour)
    {
        // MultiMesh buffer layout: a 3×4 row-major matrix per instance, then its colour.
        int o = index * FloatsPerInstance;
        (buffer[o + 12], buffer[o + 13], buffer[o + 14], buffer[o + 15]) = (colour.R, colour.G, colour.B, 1);
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
                instances[i] = new MultiMeshInstance3D { Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true } };
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
            multimesh.InstanceCount = buffer.Length / FloatsPerInstance;
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
            // White albedo: each instance's colour (the biome's plants, grey stone) is the colour.
            // The colours are written in sRGB, like every other colour in the game.
            var leaves = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true };
            var stone = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true };
            var bush = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true };
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
