using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core;

/// <summary>
/// Reads SYSTEM_POWER_CAPABILITIES (via GetPwrCapabilities) to learn whether
/// this PC has S3 sleep, hibernation, Modern Standby (S0ix / "AoAc") and a battery.
/// Offsets are from the documented struct layout (all BOOLEAN = 1 byte).
/// </summary>
internal sealed class PowerCapabilities
{
    public bool S3Supported { get; private init; }
    public bool HibernateSupported { get; private init; }
    public bool ModernStandby { get; private init; }
    public bool ModernStandbyNetworkConnected { get; private init; }
    public bool HasBattery { get; private init; }
    public bool LidPresent { get; private init; }

    public static PowerCapabilities Read()
    {
        try
        {
            var b = new byte[256];
            if (!GetPwrCapabilities(b)) return new PowerCapabilities();
            return new PowerCapabilities
            {
                LidPresent = b[2] != 0,          // LidPresent
                S3Supported = b[5] != 0,         // SystemS3
                HibernateSupported = b[6] != 0 && b[8] != 0, // SystemS4 && HiberFilePresent
                ModernStandby = b[20] != 0,      // AoAc
                ModernStandbyNetworkConnected = b[23] != 0, // AoAcConnectivitySupported
                HasBattery = b[30] != 0,         // SystemBatteriesPresent
            };
        }
        catch (Exception ex)
        {
            Logger.Warn($"GetPwrCapabilities failed: {ex.Message}");
            return new PowerCapabilities();
        }
    }

    public override string ToString() =>
        $"S3 sleep={S3Supported}, hibernate={HibernateSupported}, Modern Standby={ModernStandby}" +
        $" (network-connected={ModernStandbyNetworkConnected}), battery={HasBattery}, lid={LidPresent}";
}
