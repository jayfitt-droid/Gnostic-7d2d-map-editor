using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MapEditor.Core.Generate;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

/// <summary>
/// Users keep project files from every release, so each release must open them all. SaveSamples holds a project (and
/// a Generate preset) saved by each release's own code (tools/save-samples). Never change or remake a sample: add one
/// for each new release instead.
/// </summary>
public sealed class SaveCompatibilityTests : IDisposable
{
    public static TheoryData<string> Samples => ["v1.2.0", "v1.2.1", "v2.0.0-beta.1", "v2.0.0-beta.3", "v2.0.0-beta.4", "v2.0.0", "v2.1.0", "v2.2.0-rc.1", "v2.2.0-rc.2", "v2.2.0-rc.3"];

    public static TheoryData<string> PresetSamples => ["v2.0.0-beta.1", "v2.0.0-beta.3", "v2.0.0-beta.4", "v2.0.0", "v2.1.0", "v2.2.0-rc.1", "v2.2.0-rc.2", "v2.2.0-rc.3"];

    /// <summary>
    /// Saved values that a migration changes on purpose, by sample and JSON path. Add a path here only together with
    /// the migration in ProjectStore.Load and a test of what it turns the old value into.
    /// </summary>
    private static readonly Dictionary<string, string[]> Migrated = new()
    {
        // Format 1 → 2: the y_is_groundlevel attribute became PrefabInstance.GroundLevel.
        ["v1.2.0"] = ["$.Format", "$.Prefabs[1].Extra"],
        // Format 2 → 3: saving again writes the current format number (and the bridge list, which old files lack).
        ["v1.2.1"] = ["$.Format"],
        ["v2.0.0-beta.1"] = ["$.Format"],
        ["v2.0.0-beta.3"] = ["$.Format"],
        ["v2.0.0-beta.4"] = ["$.Format"],
        ["v2.0.0"] = ["$.Format"],
    };

    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-compat-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string SamplePath(string name) => Path.Combine(AppContext.BaseDirectory, "SaveSamples", name);

    [Theory]
    [MemberData(nameof(Samples))]
    public void Old_project_loads_with_its_world(string release)
    {
        var world = ProjectStore.Load(SamplePath(release + ProjectStore.Extension), out var settings);

        Assert.Equal("Sample", world.Name);
        Assert.Equal(256, world.Size);
        Assert.Equal((ushort)(10000 + 7 * 3 + 9 * 5), world.Height[7, 9]);
        Assert.Equal((ushort)(10000 + 255 * 3 + 255 * 5), world.Height[255, 255]);
        Assert.Equal([0x004000u, 0xFFE477u, 0xBA00FFu], world.Biomes!.Palette);
        Assert.Equal(1, world.Biomes.Indices[3, 4]);
        Assert.Equal(2, world.Biomes.Indices[5, 6]);
        Assert.Equal((byte)RoadType.Gravel, world.Roads![5, 6]);
        Assert.Equal((byte)RoadType.Asphalt, world.Roads[7, 6]);
        Assert.Equal(30, world.Water![7, 8]);
        Assert.Equal(255, world.Radiation![1, 1]);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], world.PassthroughImages[WorldFiles.Water].Pixels);
        Assert.Equal([1, 2, 3], world.ExtraFiles[WorldFiles.Level]);
        Assert.Equal("sample", WorldXml.GetProperty(world.MapInfo, "Description"));
        Assert.Equal(@"C:\out\Sample", settings.ExportFolder);
        // The Bridge tool's list came in format 3: every older project loads with none.
        Assert.NotNull(settings.Bridges);
        Assert.Empty(settings.Bridges);

        var house = world.Prefabs[0];
        Assert.Equal(("house_old_01", "model", new Int3(-10, 37, 20), 3, false), (house.Name, house.Type, house.Position, house.Rotation, house.GroundLevel));
        Assert.Equal(new KeyValuePair<string, string>("help", "x"), Assert.Single(house.ExtraAttributes));

        // A ground-level prefab keeps Y as the game's editor wrote it; only v1.2.1 and later know its offset.
        var gun = world.Prefabs[1];
        Assert.Equal(("store_gun_01", new Int3(40, 2, -30), 1, true), (gun.Name, gun.Position, gun.Rotation, gun.GroundLevel));
        Assert.Empty(gun.ExtraAttributes);
        if (release == "v1.2.0")
        {
            Assert.Null(gun.GroundLevelOffset);
            Assert.Equal(2, world.Prefabs.Count);
        }
        else
        {
            Assert.Equal(-4, gun.GroundLevelOffset);
            var book = world.Prefabs[2];
            Assert.Equal(("store_book_01", true, (int?)null), (book.Name, book.GroundLevel, book.GroundLevelOffset));
        }

        Assert.Equal(2, world.Spawns.Count);
        Assert.Equal((1.5f, 40f, -7f, 90f), (world.Spawns[0].X, world.Spawns[0].Y, world.Spawns[0].Z, world.Spawns[0].RotationY));
        Assert.Equal((-20.25f, 41f, 33f, 270f), (world.Spawns[1].X, world.Spawns[1].Y, world.Spawns[1].Z, world.Spawns[1].RotationY));

        if (release.StartsWith("v1.")) Assert.Null(settings.Generator);
        else Assert.NotNull(settings.Generator);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Old_project_keeps_every_saved_value(string release)
    {
        string old = SamplePath(release + ProjectStore.Extension);
        string resaved = Path.Combine(_dir, release + ProjectStore.Extension);
        var world = ProjectStore.Load(old, out var settings);
        ProjectStore.Save(world, resaved, settings);

        var problems = new List<string>();
        using (var before = ReadManifest(old))
        using (var after = ReadManifest(resaved))
            Compare(before.RootElement, after.RootElement, "$", null, Migrated.GetValueOrDefault(release) ?? [], problems);
        Assert.True(problems.Count == 0,
            $"Saving a {release} project again loses or changes what it held:\n" + string.Join("\n", problems));
    }

    [Theory]
    [MemberData(nameof(PresetSamples))]
    public void Old_generate_preset_keeps_every_saved_value(string release)
    {
        // AppSettings reads and writes presets with the default options, as here.
        string json = File.ReadAllText(SamplePath(release + "-preset.json"));
        var preset = JsonSerializer.Deserialize<GeneratorPreset>(json)!;
        Assert.Equal("Sample preset", preset.Name);

        var problems = new List<string>();
        using (var before = JsonDocument.Parse(json))
        using (var after = JsonDocument.Parse(JsonSerializer.Serialize(preset)))
            Compare(before.RootElement, after.RootElement, "$", typeof(GeneratorPreset), [], problems);
        Assert.True(problems.Count == 0,
            $"Saving a {release} Generate preset again loses or changes what it held:\n" + string.Join("\n", problems));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Old_projects_with_generator_settings_load_with_deck_wrecks_on(string release)
    {
        // The option came after every release sample, so none of them holds it: the default is on, as for a new project.
        ProjectStore.Load(SamplePath(release + ProjectStore.Extension), out var settings);
        if (settings.Generator is { } g) Assert.True(g.Highways.DeckWrecks);
        Assert.True(new HighwayOptions().DeckWrecks);
    }

    [Fact]
    public void The_deck_wrecks_option_is_saved_and_read_back_either_way()
    {
        foreach (bool on in new[] { true, false })
        {
            var json = JsonSerializer.Serialize(new HighwayOptions { DeckWrecks = on });
            Assert.Equal(on, JsonSerializer.Deserialize<HighwayOptions>(json)!.DeckWrecks);
        }
        Assert.True(JsonSerializer.Deserialize<HighwayOptions>("{\"Seed\":3,\"Bridges\":true}")!.DeckWrecks);
    }

    [Fact]
    public void Beta3_generator_settings_load_with_their_meaning()
    {
        ProjectStore.Load(SamplePath("v2.0.0-beta.3" + ProjectStore.Extension), out var settings);
        var g = settings.Generator!;
        // tools/save-samples fills each enum with its last member.
        Assert.Equal(LandShapeKind.Peninsula, g.Terrain.Shape.Kind);
        Assert.Equal(MapEdge.West, g.Terrain.Shape.PeninsulaEdge);
        Assert.Equal([EdgeStyle.Water, EdgeStyle.Water], g.Terrain.Edges);
        Assert.Equal([TerrainType.Mountains, TerrainType.Mountains], g.PaintedTerrain);
        Assert.Equal(BiomeLayoutKind.Line, g.Biomes!.Layout);
        Assert.Equal([GeneratorStage.Spawns], g.Done);
        Assert.Equal(HighwayKind.Bridge, g.PlacedRoads[0].Kind);
        Assert.Equal(FeatureKind.Crater, g.PlacedFeatures[0].Kind);
        Assert.Single(g.Sites);
        Assert.Single(g.PlacedWilderness);
        Assert.Single(g.PlacedSpawns);
    }

    // ---- Enum values ----

    /// <summary>
    /// Every enum member a save can hold, with the value or name it is saved as. A file stores some enums as numbers
    /// and some as names (JsonStringEnumConverter), so moving, renaming or removing a member changes what old files
    /// mean. Add new members at the end, and add them here.
    /// </summary>
    private static readonly Dictionary<Type, string> SavedEnums = new()
    {
        [typeof(BiomeLayoutKind)] = "CenterForest=0 CenterWasteland=1 Circle=2 Circle2=3 Line=4",
        [typeof(EdgeStyle)] = "None=0 Land=1 Water=2",
        [typeof(FeatureKind)] = "Lake=0 River=1 Canyon=2 Crater=3",
        [typeof(GeneratorStage)] = "Terrain=0 TownSites=1 Features=2 Highways=3 Wilderness=4 Spawns=5",
        [typeof(HighwayKind)] = "Highway=0 Ring=1 Country=2 JoinUp=3 Bridge=4",
        [typeof(LandShapeKind)] = "Edges=0 Island=1 Archipelago=2 Peninsula=3",
        [typeof(MapEdge)] = "North=0 East=1 South=2 West=3",
        [typeof(RoadType)] = "None=0 Gravel=1 Asphalt=2 Water=3",
        [typeof(RwgBiome)] = "Forest=0 BurntForest=1 Desert=2 Snow=3 Wasteland=4",
        [typeof(TerrainType)] = "None=0 Plains=1 Hills=2 Mountains=3",
        [typeof(TraderMode)] = "RwgCaps=0 OnePerTown=1",
    };

    [Fact]
    public void Saved_enum_members_keep_their_values()
    {
        var problems = new List<string>();
        foreach (var type in ReachableEnums())
            if (!SavedEnums.ContainsKey(type))
                problems.Add($"{type.Name} is saved but not listed in SavedEnums: add \"{Describe(type)}\".");
        foreach (var (type, pinned) in SavedEnums)
        {
            var now = Describe(type).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            foreach (var member in pinned.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (!now.Contains(member))
                    problems.Add($"{type.Name}: {member} is gone or has another value. Old saves hold it. Now: {Describe(type)}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static string Describe(Type type) =>
        string.Join(' ', Enum.GetValues(type).Cast<object>().Select(v => $"{v}={Convert.ToInt64(v)}"));

    /// <summary>Enum types in any property that project.json or a preset stores.</summary>
    private static HashSet<Type> ReachableEnums()
    {
        var found = new HashSet<Type> { typeof(RoadType) }; // the road layer holds RoadType values
        var seen = new HashSet<Type>();
        void Walk(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            if (t.IsEnum) { found.Add(t); return; }
            if (t.IsPrimitive || t == typeof(string) || !seen.Add(t)) return;
            if (t.IsArray) { Walk(t.GetElementType()!); return; }
            if (t.IsGenericType) foreach (var a in t.GetGenericArguments()) Walk(a);
            if (t.Namespace?.StartsWith("MapEditor") != true) return;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.SetMethod?.IsPublic == true && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
                    Walk(p.PropertyType);
        }
        Walk(typeof(ProjectStore.ProjectSettings));
        Walk(typeof(GeneratorPreset));
        return found;
    }

    // ---- JSON comparison ----

    private static readonly JsonSerializerOptions Contract = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    private static JsonDocument ReadManifest(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var s = zip.GetEntry("project.json")!.Open();
        return JsonDocument.Parse(s);
    }

    /// <summary>
    /// Checks that everything the old file held is in the new one with the same value. Below a known type, a property
    /// with no setter is skipped: it is worked out from the others and never read back. Above it (project.json's own
    /// records) every value counts.
    /// </summary>
    private static void Compare(JsonElement old, JsonElement now, string path, Type? type, string[] migrated, List<string> problems)
    {
        if (migrated.Contains(path)) return;
        if (path == "$.Settings") type = typeof(ProjectStore.ProjectSettings);
        type = type is null ? null : Nullable.GetUnderlyingType(type) ?? type;
        var info = type is null ? null : Contract.GetTypeInfo(type);

        if (old.ValueKind != now.ValueKind)
        {
            problems.Add($"{path}: was {old.GetRawText()}, now {Short(now)}");
            return;
        }
        switch (old.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in old.EnumerateObject())
                {
                    Type? child = null;
                    if (info?.Kind == JsonTypeInfoKind.Object)
                    {
                        var p = info.Properties.FirstOrDefault(p => p.Name == prop.Name);
                        if (p is null) { problems.Add($"{path}.{prop.Name}: the program no longer has this setting"); continue; }
                        if (p.Set is null) continue;
                        child = p.PropertyType;
                    }
                    else if (info?.Kind == JsonTypeInfoKind.Dictionary)
                        child = info.ElementType;
                    if (!now.TryGetProperty(prop.Name, out var value))
                        problems.Add($"{path}.{prop.Name}: missing after saving again");
                    else
                        Compare(prop.Value, value, $"{path}.{prop.Name}", child, migrated, problems);
                }
                break;
            case JsonValueKind.Array:
                int count = now.GetArrayLength();
                if (old.GetArrayLength() != count)
                {
                    problems.Add($"{path}: had {old.GetArrayLength()} items, now {count}");
                    break;
                }
                var element = info?.Kind == JsonTypeInfoKind.Enumerable ? info.ElementType : null;
                for (int i = 0; i < count; i++)
                    Compare(old[i], now[i], $"{path}[{i}]", element, migrated, problems);
                break;
            default:
                if (old.GetRawText() != now.GetRawText())
                    problems.Add($"{path}: was {old.GetRawText()}, now {Short(now)}");
                break;
        }
    }

    private static string Short(JsonElement e)
    {
        string text = e.GetRawText();
        return text.Length <= 80 ? text : text[..80] + "…";
    }
}
