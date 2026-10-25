using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LSLib.LS;
using LSLib.LS.Enums;
using SharpCompress.Archives;

namespace Bg3PartyTool.Core;

public enum SaveSource { Game, Imported, Output }

public sealed record SaveEntry(string Path, string Name, string Folder, string Leader, DateTime Modified, string? Thumb, SaveSource Source);

public sealed record RemoveResult(string Output, string Export, List<string> Log, List<string> Avatars);

public sealed record ExportFile(string Path, SaveFile.ParkedInfo Info, string? Problem);

/// <summary>Where saves live, importing them, and running a removal end to end.</summary>
public static partial class Library
{
    public static string DataDir { get; } = System.IO.Path.Combine(
        OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : XdgData,
        "BG3PartyTool");

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string WorkDir => System.IO.Path.Combine(DataDir, "work");
    public static string ImportDir => System.IO.Path.Combine(DataDir, "imported");
    public static string OutputDir => System.IO.Path.Combine(DataDir, "output");

    const string ProtonRel = "steamapps/compatdata/1086940/pfx/drive_c/users/steamuser/AppData/Local/Larian Studios/Baldur's Gate 3";

    /// <summary>BG3's own data folders: %LOCALAPPDATA% on Windows; on Linux the native build's XDG folder, the Proton prefix of every Steam library
    /// plus Heroic/Lutris-style Wine prefixes (GOG). Newest-used first.</summary>
    public static List<string> GameDataDirs()
    {
        var dirs = new List<string>();
        if (OperatingSystem.IsWindows())
            dirs.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Larian Studios", "Baldur's Gate 3"));
        else
        {
            // native Linux build (Steam since 2026): XDG data dir, also inside the Flatpak Steam sandbox
            dirs.Add(System.IO.Path.Combine(XdgData, "Larian Studios", "Baldur's Gate 3"));
            dirs.Add(System.IO.Path.Combine(Home, ".var/app/com.valvesoftware.Steam/.local/share/Larian Studios/Baldur's Gate 3"));
            foreach (var lib in SteamLibraries())
                dirs.Add(System.IO.Path.Combine(lib, ProtonRel));
            dirs.AddRange(WinePrefixes().SelectMany(UsersIn)
                .Select(u => System.IO.Path.Combine(u, "AppData", "Local", "Larian Studios", "Baldur's Gate 3")));
        }
        return dirs.Where(Directory.Exists).Select(System.IO.Path.GetFullPath).Distinct()
            .OrderByDescending(LastUsed).ToList();
    }

    static string XdgData => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : System.IO.Path.Combine(Home, ".local", "share");

    /// <summary>The game rewrites its profile list on every launch, so it tells which install is in use
    /// (a Story folder's date doesn't: writing our own save there would bump it).</summary>
    static DateTime LastUsed(string dataDir)
    {
        var profiles = System.IO.Path.Combine(dataDir, "PlayerProfiles", "playerprofiles8.lsf");
        return File.Exists(profiles) ? File.GetLastWriteTimeUtc(profiles) : DateTime.MinValue;
    }

    static IEnumerable<string> WinePrefixes()
    {
        var roots = new[]
        {
            System.IO.Path.Combine(Home, "Games", "Heroic", "Prefixes"),
            System.IO.Path.Combine(Home, ".var/app/com.heroicgameslauncher.hgl/config/heroic/Prefixes"),
            System.IO.Path.Combine(Home, "Games"),
        };
        foreach (var root in roots.Where(Directory.Exists))
            foreach (var d in SafeDirs(root))
            {
                yield return d;
                yield return System.IO.Path.Combine(d, "pfx");
            }
        yield return System.IO.Path.Combine(Home, ".wine");
    }

    static IEnumerable<string> UsersIn(string prefix) => SafeDirs(System.IO.Path.Combine(prefix, "drive_c", "users"));

    static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : []; }
        catch (Exception) { return []; }
    }

    static IEnumerable<string> SteamLibraries()
    {
        var roots = new[]
        {
            System.IO.Path.Combine(Home, ".local/share/Steam"),
            System.IO.Path.Combine(Home, ".steam/steam"),
            System.IO.Path.Combine(Home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"),
        };
        var libs = new HashSet<string>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            libs.Add(System.IO.Path.GetFullPath(root));
            var vdf = System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in VdfPath().Matches(File.ReadAllText(vdf)))
                libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        return libs;
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex VdfPath();

    public static List<string> StoryDirs() =>
        GameDataDirs().Select(d => System.IO.Path.Combine(d, "PlayerProfiles", "Public", "Savegames", "Story")).Where(Directory.Exists).ToList();

    /// <summary>Where the game looks for saves, so a result written here shows up in its Load menu: the most recently
    /// used install's Story folder, created if the game has run but never saved. Null when BG3 isn't found.</summary>
    public static string? GameSaveDir()
    {
        if (StoryDirs().FirstOrDefault() is { } story) return story;
        var profiles = GameDataDirs().Select(d => System.IO.Path.Combine(d, "PlayerProfiles")).FirstOrDefault(Directory.Exists);
        return profiles == null ? null : System.IO.Path.Combine(profiles, "Public", "Savegames", "Story");
    }

    /// <summary>The active BG3 profile ID on this machine, used to mark "your" character.</summary>
    public static string? LocalProfile()
    {
        foreach (var d in GameDataDirs())
        {
            var f = System.IO.Path.Combine(d, "PlayerProfiles", "playerprofiles8.lsf");
            if (!File.Exists(f)) continue;
            try
            {
                var res = ResourceUtils.LoadResource(f, ResourceLoadParameters.FromGameVersion(Game.BaldursGate3));
                foreach (var region in res.Regions.Values)
                    if (region.Attributes.TryGetValue("ActiveProfile", out var a)) return a.Value?.ToString();
            }
            catch (Exception) { /* unreadable profile only loses the "you" badge */ }
        }
        return null;
    }

    static SaveEntry Entry(string lsv, SaveSource source)
    {
        var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(lsv)) ?? "";
        var leader = folder.Contains("__") ? folder[..folder.IndexOf("__", StringComparison.Ordinal)] : "";
        var dash = leader.LastIndexOf('-');
        if (dash > 0) leader = leader[..dash];
        var thumb = Directory.EnumerateFiles(System.IO.Path.GetDirectoryName(lsv)!, "*.WebP").FirstOrDefault();
        return new SaveEntry(lsv, System.IO.Path.GetFileNameWithoutExtension(lsv), folder, leader, File.GetLastWriteTime(lsv), thumb, source);
    }

    public static List<SaveEntry> ListSaves()
    {
        var saves = new List<SaveEntry>();
        foreach (var d in StoryDirs())
            saves.AddRange(Directory.EnumerateDirectories(d).SelectMany(f => Directory.EnumerateFiles(f, "*.lsv")).Select(p => Entry(p, SaveSource.Game)));
        if (Directory.Exists(ImportDir))
            saves.AddRange(Directory.EnumerateFiles(ImportDir, "*.lsv", SearchOption.AllDirectories).Select(p => Entry(p, SaveSource.Imported)));
        if (Directory.Exists(OutputDir))
            saves.AddRange(Directory.EnumerateFiles(OutputDir, "*.lsv", SearchOption.AllDirectories).Select(p => Entry(p, SaveSource.Output)));
        return saves.OrderByDescending(s => s.Modified).ToList();
    }

    public static string SafeName(string s)
    {
        var r = Regex.Replace(s.Replace(' ', '_'), "[^A-Za-z0-9_-]+", "");
        return r.Length > 0 ? r : "Character";
    }

    /// <summary>Copy a .lsv (with its thumbnail) or extract an archive (.rar/.zip/.7z) of save folders.</summary>
    public static List<string> Import(string src)
    {
        var dest = System.IO.Path.Combine(ImportDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{SafeName(System.IO.Path.GetFileNameWithoutExtension(src))}");
        Directory.CreateDirectory(dest);
        try
        {
            if (src.EndsWith(".lsv", StringComparison.OrdinalIgnoreCase))
            {
                var parent = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(src)) ?? "";
                var folder = System.IO.Path.Combine(dest, parent.Contains("__") ? parent : System.IO.Path.GetFileNameWithoutExtension(src));
                Directory.CreateDirectory(folder);
                File.Copy(src, System.IO.Path.Combine(folder, System.IO.Path.GetFileName(src)));
                var pic = System.IO.Path.ChangeExtension(src, ".WebP");
                if (File.Exists(pic)) File.Copy(pic, System.IO.Path.Combine(folder, System.IO.Path.GetFileName(pic)));
            }
            else
            {
                var root = System.IO.Path.GetFullPath(dest) + System.IO.Path.DirectorySeparatorChar;
                using var archive = ArchiveFactory.OpenArchive(src, new SharpCompress.Readers.ReaderOptions());
                foreach (var e in archive.Entries.Where(e => !e.IsDirectory && e.Key != null))
                {
                    var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(dest, e.Key!));
                    if (!target.StartsWith(root, StringComparison.Ordinal)) continue; // no path traversal
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                    using var fs = File.Create(target);
                    e.WriteTo(fs, null);
                }
            }
            var found = Directory.EnumerateFiles(dest, "*.lsv", SearchOption.AllDirectories).Order().ToList();
            if (found.Count == 0) throw new SaveException(L.T("lib.nolsv"));
            return found;
        }
        catch
        {
            Directory.Delete(dest, true);
            throw;
        }
    }

    static string WorkFor(string lsv) =>
        System.IO.Path.Combine(WorkDir, Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(lsv))))[..12]);

    public static SaveFile Open(string lsv)
    {
        if (!File.Exists(lsv)) throw new SaveException(L.F("lib.nofile", lsv));
        var s = new SaveFile(lsv, WorkFor(lsv));
        var me = LocalProfile();
        if (me != null) foreach (var a in s.Avatars) a.IsMe = a.Profile == me;
        return s;
    }

    /// <summary>Remove an avatar from a fresh copy of the save on disk, write the result next to it and verify it.</summary>
    public static RemoveResult RemoveAvatar(string lsv, string avatar, string? recruitsTo, bool toGame, Action<string> log, string? extraExportPath = null, string? newName = null)
    {
        var lines = new List<string>();
        void Emit(string m) { lines.Add(m); log(m); }

        var s = new SaveFile(lsv, WorkFor(lsv));
        var export = s.RemoveAvatar(avatar, recruitsTo, Emit);
        var name = string.IsNullOrWhiteSpace(newName) ? DefaultName(s.SaveName, "name.without", avatar) : newName.Trim();
        s.MakeManualSave(); // results are always manual saves, so the game lists them under their own name
        var output = s.Write(OutRoot(toGame), name);
        Emit(L.F("lib.saved", output));
        // the export stays with the tool even when the save goes to the game, so the game's folder holds only saves
        var exportDir = System.IO.Path.Combine(OutputDir, System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(output)!));
        Directory.CreateDirectory(exportDir);
        var exportPath = System.IO.Path.Combine(exportDir, $"{SafeName(avatar)}.postac.json");
        File.WriteAllText(exportPath, export.ToJsonString(Pretty));
        Emit(L.F("lib.charsaved", exportPath));
        if (!string.IsNullOrWhiteSpace(extraExportPath) && System.IO.Path.GetFullPath(extraExportPath) != System.IO.Path.GetFullPath(exportPath))
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(extraExportPath))!);
            File.Copy(exportPath, extraExportPath, true);
            Emit(L.F("lib.charcopy", extraExportPath));
        }

        var check = new SaveFile(output, WorkFor(output));
        var names = check.Avatars.Select(a => a.Name).ToList();
        if (names.Contains(export["avatar"]!["Name"]!.GetValue<string>()))
            throw new SaveException(L.T("lib.verify.still"));
        Emit(L.F("lib.verify.ok", string.Join(", ", names)));
        return new RemoveResult(output, exportPath, lines, names);
    }

    /// <summary>"QuickSave_12_without_Vesania": the source name plus a localized word and the character.</summary>
    public static string DefaultName(string saveName, string wordKey, string character) => $"{saveName}_{L.T(wordKey)}_{SafeName(character)}";

    static string OutRoot(bool toGame)
    {
        if (!toGame) return OutputDir;
        var dir = GameSaveDir() ?? throw new SaveException(L.T("lib.nogamedir"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Folders the tool may delete saves from: the game's Story folders, imports and results.</summary>
    static bool Managed(string lsv)
    {
        var full = System.IO.Path.GetFullPath(lsv);
        return StoryDirs().Concat([ImportDir, OutputDir]).Any(d => full.StartsWith(System.IO.Path.GetFullPath(d) + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    /// <summary>Delete a save's folder (the .lsv, its thumbnail and any character files kept next to it).</summary>
    public static void DeleteSave(string lsv)
    {
        if (!File.Exists(lsv)) throw new SaveException(L.F("lib.nofile", lsv));
        if (!Managed(lsv)) throw new SaveException(L.T("lib.notours"));
        var folder = System.IO.Path.GetDirectoryName(lsv)!;
        // a save folder holds one save; anything else means the layout is not what we expect, so delete only the files
        if (Directory.EnumerateFiles(folder, "*.lsv").Count() == 1) Directory.Delete(folder, true);
        else
        {
            File.Delete(lsv);
            var pic = System.IO.Path.ChangeExtension(lsv, ".WebP");
            if (File.Exists(pic)) File.Delete(pic);
        }
    }

    /// <summary>Rename a save in place: new folder "&lt;prefix&gt;__&lt;name&gt;" and file "&lt;name&gt;.lsv", repacked with a fresh hash.
    /// Optionally make it a manual save so the game lists it under that name.</summary>
    public static string RenameSave(string lsv, string newName, Action<string> log)
    {
        if (!File.Exists(lsv)) throw new SaveException(L.F("lib.nofile", lsv));
        if (!Managed(lsv)) throw new SaveException(L.T("lib.notours"));
        var oldFolder = System.IO.Path.GetDirectoryName(lsv)!;
        var parent = System.IO.Path.GetDirectoryName(oldFolder)!;
        var s = new SaveFile(lsv, WorkFor(lsv));
        var target = System.IO.Path.Combine(parent, $"{s.FolderPrefix}__{newName}");
        var same = string.Equals(System.IO.Path.GetFullPath(target), System.IO.Path.GetFullPath(oldFolder), StringComparison.Ordinal);
        if (!same && Directory.Exists(target)) throw new SaveException(L.F("lib.exists", newName));
        s.MakeManualSave();
        var output = s.Write(parent, newName);
        if (!same)
        {
            foreach (var f in Directory.EnumerateFiles(oldFolder, "*.postac.json"))
                File.Move(f, System.IO.Path.Combine(target, System.IO.Path.GetFileName(f)), true);
            Directory.Delete(oldFolder, true);
        }
        else if (!string.Equals(output, lsv, StringComparison.Ordinal)) File.Delete(lsv);
        log(L.F("lib.saved", output));
        return output;
    }

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Keep a copy of a character export picked from anywhere, so it shows up for its campaign later.</summary>
    public static string ImportCharacter(string path)
    {
        var json = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new SaveException(L.T("lib.notchar"));
        if (json["avatar"]?["Guid"] == null) throw new SaveException(L.T("lib.notchar2"));
        var dir = System.IO.Path.Combine(ImportDir, "postacie");
        Directory.CreateDirectory(dir);
        var dest = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(path));
        if (System.IO.Path.GetFullPath(dest) != System.IO.Path.GetFullPath(path)) File.Copy(path, dest, true);
        return dest;
    }

    /// <summary>Character exports (.postac.json) belonging to this save's campaign, with why each can't be restored (if so).</summary>
    public static List<ExportFile> FindExports(SaveFile s)
    {
        var files = new[] { OutputDir, ImportDir }.Concat(StoryDirs()).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.postac.json", SearchOption.AllDirectories));
        var result = new List<ExportFile>();
        var seen = new HashSet<string>();
        foreach (var f in files.OrderByDescending(File.GetLastWriteTime))
        {
            try
            {
                var json = JsonNode.Parse(File.ReadAllText(f))!.AsObject();
                if (json["game_id"]?.GetValue<string>() != s.GameId) continue;
                var info = SaveFile.Describe(json);
                if (!seen.Add(info.Guid)) continue; // newest export per character
                result.Add(new ExportFile(f, info, s.CannotRestore(json)));
            }
            catch (Exception) { /* not an export of ours */ }
        }
        return result;
    }

    /// <summary>Restore a parked character from its export into a fresh copy of the save, write the result and verify it.</summary>
    public static RemoveResult RestoreAvatar(string lsv, string exportPath, string? profile, bool takeBackRecruits, bool toGame, Action<string> log, string? newName = null)
    {
        var lines = new List<string>();
        void Emit(string m) { lines.Add(m); log(m); }

        var export = JsonNode.Parse(File.ReadAllText(exportPath))!.AsObject();
        var info = SaveFile.Describe(export);
        var s = new SaveFile(lsv, WorkFor(lsv));
        var owner = profile ?? info.Profile;
        s.RestoreAvatar(export, owner, takeBackRecruits, Emit);
        var name = string.IsNullOrWhiteSpace(newName) ? DefaultName(s.SaveName, "name.with", info.Name) : newName.Trim();
        s.MakeManualSave();
        var output = s.Write(OutRoot(toGame), name);
        Emit(L.F("lib.saved", output));

        var check = new SaveFile(output, WorkFor(output));
        var restored = check.Avatars.FirstOrDefault(a => a.Guid == info.Guid);
        if (restored == null || restored.Profile != owner)
            throw new SaveException(L.T("lib.verify.missing"));
        Emit(L.F("lib.verify.ok", string.Join(", ", check.Avatars.Select(a => a.Name))));
        return new RemoveResult(output, exportPath, lines, check.Avatars.Select(a => a.Name).ToList());
    }
}
