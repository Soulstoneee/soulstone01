namespace RestMode.Util;

/// <summary>
/// Command line:
///   RestMode.exe                 start in the tray
///   RestMode.exe --enter         enter Rest Mode now (used by the desktop shortcut)
///   RestMode.exe --settings      open the settings window
///   RestMode.exe --restore       put every setting back to normal (emergency)
///   RestMode.exe --uninstall     restore + remove autostart, shortcut and helper task
///   RestMode.exe --startup       (used by the "start with Windows" entry)
///   RestMode.exe --helper ...    internal: elevated helper
/// </summary>
internal sealed class CommandLine
{
    public bool Enter { get; private set; }
    public bool ShowSettings { get; private set; }
    public bool Restore { get; private set; }
    public bool Uninstall { get; private set; }
    public bool Startup { get; private set; }
    public string? HelperVerb { get; private set; }
    public string? DataDir { get; private set; }
    public string? FanExe { get; private set; }
    public string? UserSid { get; private set; }

    public static CommandLine Parse(string[] args)
    {
        var c = new CommandLine();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].ToLowerInvariant();
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (a)
            {
                case "--enter": case "/enter": c.Enter = true; break;
                case "--settings": case "/settings": c.ShowSettings = true; break;
                case "--restore": case "/restore": c.Restore = true; break;
                case "--uninstall": case "/uninstall": c.Uninstall = true; break;
                case "--startup": c.Startup = true; break;
                case "--helper": c.HelperVerb = Next(); break;
                case "--data": c.DataDir = Next(); break;
                case "--fan": c.FanExe = Next(); break;
                case "--user-sid": c.UserSid = Next(); break;
            }
        }
        return c;
    }
}
