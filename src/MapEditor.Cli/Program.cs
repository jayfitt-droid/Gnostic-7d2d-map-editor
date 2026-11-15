using System.Diagnostics;
using MapEditor.Core.Generate;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Rendering;
using MapEditor.Core.Validate;

try
{
    return args switch
    {
        ["info", var world] => Info(world),
        ["roundtrip", var world, var output, .. var rest] => RoundTrip(world, output, overwrite: rest.Contains("--overwrite")),
        ["installs"] => Installs(),
        ["render", var world, var output, .. var rest] => Render(world, output, rest.Length > 0 ? int.Parse(rest[0]) : -1),
        ["export", var project, var output, .. var rest] => Export(project, output, overwrite: rest.Contains("--overwrite")),
        ["validate", var source, .. var rest] => Validate(source, rest),
        ["compare", var a, var b] => Compare(a, b),
        ["topview", var output, .. var names] when names.Length > 0 => TopView(output, names),
        ["new", var output, .. var rest] => New(output, rest),
        _ => Usage(),
    };
}
// A damaged or unexpected file, or a bad number on the command line: a message and exit code 2, not a stack trace (#20).
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException
    or System.Xml.XmlException or FormatException or OverflowException)
{
    return BadArguments(ex.Message);
}

static int Usage()
{
    Console.Error.WriteLine("""
        mapeditor-cli info <worldFolder>
            Print a summary of a game world.
        mapeditor-cli roundtrip <worldFolder> <outFolder> [--overwrite]
            Load a world, write it back out, re-load the output and compare every layer.
        mapeditor-cli installs
            List detected 7 Days to Die installs.
        mapeditor-cli render <worldFolder> <out.png> [level]
            Render the editor's map view (default: 2 levels below the top) to a PNG.
        mapeditor-cli export <project.7dmap> <outFolder> [--overwrite]
            Write a project as a game world folder.
        mapeditor-cli validate <worldFolder|project.7dmap> [--bridges-only] [--game <gameFolder>]
            List problems: spawns, traders, prefab overlaps, water and bounds, and every bridge (found from the
            prefabs alone): deck ends against the ground, roads reaching them, ramp grades, ground along the deck,
            pillar footings and clearance. Prints a summary line, a table of the bridges, then one line per issue.
            --bridges-only             Print only the bridge issues.
            --game <gameFolder>        The 7 Days to Die folder to read prefabs from (default: the first Steam install found).
            Exit code: 0 no errors, 1 errors found, 2 bad input.
        mapeditor-cli compare <worldFolder|project.7dmap> <worldFolder|project.7dmap>
            Compare every layer, prefab and spawn of two worlds.
        mapeditor-cli topview <out.png> <prefab>[:rotation] [...]
            Draw prefabs from above, as the map's prefab detail shows them, side by side (4 px per block).
        mapeditor-cli new <outFolder> [size] [edges] [options]
            Create a blank world. size = 1024 to 16384, a multiple of 512 (default 4096).
            edges = 4 letters for N,E,S,W: L land, W water, N none (default LLLL).
            --seed <n>                 World seed; the same seed gives the same coast (default: random).
            --shape <kind>             edges (default), island, archipelago or peninsula. A shape puts
                                       ocean on its edges, so it replaces the edges letters.
            --land <10-80>             Share of the map that is land, in percent (default 45).
            --roughness <0-100>        How ragged the coast is (default 50).
            --islands <2-12>           Islands an archipelago starts with (default 6).
            --edge <side>              north, east, south or west: the edge a peninsula joins (default south).
            --biome <name>             Paint the whole map one biome, by its biomes.xml name (default pine_forest).
            --biomes <layout>          Grow every land biome as the game's generator does instead. layout =
                                       centerforest, centerwasteland, circle, circle2 or line.
            --weight <name>=<0-50>     With --biomes: a biome's share, against the others (default: the game's;
                                       pine_forest 13, burnt_forest 18, desert 22, snow 23, wasteland 24).
                                       Repeat for each biome to change; 0 leaves a biome out.
            --generate                 Run the Generate stages after making the world, as the Generate panel
                                       does: terrain, towns, features, highways, wilderness, spawns.
            --stages <list>            With --generate: run the stages up to the last one named, comma-separated,
                                       such as terrain,towns (default: all). Each stage needs the ones before it.
            --preset <name>            With --generate: a built-in preset (default "Game defaults").
                                       --preset list shows them.
            --bridges                  With --generate: join islands with bridges, as "Bridge islands" does.
            --bridge-max <20-4000>     With --bridges: the longest bridge, in metres (default 1500).
            --shortcut-bridges         With --bridges: also bridge islands that are many bridges apart by road.
            --project <file.7dmap>     Also save a project, with the Generate settings, to carry on in the app.
        """);
    return 2;
}

static int New(string output, string[] args)
{
    int size = 4096, seed = Environment.TickCount;
    string edges = "LLLL";
    var shape = new LandShape();
    string? oneBiome = null;
    BiomeLayoutKind? layout = null;
    var weights = new List<(string Name, int Weight)>();
    bool generate = false;
    List<GeneratorStage>? stages = null;
    GeneratorPreset? preset = null;
    string? project = null;
    bool bridges = false, shortcutBridges = false;
    int? bridgeMax = null;
    int positional = 0;
    try
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--"))
            {
                switch (positional++)
                {
                    case 0: size = Number(arg, "size", NewWorldGenerator.MinSize, NewWorldGenerator.MaxSize); break;
                    case 1: edges = arg; break;
                    default: throw new ArgumentException($"Unexpected argument '{arg}'.");
                }
                continue;
            }
            switch (arg)
            {
                case "--generate": generate = true; continue;
                case "--bridges": bridges = true; continue;
                case "--shortcut-bridges": shortcutBridges = true; continue;
            }
            if (i + 1 >= args.Length) throw new ArgumentException($"{arg} needs a value.");
            string value = args[++i];
            switch (arg)
            {
                case "--seed": seed = Number(value, arg, int.MinValue, int.MaxValue); break;
                case "--shape": shape.Kind = Choice<LandShapeKind>(value, arg); break;
                case "--land": shape.LandPercent = Number(value, arg, LandShape.MinLandPercent, LandShape.MaxLandPercent); break;
                case "--roughness": shape.Roughness = Number(value, arg, 0, 100); break;
                case "--islands": shape.Islands = Number(value, arg, LandShape.MinIslands, LandShape.MaxIslands); break;
                case "--edge": shape.PeninsulaEdge = Choice<MapEdge>(value, arg); break;
                case "--biome": oneBiome = value; break;
                case "--biomes": layout = Choice<BiomeLayoutKind>(value, arg); break;
                case "--stages": stages = Stages(value); break;
                case "--bridge-max": bridgeMax = Number(value, arg, 20, 4000); break;
                case "--preset":
                    if (value.Equals("list", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var p in BuiltInPresets.All) Console.WriteLine($"{p.Name,-16} {p.Summary}");
                        return 0;
                    }
                    preset = BuiltInPresets.All.FirstOrDefault(p => p.Name.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
                             ?? throw new ArgumentException($"No preset named '{value}'. Presets: {string.Join(", ", BuiltInPresets.All.Select(p => p.Name))}.");
                    break;
                case "--project":
                    project = value.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase) ? value
                        : throw new ArgumentException($"--project must name a {ProjectStore.Extension} file; '{value}' doesn't.");
                    break;
                case "--weight":
                    int eq = value.LastIndexOf('=');
                    if (eq <= 0) throw new ArgumentException($"--weight must be <biome>=<share>, such as snow=0; '{value}' is not.");
                    weights.Add((value[..eq], Number(value[(eq + 1)..], $"--weight {value[..eq]}", 0, 50)));
                    break;
                default: throw new ArgumentException($"Unknown option {arg}.");
            }
        }
        if (size % 512 != 0) throw new ArgumentException($"size must be a multiple of 512; {size} is not.");
        if (edges.Length != 4 || edges.ToUpperInvariant().Any(c => c is not ('L' or 'W' or 'N')))
            throw new ArgumentException($"edges must be 4 letters, each L, W or N; '{edges}' is not.");
        if (oneBiome is not null && layout is not null) throw new ArgumentException("Use --biome or --biomes, not both.");
        if (weights.Count > 0 && layout is null) throw new ArgumentException("--weight needs --biomes.");
        if (!generate && (stages is not null || preset is not null || bridges))
            throw new ArgumentException("--stages, --preset and --bridges need --generate.");
        if (!bridges && (bridgeMax is not null || shortcutBridges)) throw new ArgumentException("--bridge-max and --shortcut-bridges need --bridges.");
    }
    catch (ArgumentException ex)
    {
        return BadArguments(ex.Message);
    }

    var installPath = GameInstall.FindCandidates().FirstOrDefault();
    var install = installPath is null ? null : GameInstall.Open(installPath);
    if (generate && install is null)
    {
        Console.Error.WriteLine("--generate needs the game's own stamps, prefabs and rwgmixer.xml, and no game install was found.");
        return 1;
    }
    // The game's own biomes.xml, so mod biomes can be named too.
    var biomes = install is null ? BiomeTable.Vanilla : BiomeTable.Load(install);
    BiomeDefinition FindBiome(string name) =>
        biomes.Biomes.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"No biome named '{name}'. Biomes: {string.Join(", ", biomes.Biomes.Select(b => b.Name))}.");
    uint color = BiomeLayout.ForestColor;
    BiomePlan? plan = null;
    try
    {
        if (oneBiome is not null) color = FindBiome(oneBiome).Color;
        if (layout is { } kind)
        {
            var shares = BiomeLayout.DefaultShares(biomes);
            foreach (var (name, weight) in weights)
            {
                var biome = FindBiome(name);
                int at = shares.FindIndex(s => s.Color == biome.Color);
                if (at < 0) throw new ArgumentException($"{biome.Name} can't be grown on land.");
                shares[at] = shares[at] with { Weight = weight };
            }
            if (shares.All(s => s.Weight <= 0)) throw new ArgumentException("Give at least one biome a share of the map.");
            plan = new BiomePlan(kind, shares);
        }
    }
    catch (ArgumentException ex)
    {
        return BadArguments(ex.Message);
    }

    var stamps = install is null ? StampLibrary.Empty : StampLibrary.Load(install);
    var template = LevelFile.FindTemplate(install);
    if (template is null)
    {
        Console.Error.WriteLine("No main.ttw template found; generate any world in the game once first.");
        return 1;
    }

    var styles = edges.ToUpperInvariant().Select(c => c switch { 'W' => EdgeStyle.Water, 'N' => EdgeStyle.None, _ => EdgeStyle.Land }).ToArray();
    // The coast's seed comes from the world seed, as in the New World dialog.
    shape.Seed = GeneratorSettings.LandSeed(seed);
    var sw = Stopwatch.StartNew();
    // A world to generate starts flat, as from the New World dialog: the terrain stage raises it.
    const float GenerateBaseHeight = 35;
    var options = generate
        ? new NewWorldOptions(Path.GetFileName(output), size, GenerateBaseHeight, NoiseAmplitude: 0, Seed: seed, Biome: color,
            Edges: styles, BiomePlan: plan, Shape: shape.IsShaped ? shape : null)
        : new NewWorldOptions(Path.GetFileName(output), size, Seed: seed, Biome: color, Edges: styles, BiomePlan: plan,
            Shape: shape.IsShaped ? shape : null);
    var world = NewWorldGenerator.Create(options, stamps, File.ReadAllBytes(template.Path));
    // The Generate settings, as the app keeps them for a new world.
    var settings = GeneratorSettings.ForNewWorld(seed, styles, options.BaseHeight, shape,
        plan is null ? null : BiomeLayoutSettings.From(plan, seed, shape));
    if (generate)
    {
        var mixer = RwgMixer.Load(install!, size);
        settings.Apply(preset ?? GeneratorPreset.GameDefaults, mixer);
        if (bridges)
        {
            // Bridges are part of the join-up pass, as the app's "Bridge islands" is.
            settings.Highways.JoinUp = true;
            settings.Highways.Bridges = true;
            settings.Highways.BridgeShortcuts = shortcutBridges;
            if (bridgeMax is { } max) settings.Highways.BridgeMax = max;
        }
        Console.WriteLine($"Made the {size}x{size} world in {sw.Elapsed.TotalSeconds:0.0} s. Loading prefabs...");
        var data = new GeneratorData(stamps, mixer, PrefabLibrary.Load(install!), biomes);
        GeneratorStage? running = null;
        GeneratorRunner.Run(world, settings, data, stages ?? GeneratorRunner.Order,
            new SyncProgress<(GeneratorStage Stage, double Done)>(p =>
            {
                if (p.Stage == running) return;
                running = p.Stage;
                Console.Write($"  {GeneratorRunner.Name(p.Stage),-10} ");
            }),
            r => Console.WriteLine($"{r.Time.TotalSeconds,5:0.0} s  {r.Summary}"));
    }
    WorldWriter.Write(world, output);
    if (project is not null)
        ProjectStore.Save(world, project, new ProjectStore.ProjectSettings { ExportFolder = Path.GetFullPath(output), Generator = settings });
    string land = shape.Kind switch
    {
        LandShapeKind.Island => $"island, {shape.LandPercent}% land",
        LandShapeKind.Archipelago => $"archipelago of {shape.Islands} islands, {shape.LandPercent}% land",
        LandShapeKind.Peninsula => $"peninsula on the {shape.PeninsulaEdge.ToString().ToLowerInvariant()} edge, {shape.LandPercent}% land",
        _ => $"edges {edges.ToUpperInvariant()}",
    };
    string biomeText = plan is null
        ? biomes.FindByColor(color)?.Name ?? "one biome"
        : $"{plan.Layout.ToString().ToLowerInvariant()} layout of " + string.Join(", ", plan.Shares.Where(s => s.Weight > 0)
            .Select(s => $"{s.Name} {100.0 * s.Weight / plan.Shares.Sum(t => t.Weight):0}%"));
    Console.WriteLine($"Created {size}x{size} world, seed {seed}, {land}, in {sw.Elapsed.TotalSeconds:0.0} s");
    Console.WriteLine($"Biomes: {biomeText}");
    if (generate)
        Console.WriteLine($"Generated with the \"{(preset ?? GeneratorPreset.GameDefaults).Name}\" preset: "
                          + $"{world.Prefabs.Count:N0} prefabs, {world.Spawns.Count} spawns");
    if (project is not null) Console.WriteLine($"Saved the project to {Path.GetFullPath(project)}");
    Console.WriteLine($"main.ttw from {template.Path} ({template.GameVersion})");
    return 0;
}

static List<GeneratorStage> Stages(string list)
{
    var named = new List<GeneratorStage>();
    foreach (string name in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (name.Equals("all", StringComparison.OrdinalIgnoreCase)) return [.. GeneratorRunner.Order];
        var match = GeneratorRunner.Order.Where(s => GeneratorRunner.Name(s).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (match.Count == 0)
            throw new ArgumentException($"--stages: no stage named '{name}'. Stages: {string.Join(", ", GeneratorRunner.Order.Select(GeneratorRunner.Name))}.");
        named.Add(match[0]);
    }
    if (named.Count == 0) throw new ArgumentException("--stages needs at least one stage.");
    // Each stage builds on the ones before it, so the run goes from terrain up to the last one named.
    var order = GeneratorRunner.Order.ToList();
    return [.. order.Take(named.Max(order.IndexOf) + 1)];
}

static int BadArguments(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine("Run mapeditor-cli with no arguments to see the options.");
    return 2;
}

static int Number(string text, string name, int min, int max) =>
    int.TryParse(text, out int n) && n >= min && n <= max ? n
        : throw new ArgumentException($"{name} must be a whole number from {min} to {max}; '{text}' is not.");

static T Choice<T>(string text, string name) where T : struct, Enum =>
    Enum.TryParse<T>(text, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value
        : throw new ArgumentException($"{name} must be one of {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}; '{text}' is not.");

static int TopView(string output, string[] names)
{
    var installPath = GameInstall.FindCandidates().FirstOrDefault();
    if (installPath is null)
    {
        Console.Error.WriteLine("No game install found.");
        return 1;
    }
    var library = PrefabLibrary.Load(GameInstall.Open(installPath));
    const int scale = 4, gap = 8;
    var views = new List<PrefabTopView>();
    foreach (var arg in names)
    {
        var parts = arg.Split(':');
        if (library.Find(parts[0]) is not { } info)
        {
            Console.Error.WriteLine($"No prefab named {parts[0]}");
            return 1;
        }
        var sw = Stopwatch.StartNew();
        var view = PrefabTopViewReader.Read(Path.Combine(info.Folder, info.Name + ".tts"), info.YOffset);
        if (view is null)
        {
            Console.Error.WriteLine($"Could not read {info.Name}");
            return 1;
        }
        Console.WriteLine($"{info.Name}: {info.Size.X}x{info.Size.Y}x{info.Size.Z} in {sw.ElapsedMilliseconds} ms");
        var turned = view.Turned(parts.Length > 1 ? int.Parse(parts[1]) : 0);
        // The street a link road would match at each edge.
        string[] edges = ["N", "E", "S", "W"];
        Console.WriteLine("  streets: " + string.Join(", ", Enumerable.Range(0, 4).Select(d =>
            TownGenerator.StreetAcross(turned, d) is { } s ? $"{edges[d]} {s.Type} {s.Radius * 2:0.#} m" : $"{edges[d]} none")));
        views.Add(turned);
    }
    int width = views.Sum(v => v.Width * scale + gap), height = views.Max(v => v.Height) * scale;
    var image = new byte[width * height * 4];
    int left = 0;
    foreach (var v in views)
    {
        for (int y = 0; y < v.Height * scale; y++)
            for (int x = 0; x < v.Width * scale; x++)
            {
                uint c = v.Pixels[y / scale * v.Width + x / scale];
                int o = (y * width + left + x) * 4;
                (image[o], image[o + 1], image[o + 2], image[o + 3]) = c == 0 ? ((byte)40, (byte)90, (byte)40, (byte)255)
                    : ((byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)255);
            }
        left += v.Width * scale + gap;
    }
    Png.WriteRgba(output, width, height, image);
    return 0;
}

static int Render(string folder, string output, int level)
{
    var world = WorldReader.Read(folder);
    var install = GameInstall.FindCandidates().FirstOrDefault();
    var biomes = install is null ? BiomeTable.Vanilla : BiomeTable.Load(GameInstall.Open(install));
    var renderer = new MapRenderer(world, biomes);
    if (level < 0) level = Math.Max(0, renderer.Levels - 3);

    int tiles = renderer.TilesAcross(level);
    int px = (int)Math.Ceiling(world.Size / (double)MapRenderer.MetresPerPixel(level));
    var image = new byte[px * px * 4];
    var sw = Stopwatch.StartNew();
    Parallel.For(0, tiles * tiles, i =>
    {
        int tx = i % tiles, ty = i / tiles;
        var tile = new uint[MapRenderer.TileSize * MapRenderer.TileSize];
        renderer.RenderTile(level, tx, ty, MapLayers.All, tile);
        for (int y = 0; y < MapRenderer.TileSize; y++)
        {
            int iy = ty * MapRenderer.TileSize + y;
            if (iy >= px) break;
            for (int x = 0; x < MapRenderer.TileSize; x++)
            {
                int ix = tx * MapRenderer.TileSize + x;
                if (ix >= px) break;
                uint c = tile[y * MapRenderer.TileSize + x];
                int o = (iy * px + ix) * 4;
                image[o] = (byte)(c >> 16);
                image[o + 1] = (byte)(c >> 8);
                image[o + 2] = (byte)c;
                image[o + 3] = (byte)(c >> 24);
            }
        }
    });
    Console.WriteLine($"level {level}: {tiles}x{tiles} tiles, {px}x{px} px in {sw.Elapsed.TotalSeconds:0.00} s");
    Png.WriteRgba(output, px, px, image);
    return 0;
}

static int Info(string folder)
{
    var sw = Stopwatch.StartNew();
    var world = WorldReader.Read(folder);
    Console.Write(WorldSummaryFormatter.Format(world));
    Console.WriteLine($"Loaded in  {sw.Elapsed.TotalSeconds:0.00} s");
    return 0;
}

static int RoundTrip(string folder, string output, bool overwrite)
{
    var sw = Stopwatch.StartNew();
    var original = WorldReader.Read(folder);
    Console.WriteLine($"read   {sw.Elapsed.TotalSeconds,6:0.00} s");

    sw.Restart();
    WorldWriter.Write(original, output, new WorldWriteOptions { Overwrite = overwrite });
    Console.WriteLine($"write  {sw.Elapsed.TotalSeconds,6:0.00} s");

    sw.Restart();
    var copy = WorldReader.Read(output);
    Console.WriteLine($"reread {sw.Elapsed.TotalSeconds,6:0.00} s");

    var differences = WorldComparer.Compare(original, copy);
    foreach (var d in differences) Console.WriteLine("DIFF   " + d);
    Console.WriteLine(differences.Count == 0 ? "OK     all layers identical" : $"FAIL   {differences.Count} difference(s)");
    return differences.Count == 0 ? 0 : 1;
}

static World Load(string source) =>
    File.Exists(source) && source.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase)
        ? ProjectStore.Load(source)
        : WorldReader.Read(source);

static int Export(string project, string output, bool overwrite)
{
    var sw = Stopwatch.StartNew();
    var world = ProjectStore.Load(project);
    world.Name = Path.GetFileName(Path.GetFullPath(output));
    WorldWriter.Write(world, output, new WorldWriteOptions { Overwrite = overwrite, NewLevelGuid = true });
    Console.WriteLine($"Exported {world.Name} in {sw.Elapsed.TotalSeconds:0.0} s");
    return 0;
}

static int Validate(string source, string[] options)
{
    bool bridgesOnly = false;
    string? game = null;
    for (int i = 0; i < options.Length; i++)
    {
        switch (options[i])
        {
            case "--bridges-only": bridgesOnly = true; break;
            case "--game" when i + 1 < options.Length: game = options[++i]; break;
            default: return BadArguments($"Unknown or incomplete option {options[i]}.");
        }
    }
    if (!Directory.Exists(source) && !File.Exists(source))
        return BadArguments($"'{source}' is not a world folder or a {ProjectStore.Extension} file.");
    if (game is not null && !Directory.Exists(game))
        return BadArguments($"The game folder '{game}' does not exist.");

    World world;
    try
    {
        world = Load(source);
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException
        or System.Xml.XmlException or FormatException or OverflowException)
    {
        return BadArguments($"Could not read '{source}': {ex.Message}");
    }

    var installPath = game ?? GameInstall.FindCandidates().FirstOrDefault();
    var library = installPath is null ? PrefabLibrary.Empty : PrefabLibrary.Load(GameInstall.Open(installPath));
    if (library.Count == 0 && bridgesOnly)
        return BadArguments("No prefabs to read: bridges are found from the game's bridge prefabs. Pass --game <gameFolder>.");
    GroundLevel.Resolve(world, library);
    var biomeTable = BiomeTable.Vanilla;
    try
    {
        if (installPath is not null) biomeTable = BiomeTable.Load(GameInstall.Open(installPath));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
    {
        // The vanilla colours still name the biomes of a world made for the base game.
    }
    var issues = WorldValidator.Validate(world, library, biomes: biomeTable);
    var bridges = BridgeChecks.Analyze(world, library).Bridges;
    Console.Write(ValidationReport.Format(world, issues, bridges, bridgesOnly));
    var shown = bridgesOnly ? issues.Where(i => i.Kind is not null) : issues;
    return shown.Any(i => i.Severity == IssueSeverity.Error) ? 1 : 0;
}

static int Compare(string a, string b)
{
    var differences = WorldComparer.Compare(Load(a), Load(b));
    foreach (var d in differences) Console.WriteLine("DIFF   " + d);
    Console.WriteLine(differences.Count == 0 ? "OK     identical" : $"FAIL   {differences.Count} difference(s)");
    return differences.Count == 0 ? 0 : 1;
}

static int Installs()
{
    var found = GameInstall.FindCandidates();
    foreach (var path in found) Console.WriteLine(path);
    if (found.Count == 0) Console.WriteLine("No Steam installs found; pick the game folder manually.");
    Console.WriteLine("Generated worlds: " + GameInstall.UserGeneratedWorldsDir);
    return 0;
}

/// <summary>
/// Reports straight away on the reporting thread, unlike Progress&lt;T&gt;, so lines print in order. A stage may report
/// from several threads at once, so one report runs at a time.
/// </summary>
sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    private readonly Lock _gate = new();

    public void Report(T value)
    {
        lock (_gate) report(value);
    }
}
