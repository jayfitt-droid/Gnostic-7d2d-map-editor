// Writes a sample .7dmap (and a Generate preset) with the MapEditor.Core of one release. make-sample.ps1 builds it
// against that release's tag. FORMAT1 marks v1.2.0 and older; NO_GENERATOR marks releases before the Generate panel;
// NO_PINS marks releases before format 6 (v2.3.0 and older). The v2.4.0-rc.1 sample was made before the tool added a pin.
// Usage: save-samples <out.7dmap> [<out-preset.json>]
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
#if !NO_PINS
using MapEditor.Core.GameLink;
#endif
using MapEditor.Core.IO;
using MapEditor.Core.Model;

const int Size = 256;
var world = new World { Name = "Sample", Size = Size, Height = new Grid<ushort>(Size, Size) };
for (int z = 0; z < Size; z++)
    for (int x = 0; x < Size; x++)
        world.Height[x, z] = (ushort)(10000 + x * 3 + z * 5);
var biomes = new Grid<byte>(Size / 8, Size / 8);
biomes[3, 4] = 1;
biomes[5, 6] = 2;
world.Biomes = new IndexedLayer(biomes, [0x004000, 0xFFE477, 0xBA00FF]);
world.Roads = new Grid<byte>(Size, Size);
world.Roads[5, 6] = (byte)RoadType.Gravel;
world.Roads[7, 6] = (byte)RoadType.Asphalt;
world.Water = new Grid<byte>(Size, Size);
world.Water[7, 8] = 30;
world.Radiation = new Grid<byte>(Size / 32, Size / 32);
world.Radiation[1, 1] = 255;

var house = new PrefabInstance { Name = "house_old_01", Position = new Int3(-10, 37, 20), Rotation = 3, Type = "model" };
house.ExtraAttributes.Add(new("help", "x"));
world.Prefabs.Add(house);
#if FORMAT1
// Format 1 kept the game editor's ground-level flag as a plain attribute.
var ground = new PrefabInstance { Name = "store_gun_01", Position = new Int3(40, 2, -30), Rotation = 1, Type = "model" };
ground.ExtraAttributes.Add(new("y_is_groundlevel", "true"));
world.Prefabs.Add(ground);
#else
world.Prefabs.Add(new PrefabInstance { Name = "store_gun_01", Position = new Int3(40, 2, -30), Rotation = 1, Type = "model", GroundLevel = true, GroundLevelOffset = -4 });
world.Prefabs.Add(new PrefabInstance { Name = "store_book_01", Position = new Int3(60, 2, -60), Rotation = 2, Type = "model", GroundLevel = true });
#endif
world.Spawns.Add(new SpawnPoint { X = 1.5f, Y = 40, Z = -7, RotationY = 90 });
world.Spawns.Add(new SpawnPoint { X = -20.25f, Y = 41, Z = 33, RotationY = 270 });
world.ExtraFiles[WorldFiles.Level] = [1, 2, 3];
world.PassthroughImages[WorldFiles.Water] = new RgbaImage(2, 1, [1, 2, 3, 4, 5, 6, 7, 8]);
WorldXml.SetProperty(world.MapInfo, "Description", "sample");

var settings = new ProjectStore.ProjectSettings { ExportFolder = @"C:\out\Sample" };
#if !NO_GENERATOR
var gen = new MapEditor.Core.Generate.GeneratorSettings();
Filler.Fill(gen);
settings.Generator = gen;
#endif
#if !NO_PINS
// Format 6: one playtest pin, and the receipt that waits for the next save.
var pin = new GameLinkPin(new Guid("5a3c0e7d-2f4b-4e61-9d8a-1b2c3d4e5f60"), -18.25f, 62, 49, 225, "Sample", "Test Save",
    new DateTimeOffset(2026, 11, 23, 12, 34, 56, TimeSpan.Zero), "Check the \"café\".");
settings.Pins.Add(pin);
settings.PendingPinAcks.Add(GameLinkPins.WorldKey(pin.World), pin.Id);
#endif
ProjectStore.Save(world, args[0], settings);
#if !NO_GENERATOR
if (args.Length > 1)
    File.WriteAllText(args[1], JsonSerializer.Serialize(gen.ToPreset("Sample preset"), new JsonSerializerOptions { WriteIndented = true }));
#endif
Console.WriteLine($"wrote {args[0]}");

/// <summary>
/// Sets every saved property in an object graph to a non-default value and puts one item in every collection, so a
/// saved file holds every field the release knew about.
/// </summary>
static class Filler
{
    static int _n = 1;

    public static void Fill(object target, int depth = 0)
    {
        foreach (var p in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || p.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;
            if (p.CanWrite && p.SetMethod!.IsPublic)
            {
                if (Make(p.PropertyType, depth) is { } value) p.SetValue(target, value);
            }
            else if (p.CanRead && p.GetValue(target) is IList list && !list.IsFixedSize && ElementType(p.PropertyType) is { } et)
                list.Add(Make(et, depth));
        }
    }

    static Type? ElementType(Type t) =>
        t.IsArray ? t.GetElementType() : t.IsGenericType ? t.GetGenericArguments()[0] : null;

    public static object? Make(Type t, int depth)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        int n = _n++;
        if (u == typeof(int)) return 7 + n;
        if (u == typeof(long)) return 7L + n;
        if (u == typeof(uint)) return (uint)(0x100 + n);
        if (u == typeof(ushort)) return (ushort)(100 + n);
        if (u == typeof(byte)) return (byte)(1 + n % 200);
        if (u == typeof(float)) return n + 0.5f;
        if (u == typeof(double)) return n + 0.25;
        if (u == typeof(bool)) return true;
        if (u == typeof(string)) return $"s{n}";
        if (u.IsEnum)
        {
            // The last member: a reordered or removed member then changes what the file means.
            var values = Enum.GetValues(u);
            return values.GetValue(values.Length - 1);
        }
        if (depth > 6) return null;
        if (u.IsArray)
        {
            var arr = Array.CreateInstance(u.GetElementType()!, 2);
            arr.SetValue(Make(u.GetElementType()!, depth + 1), 0);
            arr.SetValue(Make(u.GetElementType()!, depth + 1), 1);
            return arr;
        }
        if (u.IsGenericType)
        {
            var def = u.GetGenericTypeDefinition();
            var args = u.GetGenericArguments();
            if (def == typeof(List<>) || def == typeof(HashSet<>))
            {
                var c = Activator.CreateInstance(u)!;
                u.GetMethod("Add")!.Invoke(c, [Make(args[0], depth + 1)]);
                return c;
            }
            if (def == typeof(Dictionary<,>))
            {
                var d = (IDictionary)Activator.CreateInstance(u)!;
                d[Make(args[0], depth + 1)!] = Make(args[1], depth + 1);
                return d;
            }
        }
        if (u.IsClass || u.IsValueType)
        {
            var ctor = u.GetConstructor(Type.EmptyTypes);
            object obj;
            if (ctor is not null || u.IsValueType)
            {
                obj = Activator.CreateInstance(u)!;
            }
            else
            {
                var best = u.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
                obj = best.Invoke(best.GetParameters().Select(p => Make(p.ParameterType, depth + 1)).ToArray());
                return obj; // records: the constructor set everything
            }
            Fill(obj, depth + 1);
            return obj;
        }
        return null;
    }
}
