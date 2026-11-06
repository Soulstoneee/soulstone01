namespace RestMode.Util;

/// <summary>
/// Tiny append-only text log. Rotates to *.old.log at 512 KB so it stays small.
/// Never throws: logging must not be able to break Rest Mode or its restore.
/// </summary>
internal static class Logger
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static string? CurrentPath => _path;

    public static void Init(string path) => _path = path;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    /// <summary>One line per finished Rest Mode session (the "what happened" summary).</summary>
    public static void Session(string message) => Write("SESSN", message);

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
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Ignore: a full disk or locked file must never stop a restore.
            }
        }
    }
}
