using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestMode.Util;

/// <summary>
/// Small helpers to read/write JSON files safely.
/// Writes go to a fresh temp file first and are then moved over the target,
/// so a crash mid-write never leaves a half-written state file behind.
/// The elevated helper also uses these, so writes refuse to follow
/// symbolic links / junctions (a standard user must not be able to trick the
/// admin helper into overwriting some other file).
/// </summary>
internal static class JsonFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not read {Path.GetFileName(path)} ({ex.Message}); ignoring it.");
            return null;
        }
    }

    public static void Write<T>(string path, T value)
    {
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        if (IsReparsePoint(dir) || IsReparsePoint(path))
            throw new IOException($"Refusing to write through a link: {path}");

        string tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp); // deletes a link itself, never its target
        using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var sw = new StreamWriter(fs))
        {
            sw.Write(JsonSerializer.Serialize(value, Options));
            sw.Flush();
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Logger.Warn($"Could not delete {Path.GetFileName(path)}: {ex.Message}"); }
    }

    public static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return false;
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true; // be conservative
        }
    }
}
