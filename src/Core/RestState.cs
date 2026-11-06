using RestMode.Util;

namespace RestMode.Core;

/// <summary>
/// Everything RestMode changed, and what it was before. Written to
/// %LOCALAPPDATA%\RestMode\state.json after EVERY step of entering Rest Mode,
/// so a crash, kill, power cut or log-off at any moment can be undone on the
/// next launch. The file is deleted once everything has been restored.
/// (Network adapter + Fan Control changes are tracked separately by the
/// elevated helper in helper-state.json.)
/// </summary>
public sealed class RestState
{
    public bool Active { get; set; }
    public DateTime StartedUtc { get; set; }
    public string Trigger { get; set; } = "";

    /// <summary>The user's own power plan (never modified, only re-activated).</summary>
    public Guid? OriginalScheme { get; set; }
    /// <summary>The temporary duplicated plan; deleted on restore.</summary>
    public Guid? TempScheme { get; set; }

    /// <summary>Windows "Power mode" slider before Rest Mode (Guid.Empty = Balanced), if RestMode changed it.</summary>
    public Guid? PowerModeBefore { get; set; }
    public bool PowerModeChanged { get; set; }

    public AudioSnapshot? Audio { get; set; }
    public List<ProcessSnapshot> Processes { get; set; } = new();
    public bool HelperRequested { get; set; }
}

public sealed class AudioSnapshot
{
    public string? DeviceId { get; set; }
    public float Volume { get; set; }
    public bool Muted { get; set; }
}

public sealed class ProcessSnapshot
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Process creation time, so a re-used PID is never touched by mistake.</summary>
    public long CreationFileTime { get; set; }
    public uint OriginalPriorityClass { get; set; }
    public bool PriorityChanged { get; set; }
    public bool EcoQosSet { get; set; }
    public bool Suspended { get; set; }
}

internal static class StateStore
{
    public static RestState? Load() => JsonFile.Read<RestState>(AppPaths.StateFile);

    public static void Save(RestState state)
    {
        try { JsonFile.Write(AppPaths.StateFile, state); }
        catch (Exception ex) { Logger.Error("Could not save rest state (crash recovery may be incomplete)", ex); }
    }

    public static void Clear() => JsonFile.Delete(AppPaths.StateFile);
}
