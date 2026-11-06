namespace RestMode.Util;

/// <summary>
/// Command line:
///   RestMode.exe                 start in the tray (and open the main window the first time)
///   RestMode.exe --enter         enter Rest Mode now (Start menu "RestMode - Rest now", desktop shortcut)
///   RestMode.exe --wake          leave Rest Mode now
///   RestMode.exe --settings      open the settings window
///   RestMode.exe --restore       put every setting back to normal (emergency)
///   RestMode.exe --install       install for this user (also what RestMode-Setup.exe does)
///   RestMode.exe --uninstall     restore + remove everything RestMode added
///   RestMode.exe --quiet         with --restore / --install / --uninstall: no dialogs
///   RestMode.exe --startup       (used by the "start with Windows" entry)
///   RestMode.exe --simulate X    test runs only (needs the test-mode flag file)
///   RestMode.exe --helper ...    internal: elevated helper
/// </summary>
internal sealed class CommandLine
{
    public bool Enter { get; private set; }
    public bool Wake { get; private set; }
    public bool ShowSettings { get; private set; }
    public bool Restore { get; private set; }
    public bool Uninstall { get; private set; }
    public bool Install { get; private set; }
    public bool Quiet { get; private set; }
    public bool Startup { get; private set; }
    public string? Simulate { get; private set; }
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
                case "--wake": case "/wake": c.Wake = true; break;
                case "--settings": case "/settings": c.ShowSettings = true; break;
                case "--restore": case "/restore": c.Restore = true; break;
                case "--uninstall": case "/uninstall": c.Uninstall = true; break;
                case "--install": case "/install": c.Install = true; break;
                case "--quiet": case "/quiet": case "/q": c.Quiet = true; break;
                case "--startup": c.Startup = true; break;
                case "--simulate": c.Simulate = Next(); break;
                case "--helper": c.HelperVerb = Next(); break;
                case "--data": c.DataDir = Next(); break;
                case "--fan": c.FanExe = Next(); break;
                case "--user-sid": c.UserSid = Next(); break;
            }
        }
        return c;
    }
}
