namespace RestMode.Core;

/// <summary>
/// Power setting GUIDs (same as the aliases shown by `powercfg /aliases`).
/// Several of these are "hidden" in Control Panel but can still be written.
/// </summary>
internal static class PowerGuids
{
    // Built-in plans
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    // SUB_PROCESSOR
    public static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    public static readonly Guid ProcThrottleMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");   // PROCTHROTTLEMAX
    public static readonly Guid ProcThrottleMax1 = new("bc5038f7-23e0-4960-96da-33abaf5935ed");  // PROCTHROTTLEMAX1 (E-cores on hybrid CPUs)
    public static readonly Guid ProcThrottleMin = new("893dee8e-2bef-41e0-89c6-b55d0929964c");   // PROCTHROTTLEMIN
    public static readonly Guid ProcThrottleMin1 = new("893dee8e-2bef-41e0-89c6-b55d0929964d");  // PROCTHROTTLEMIN1
    public static readonly Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");     // PERFBOOSTMODE (0 = Disabled)
    public static readonly Guid PerfEpp = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");           // PERFEPP (0-100, 100 = max saving)
    public static readonly Guid PerfEpp1 = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6864");          // PERFEPP1
    public static readonly Guid SysCoolPol = new("94d3a615-a899-4ac5-ae2b-e4d8f634367f");        // SYSCOOLPOL (0 = Passive, 1 = Active)

    // SUB_PCIEXPRESS
    public static readonly Guid SubPciExpress = new("501a4d13-42af-4429-9fd1-a8218c268e20");
    public static readonly Guid Aspm = new("ee12f906-d277-404b-b6da-e5fa1a576df5");              // ASPM (2 = Maximum power savings)

    // SUB_DISK
    public static readonly Guid SubDisk = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
    public static readonly Guid DiskIdle = new("6738e2c4-e8a5-4a42-b16a-e040e769756e");          // DISKIDLE (seconds)

    // SUB_VIDEO
    public static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
    public static readonly Guid VideoIdle = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");         // VIDEOIDLE (seconds)
    public static readonly Guid VideoAdapt = new("90959d22-d6a1-49b9-af93-bce885ad335b");        // VIDEOADAPT (adaptive display timeout, 0 = off)
    public static readonly Guid VideoConLock = new("8ec4b3a5-6868-48c2-be75-4f3044be88a7");      // VIDEOCONLOCK (display off timeout on the lock screen, seconds)

    // SUB_SLEEP
    public static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    public static readonly Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");       // STANDBYIDLE (seconds, 0 = never)
    public static readonly Guid HibernateIdle = new("9d7815a6-7ee4-497e-8888-515a05f02364");     // HIBERNATEIDLE

    // SUB_BUTTONS
    public static readonly Guid SubButtons = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    public static readonly Guid LidAction = new("5ca83367-6e45-459f-a27b-476b1d01c936");         // LIDACTION (0 = Do nothing)

    // Wireless Adapter Settings
    public static readonly Guid SubWireless = new("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1");
    public static readonly Guid WirelessPowerSaving = new("12bbebe6-58d6-4636-95bb-3217ef867c1a"); // 0 = Maximum Performance

    // Power setting notifications
    public static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47"); // 0 off, 1 on, 2 dimmed
    public static readonly Guid AcDcPowerSource = new("5d3e9a59-e9d5-4b00-a6bd-ff34ff516548"); // 0 AC, 1 battery, 2 UPS
    public static readonly Guid LidSwitchStateChange = new("ba3e0f4d-b817-4094-a2d1-d56379e6a0f3"); // 0 closed, 1 open
    public static readonly Guid PowerSchemePersonality = new("245d8541-3943-4422-b025-13a784f679b7"); // active plan changed
}
