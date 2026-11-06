using System.Diagnostics;
using System.Runtime.InteropServices;
using RestMode.Util;
using static RestMode.Native.NativeMethods;

namespace RestMode.Core.Safety;

/// <summary>One set of temperature / fan readings.</summary>
public sealed class SensorReading
{
    public double? CpuC { get; set; }
    public double? GpuC { get; set; }
    /// <summary>Fan speeds in RPM, or null if this device can't report them.</summary>
    public List<int>? FanRpm { get; set; }
    public string Source { get; set; } = "";
    public DateTime Utc { get; set; } = DateTime.UtcNow;

    public bool HasTemperature => CpuC != null || GpuC != null;
    public double? Hottest => CpuC is double c && GpuC is double g ? Math.Max(c, g) : CpuC ?? GpuC;

    public override string ToString() =>
        $"CPU {(CpuC is double c ? $"{c:0} °C" : "n/a")}, GPU {(GpuC is double g ? $"{g:0} °C" : "n/a")}, " +
        $"fans {(FanRpm is { Count: > 0 } f ? string.Join("/", f) + " RPM" : "not available on this device")} ({Source})";
}

/// <summary>
/// Temperature sources that need NO admin rights, usable on any PC:
///  * ACPI thermal zones ("\Thermal Zone Information(*)\Temperature" performance
///    counter, Kelvin) - the motherboard's own CPU-area sensor on most PCs.
///  * nvidia-smi (NVIDIA driver) for the GPU temperature - desktops only: on
///    laptops querying it can wake a sleeping discrete GPU and cost power.
/// Vendor sensors (e.g. Lenovo, which also gives fan RPM) are read by the
/// admin helper; see <see cref="Fans.IBuiltInFanModes.ReadSensors"/>.
/// </summary>
internal static class Sensors
{
    private static string? _nvidiaSmi;
    private static bool _nvidiaChecked;

    public static SensorReading ReadLocal(DeviceProfile dev)
    {
        var r = new SensorReading();
        var sources = new List<string>();
        if (AcpiThermalZoneC() is double c) { r.CpuC = c; sources.Add("ACPI thermal zone"); }
        if (dev.Kind == DeviceKind.Desktop && NvidiaGpuC() is double g) { r.GpuC = g; sources.Add("nvidia-smi"); }
        r.Source = sources.Count > 0 ? string.Join(" + ", sources) : "no sensor";
        return r;
    }

    /// <summary>Hottest ACPI thermal zone in °C, or null. Ignores the common fake constant values.</summary>
    public static double? AcpiThermalZoneC()
    {
        IntPtr query = IntPtr.Zero;
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out query) != 0) return null;
            if (PdhAddEnglishCounterW(query, @"\Thermal Zone Information(*)\Temperature", IntPtr.Zero, out IntPtr counter) != 0) return null;
            if (PdhCollectQueryData(query) != 0) return null;
            uint size = 0, count = 0;
            uint rc = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, ref count, IntPtr.Zero);
            if (rc != PDH_MORE_DATA || size == 0) return null;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, ref count, buf) != 0) return null;
                int itemSize = IntPtr.Size + 16; // PDH_FMT_COUNTERVALUE_ITEM_W: name pointer + {CStatus, padding, double}
                double best = double.NaN;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buf + i * itemSize;
                    uint status = (uint)Marshal.ReadInt32(item + IntPtr.Size);
                    double kelvin = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + IntPtr.Size + 8));
                    if (status != 0) continue;
                    double celsius = kelvin - 273.15;
                    // Many boards report a fixed 27.8 °C / 0 °C / 100+ °C placeholder: ignore those.
                    if (celsius < 5 || celsius > 115 || Math.Abs(celsius - 27.85) < 0.2) continue;
                    if (double.IsNaN(best) || celsius > best) best = celsius;
                }
                return double.IsNaN(best) ? null : Math.Round(best, 1);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return null; }
        finally { if (query != IntPtr.Zero) try { PdhCloseQuery(query); } catch { } }
    }

    public static double? NvidiaGpuC()
    {
        try
        {
            if (!_nvidiaChecked)
            {
                _nvidiaChecked = true;
                string smi = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
                _nvidiaSmi = File.Exists(smi) ? smi : null;
            }
            if (_nvidiaSmi == null) return null;
            var psi = new ProcessStartInfo(_nvidiaSmi, "--query-gpu=temperature.gpu --format=csv,noheader,nounits")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using var p = Process.Start(psi)!;
            string text = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(4000)) { try { p.Kill(); } catch { } return null; }
            var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return double.TryParse(first, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && v is > 5 and < 120 ? v : null;
        }
        catch { return null; }
    }
}
