using System.IO.Compression;
using System.Text;
using System.Xml;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;

namespace MapEditor.Core.IO;

/// <summary>The values written to a bundle's ModInfo.xml.</summary>
public sealed record BundleInfo(
    string Name,
    string DisplayName,
    string Description,
    string Author,
    string Version,
    string Website,
    bool SkipWithAntiCheat);

/// <summary>A world folder in GeneratedWorlds, for the picker.</summary>
public sealed record WorldFolder(string Path, string Name, long Bytes, DateTime Modified);

/// <summary>A prefab that goes into a bundle, with the files it brings (full paths).</summary>
public sealed record BundledPrefab(string Name, IReadOnlyList<string> Files, bool IsPart);

/// <summary>What a bundle would hold. <see cref="Missing"/> names prefabs the maps use that were found nowhere.</summary>
public sealed record BundlePlan(
    IReadOnlyList<BundledPrefab> Prefabs,
    IReadOnlyList<string> Missing,
    int VanillaCount);

public readonly record struct BundleProgress(long BytesDone, long BytesTotal, string File);

/// <summary>Plans and writes a map bundle: a mod folder (Name\ at the zip's top level, unzipped into Mods), with worlds, their non-vanilla prefabs and optional config.</summary>
public static class MapBundle
{
    public const string PrefabsFolder = "Prefabs/POIs";

    /// <summary>The world folders in <paramref name="dir"/>, newest first. A world is a folder with a prefabs.xml.</summary>
    public static IReadOnlyList<WorldFolder> ListWorlds(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        var result = new List<WorldFolder>();
        foreach (string folder in Directory.EnumerateDirectories(dir))
        {
            if (!File.Exists(System.IO.Path.Combine(folder, WorldFiles.Prefabs))) continue;
            long bytes = 0;
            // The size it adds to a bundle, so without the game's cache files.
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                if (!IsGameCache(file)) bytes += new FileInfo(file).Length;
            result.Add(new WorldFolder(folder, System.IO.Path.GetFileName(folder), bytes, Directory.GetLastWriteTime(folder)));
        }
        return [.. result.OrderByDescending(w => w.Modified)];
    }

    /// <summary>The Name that goes with a display name: spaces become hyphens, characters a folder can't hold go.</summary>
    public static string DefaultName(string displayName)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in displayName.Trim())
        {
            if (char.IsWhiteSpace(c)) sb.Append('-');
            else if (Array.IndexOf(invalid, c) < 0) sb.Append(c);
        }
        return sb.ToString().TrimEnd('.', '-');
    }

    /// <summary>An error that stops a bundle with this name, or null. Name is used as a folder name.</summary>
    public static string? NameError(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "The name is empty.";
        if (name != name.Trim()) return "The name starts or ends with a space.";
        if (name.EndsWith('.')) return "The name ends with a dot.";
        if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) return "The name has characters a folder name can't have.";
        return null;
    }

    /// <summary>A warning for a name that works but breaks the habit of the game's mods (hyphens, no spaces), or null.</summary>
    public static string? NameWarning(string name) =>
        name.Contains(' ') ? "The name has spaces. Mod folder names usually use hyphens instead." : null;

    /// <summary>
    /// Finds which prefabs the worlds use that aren't vanilla. A prefab is vanilla when the library found it under
    /// <paramref name="vanillaPrefabsDir"/> (the library keeps the copy the game would use, so a mod's copy of a vanilla
    /// name counts as the mod's). Parts a bundled prefab's markers name come along when they aren't vanilla either.
    /// Vanilla prefabs are never bundled.
    /// </summary>
    public static BundlePlan Plan(IEnumerable<string> worldFolders, PrefabLibrary library, string vanillaPrefabsDir)
    {
        string vanilla = System.IO.Path.GetFullPath(vanillaPrefabsDir).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            + System.IO.Path.DirectorySeparatorChar;

        var used = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string world in worldFolders)
        {
            string file = System.IO.Path.Combine(world, WorldFiles.Prefabs);
            if (!File.Exists(file)) continue;
            foreach (var prefab in WorldXml.ReadPrefabs(file))
                if (prefab.Name.Length > 0) used.Add(prefab.Name);
        }

        var bundled = new Dictionary<string, BundledPrefab>(StringComparer.OrdinalIgnoreCase);
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var vanillaSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Name, bool IsPart)>(used.Select(n => (n, false)));
        var queued = new HashSet<string>(used, StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0)
        {
            var (name, isPart) = queue.Dequeue();
            if (library.Find(name) is not { } info) { missing.Add(name); continue; }
            string folder = System.IO.Path.GetFullPath(info.Folder).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                + System.IO.Path.DirectorySeparatorChar;
            if (folder.StartsWith(vanilla, StringComparison.OrdinalIgnoreCase)) { vanillaSeen.Add(info.Name); continue; }

            bundled[info.Name] = new BundledPrefab(info.Name, FilesOf(info), isPart);
            foreach (string part in info.Markers.Select(m => m.Part).OfType<string>())
                if (queued.Add(part)) queue.Enqueue((part, true));
        }

        return new BundlePlan([.. bundled.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)], [.. missing], vanillaSeen.Count);
    }

    /// <summary>Every file in the prefab's folder that shares its base name (name.tts, name.xml, name.blocks.nim ...).</summary>
    private static List<string> FilesOf(PrefabInfo info)
    {
        string prefix = info.Name + ".";
        return [.. Directory.EnumerateFiles(info.Folder)
            .Where(f => System.IO.Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The text of ModInfo.xml.</summary>
    public static string ModInfoXml(BundleInfo info)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            IndentChars = "\t",
            NewLineChars = "\r\n",
            OmitXmlDeclaration = true,
        };
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" ?>\r\n");
        using (var w = XmlWriter.Create(sb, settings))
        {
            w.WriteStartElement("xml");
            void Value(string element, string value)
            {
                w.WriteStartElement(element);
                w.WriteAttributeString("value", value);
                w.WriteEndElement();
            }
            Value("Name", info.Name);
            Value("DisplayName", info.DisplayName);
            Value("Description", info.Description);
            if (info.Author.Length > 0) Value("Author", info.Author);
            Value("Version", info.Version);
            if (info.Website.Length > 0) Value("Website", info.Website);
            if (info.SkipWithAntiCheat) Value("SkipWithAntiCheat", "true");
            w.WriteEndElement();
        }
        sb.Append("\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// Writes the zip. The data goes to a temporary file beside it and replaces <paramref name="zipPath"/> only when
    /// all of it is written; on cancel or failure that file is deleted and an old zip is left alone.
    /// </summary>
    public static void Write(string zipPath, BundleInfo info, IEnumerable<string> worldFolders, BundlePlan plan,
        string? configFolder, Action<BundleProgress>? progress, CancellationToken cancel)
    {
        if (NameError(info.Name) is { } error) throw new ArgumentException(error, nameof(info));
        var worlds = worldFolders.ToList();

        // The zip's top level is the mod folder itself, so it unzips straight into the Mods folder.
        string root = info.Name + "/";
        // (full path, entry name)
        var files = new List<(string Path, string Entry)>();
        foreach (string world in worlds)
        {
            string name = System.IO.Path.GetFileName(world.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            AddTree(files, world, root + "Worlds/" + name + "/", skip: IsGameCache);
        }
        foreach (var prefab in plan.Prefabs)
            foreach (string file in prefab.Files)
                files.Add((file, root + PrefabsFolder + "/" + System.IO.Path.GetFileName(file)));
        if (configFolder is { Length: > 0 }) AddTree(files, configFolder, root + "Config/");

        long total = files.Sum(f => new FileInfo(f.Path).Length);
        string temp = zipPath + ".partial";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(zipPath))!);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var modInfo = zip.CreateEntry(root + "ModInfo.xml", CompressionLevel.Optimal);
                using (var s = modInfo.Open())
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(ModInfoXml(info));
                    s.Write(bytes);
                }

                long done = 0;
                var buffer = new byte[1 << 20];
                foreach (var (path, entryName) in files)
                {
                    cancel.ThrowIfCancellationRequested();
                    progress?.Invoke(new BundleProgress(done, total, entryName));
                    var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                    entry.LastWriteTime = new DateTimeOffset(File.GetLastWriteTime(path));
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
                    using var output = entry.Open();
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        done += read;
                        progress?.Invoke(new BundleProgress(done, total, entryName));
                    }
                }
            }
            File.Move(temp, zipPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>
    /// Whether a file in a world folder is one the game writes for itself when it loads the world (checksums.txt and
    /// the *_processed / *_half copies of the heightmap and splat maps). It makes them again on a player's PC, so a
    /// bundle leaves them out, as hand-made bundles do; they would roughly double its size.
    /// </summary>
    public static bool IsGameCache(string fileName)
    {
        string name = System.IO.Path.GetFileName(fileName);
        string stem = System.IO.Path.GetFileNameWithoutExtension(name);
        return name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase)
               || stem.EndsWith("_processed", StringComparison.OrdinalIgnoreCase)
               || stem.EndsWith("_half", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddTree(List<(string, string)> files, string folder, string entryRoot, Func<string, bool>? skip = null)
    {
        string full = System.IO.Path.GetFullPath(folder);
        foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            if (skip is null || !skip(file))
                files.Add((file, entryRoot + System.IO.Path.GetRelativePath(full, file).Replace('\\', '/')));
    }
}
