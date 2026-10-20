using Godot;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.World;

namespace SumonerWorld.Game.World;

/// <summary>
/// The M1 test region (M1-19): a seeded heightfield of <c>chunks</c> × <c>chunks</c> 64 m chunks (default 8 × 8 = 512 m; 64 × 64 =
/// 4 km for spike 4), the terrain, streamed placeholder props, a sun with shadows, sky and distance fog that hides the far ring edge
/// (style guide lighting test values). A free-fly camera (right mouse + WASD, Shift = 3× speed) unless a benchmark drives it.
/// </summary>
public sealed partial class TestRegion : Node3D
{

    private readonly int _chunks;
    private readonly ILog _log;

    /// <summary>Creates the region.</summary>
    public TestRegion(int chunks, ILog log)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunks);
        _chunks = chunks;
        _log = log;
        Name = "TestRegion";
    }

    /// <summary>The ground.</summary>
    public Heightfield? Field { get; private set; }

    /// <summary>The camera everything streams around.</summary>
    public Camera3D? Camera { get; private set; }

    /// <summary>The prop streamer.</summary>
    public ChunkStreamer? Streamer { get; private set; }

    /// <summary>When false, the camera does not read input (a benchmark moves it).</summary>
    public bool FreeFly { get; set; } = true;

    /// <inheritdoc />
    public override void _Ready()
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        Field = _chunks == DevRegion.Chunks ? DevWorld.Ground : DevRegion.CreateGround(_chunks);
        AddEnvironment();
        Camera = new FlyCamera { Name = "Camera", Far = 2000, Fov = 70, Region = this };
        AddChild(Camera);
        float centre = Field.Size / 2;
        Camera.Position = new Vector3(centre, Field.HeightAt(centre, centre) + 2, centre);
        TerrainBuilder.Build(this, Field, Camera, _log);
        Streamer = new ChunkStreamer(new StreamingSettings { RegionChunks = _chunks }, Field, DevRegion.Seed) { Viewer = Camera };
        AddChild(Streamer);
        _log.Write(LogLevel.Info, "region.ready", $"{_chunks}×{_chunks} chunks in {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds:F1} s");
    }

    private void AddEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.35f, 0.5f, 0.72f),
            SkyHorizonColor = new Color(0.72f, 0.76f, 0.78f),
            GroundHorizonColor = new Color(0.72f, 0.76f, 0.78f),
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color(0.72f, 0.76f, 0.78f),
            FogDensity = 0.0025f,
            FogSkyAffect = 0.3f,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-50, -35, 0),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 150,
        });
    }

    /// <summary>Hold the right mouse button to look; WASD moves, Q/E down/up, Shift is 3× faster. Stays above the ground.</summary>
    private sealed partial class FlyCamera : Camera3D
    {
        private const float Speed = 10f;
        private const float MouseSensitivity = 0.003f;

        public TestRegion? Region { get; init; }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (Region?.FreeFly == true && @event is InputEventMouseMotion motion && Input.IsMouseButtonPressed(MouseButton.Right))
            {
                Rotation = new Vector3(Mathf.Clamp(Rotation.X - (motion.Relative.Y * MouseSensitivity), -1.5f, 1.5f), Rotation.Y - (motion.Relative.X * MouseSensitivity), 0);
            }
        }

        public override void _Process(double delta)
        {
            if (Region?.FreeFly != true || Region.Field is null)
            {
                return;
            }

            var move = new Vector3(
                (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0),
                (Input.IsKeyPressed(Key.E) ? 1 : 0) - (Input.IsKeyPressed(Key.Q) ? 1 : 0),
                (Input.IsKeyPressed(Key.S) ? 1 : 0) - (Input.IsKeyPressed(Key.W) ? 1 : 0));
            float speed = Speed * (Input.IsKeyPressed(Key.Shift) ? 3 : 1);
            Vector3 next = Position + (GlobalBasis * move * speed * (float)delta);
            next.Y = Mathf.Max(next.Y, Region.Field.HeightAt(next.X, next.Z) + 1.5f);
            Position = next;
        }
    }
}
