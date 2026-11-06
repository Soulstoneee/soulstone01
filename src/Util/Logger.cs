namespace RestMode.Util;

/// <summary>
/// Tiny append-only text log. Never throws: logging must not be able to break
/// Rest Mode or its restore.
///  * Privacy: the user name, computer name and user-profile path are replaced
///    with placeholders, so a log can be shared safely. Wi-Fi names are never logged.
///  * Retention: lines older than 30 days are removed at startup; the file
///    also rotates to *.old.log at 512 KB.
/// </summary>
internal static class Logger
{
    private const long MaxBytes = 512 * 1024;
    public const int RetentionDays = 30;
    private static readonly object Gate = new();
    private static string? _path;
    private static readonly (string find, string replace)[] Redactions = BuildRedactions();

    public static string? CurrentPath => _path;

    public static void Init(string path)
    {
        _path = path;
        PruneOldLines(path);
        PruneOldLines(Path.ChangeExtension(path, ".old.log"));
    }

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    /// <summary>One line per finished Rest Mode session (the "what happened" summary).</summary>
    public static void Session(string message) => Write("SESSN", message);

    public static string Sanitize(string text)
    {
        foreach (var (find, replace) in Redactions)
            if (find.Length > 2) text = text.Replace(find, replace, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    private static (string, string)[] BuildRedactions()
    {
        var list = new List<(string, string)>();
        try
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (profile.Length > 3) list.Add((profile, "%USERPROFILE%"));
            list.Add((@"\Users\" + Environment.UserName + @"\", @"\Users\<user>\"));
            list.Add((Environment.MachineName, "<pc>"));
        }
        catch { /* best effort */ }
        return list.ToArray();
    }

    private static void Write(string level, string message)
    {
        string? path = _path;
        if (path == null) return;
        lock (Gate)
        {
            try
            {
                // The elevated helper logs into the user's folder: never write through a link.
                if (JsonFile.IsReparsePoint(path) || JsonFile.IsReparsePoint(Path.GetDirectoryName(path)!)) return;
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes)
                    File.Move(path, Path.ChangeExtension(path, ".old.log"), overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {Sanitize(message)}{Environment.NewLine}");
            }
            catch
            {
                // Ignore: a full disk or locked file must never stop a restore.
            }
        }
    }

    /// <summary>Removes log lines older than <see cref="RetentionDays"/> days (lines start with "yyyy-MM-dd").</summary>
    private static void PruneOldLines(string path)
    {
        try
        {
            if (!File.Exists(path) || JsonFile.IsReparsePoint(path)) return;
            string cutoff = DateTime.Now.AddDays(-RetentionDays).ToString("yyyy-MM-dd");
            var lines = File.ReadAllLines(path);
            var keep = lines.Where(l => l.Length < 10 || string.CompareOrdinal(l[..10], cutoff) >= 0).ToArray();
            if (keep.Length == lines.Length) return;
            if (keep.Length == 0) File.Delete(path);
            else File.WriteAllLines(path, keep);
        }
        catch { /* best effort */ }
    }
}
