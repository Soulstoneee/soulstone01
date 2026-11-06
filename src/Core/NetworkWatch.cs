using System.Diagnostics;
using System.Net.NetworkInformation;
using RestMode.Util;

namespace RestMode.Core;

/// <summary>
/// Watchdog helper: is the PC still online, and if Wi-Fi dropped, ask Windows
/// to reconnect to the network it was on (netsh wlan connect, no admin).
/// The Wi-Fi profile name is kept in memory only and never logged.
/// </summary>
internal static class NetworkWatch
{
    private static string? _wifiProfile;

    public static bool IsOnline()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                if (ni.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any))) return true;
            }
        }
        catch { return true; } // can't tell: don't raise false alarms
        return false;
    }

    /// <summary>Remembers the connected Wi-Fi profile (called when entering Rest Mode).</summary>
    public static void RememberWifi()
    {
        _wifiProfile = null;
        try
        {
            string text = Run("wlan show interfaces");
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2 && parts[0].Trim().Equals("Profile", StringComparison.OrdinalIgnoreCase))
                {
                    _wifiProfile = parts[1].Trim();
                    break;
                }
            }
        }
        catch { /* no Wi-Fi */ }
    }

    public static bool TryReconnectWifi()
    {
        if (string.IsNullOrEmpty(_wifiProfile)) return false;
        try
        {
            Run($"wlan connect name=\"{_wifiProfile.Replace("\"", "")}\"");
            return true;
        }
        catch { return false; }
    }

    private static string Run(string args)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), args)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(10_000);
        return o;
    }
}
