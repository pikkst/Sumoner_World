using Godot;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Core.World;

namespace SumonerWorld.Game.World;

/// <summary>
/// Puts a <see cref="Heightfield"/> on screen (TDD §5.1). With the Terrain3D extension installed (<c>scripts/fetch-terrain3d.sh</c>)
/// it creates a <c>Terrain3D</c> node: GPU clipmap LODs, its own collision near the camera, one grass-coloured texture. Without it, the
/// in-house fallback builds one mesh per 64 m chunk at 2 m spacing (no LODs), so the region still runs, for example in CI. Terrain3D
/// is a GDExtension without C# bindings, so it is driven through <c>GodotObject.Call</c> and <c>GodotObject.Set</c>. The ground's
/// colour comes from the region's biomes (E4b): a colour map for Terrain3D over a pale texture, vertex colours for the fallback.
/// </summary>
public static class TerrainBuilder
{
    /// <summary>Terrain3D's class name.</summary>
    public const string Terrain3DClass = "Terrain3D";

    private const int RegionSize = 256;
    private const int FallbackStep = 2;
    private const int TextureSize = 256;
    private const string DataDirectory = "user://terrain3d";
    private static readonly Color PlainGround = new(0.28f, 0.42f, 0.19f);

    /// <summary>True when the Terrain3D extension is loaded.</summary>
    public static bool HasTerrain3D => ClassDB.ClassExists(Terrain3DClass);

    /// <summary>Adds the terrain under <paramref name="parent"/> and returns it.</summary>
    public static Node3D Build(Node parent, Heightfield field, Camera3D camera, ILog log, Core.Data.Definitions.RegionDefinition? region = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(log);
        Node3D terrain = HasTerrain3D ? BuildTerrain3D(parent, field, camera, region) : BuildFallback(parent, field, region);
        log.Write(LogLevel.Info, "terrain.built", $"{(HasTerrain3D ? "Terrain3D" : "in-house fallback (Terrain3D not installed)")}: {field.Size} m, {field.Samples}² samples");
        return terrain;
    }

    /// <summary>The ground colour at a point: the biomes' colours by weight, the plain M1 green outside them.</summary>
    public static Color GroundAt(Core.Data.Definitions.RegionDefinition? region, float x, float z)
    {
        if (region is null)
        {
            return PlainGround;
        }

        float total = 0;
        Vector3 colour = Vector3.Zero;
        foreach (Core.Data.Definitions.BiomeDefinition biome in region.Biomes)
        {
            float weight = RegionGround.Weight(biome, x, z);
            Color tint = Color.FromHtml(biome.Ground);
            total += weight;
            colour += weight * new Vector3(tint.R, tint.G, tint.B);
        }

        float rest = MathF.Max(0, 1 - total);
        colour = (colour + (rest * new Vector3(PlainGround.R, PlainGround.G, PlainGround.B))) / (total + rest);
        return new Color(colour.X, colour.Y, colour.Z);
    }

    private static Node3D BuildTerrain3D(Node parent, Heightfield field, Camera3D camera, Core.Data.Definitions.RegionDefinition? region)
    {
        var terrain = ClassDB.Instantiate(Terrain3DClass).As<Node3D>();
        terrain.Name = "Terrain3D";
        terrain.Set("region_size", RegionSize);
        terrain.Set("vertex_spacing", field.Spacing);
        // Generated, never saved: an empty user:// folder stops Terrain3D looking for region files in res://.
        DirAccess.MakeDirRecursiveAbsolute(DataDirectory);
        terrain.Set("data_directory", DataDirectory);
        parent.AddChild(terrain);
        terrain.Call("set_camera", camera);
        terrain.Get("assets").AsGodotObject().Call("set_texture", 0, GrassTexture());

        var heights = new byte[field.Samples * field.Samples * sizeof(float)];
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(field.Heights).CopyTo(heights);
        Image height = Image.CreateFromData(field.Samples, field.Samples, false, Image.Format.Rf, heights);

        // n samples span n − 1 metres; the last row and column would start another Terrain3D region.
        height.Crop(field.Samples - 1, field.Samples - 1);
        var images = new Godot.Collections.Array<Image> { height, null!, ColourMap(field, region) };
        GodotObject data = terrain.Get("data").AsGodotObject();
        data.Call("import_images", images, Vector3.Zero, 0f, 1f);
        return terrain;
    }

    private static GodotObject GrassTexture()
    {
        var noise = new FastNoiseLite { Seed = 7, Frequency = 0.08f };
        Image image = Image.CreateEmpty(TextureSize, TextureSize, false, Image.Format.Rgba8);
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float n = (noise.GetNoise2D(x, y) + 1) * 0.5f;
                // Pale and grey: the colour map gives the ground its colour.
                float pale = 0.82f + (0.18f * n);
                image.SetPixel(x, y, new Color(pale, pale, pale, 0.5f));
            }
        }

        image.GenerateMipmaps();
        GodotObject texture = ClassDB.Instantiate("Terrain3DTextureAsset").AsGodotObject();
        texture.Set("name", "grass");
        texture.Set("albedo_texture", ImageTexture.CreateFromImage(image));
        return texture;
    }

    // One colour per metre (RGBA8, alpha = neutral roughness), built as raw bytes: millions of SetPixel calls would take seconds.
    private static Image ColourMap(Heightfield field, Core.Data.Definitions.RegionDefinition? region)
    {
        int size = field.Samples - 1;
        var bytes = new byte[size * size * 4];
        System.Threading.Tasks.Parallel.For(0, size, z =>
        {
            for (int x = 0; x < size; x++)
            {
                Color colour = GroundAt(region, x, z);
                int at = ((z * size) + x) * 4;
                (bytes[at], bytes[at + 1], bytes[at + 2], bytes[at + 3]) = ((byte)(colour.R * 255), (byte)(colour.G * 255), (byte)(colour.B * 255), 128);
            }
        });
        return Image.CreateFromData(size, size, false, Image.Format.Rgba8, bytes);
    }

    private static Node3D BuildFallback(Node parent, Heightfield field, Core.Data.Definitions.RegionDefinition? region)
    {
        var root = new Node3D { Name = "FallbackTerrain" };
        parent.AddChild(root);
        var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        int chunks = (int)(field.Size / ChunkCoord.Size);
        int perSide = (int)(ChunkCoord.Size / FallbackStep) + 1;
        for (int cz = 0; cz < chunks; cz++)
        {
            for (int cx = 0; cx < chunks; cx++)
            {
                var tool = new SurfaceTool();
                tool.Begin(Mesh.PrimitiveType.Triangles);
                float x0 = cx * ChunkCoord.Size;
                float z0 = cz * ChunkCoord.Size;
                for (int j = 0; j < perSide; j++)
                {
                    for (int i = 0; i < perSide; i++)
                    {
                        float x = x0 + (i * FallbackStep);
                        float z = z0 + (j * FallbackStep);
                        tool.SetColor(GroundAt(region, x, z));
                        tool.AddVertex(new Vector3(x, field.HeightAt(x, z), z));
                    }
                }

                for (int j = 0; j < perSide - 1; j++)
                {
                    for (int i = 0; i < perSide - 1; i++)
                    {
                        int a = (j * perSide) + i;
                        tool.AddIndex(a);
                        tool.AddIndex(a + 1);
                        tool.AddIndex(a + perSide);
                        tool.AddIndex(a + 1);
                        tool.AddIndex(a + perSide + 1);
                        tool.AddIndex(a + perSide);
                    }
                }

                tool.GenerateNormals();
                root.AddChild(new MeshInstance3D { Mesh = tool.Commit(), MaterialOverride = material });
            }
        }

        return root;
    }
}
