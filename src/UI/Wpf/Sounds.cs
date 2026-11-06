using System.IO;
using System.Media;

namespace RestMode.UI.Wpf;

/// <summary>
/// Soft chime sounds, synthesised at runtime (original, no audio files):
/// a falling three-note chime when entering Rest Mode, a rising one on wake,
/// and a very quiet tick when a button is selected. Off when "Sounds" is off.
/// </summary>
internal static class Sounds
{
    private static byte[]? _enter, _wake, _tick;
    private static DateTime _lastTick;

    public static void Enter() => Play(_enter ??= Chime(new[] { 1318.5, 987.8, 659.3 }, 0.22, 0.5));
    public static void Wake() => Play(_wake ??= Chime(new[] { 659.3, 987.8, 1318.5 }, 0.16, 0.45));

    public static void Tick()
    {
        if (DateTime.UtcNow - _lastTick < TimeSpan.FromMilliseconds(80)) return;
        _lastTick = DateTime.UtcNow;
        Play(_tick ??= Chime(new[] { 2093.0 }, 0.05, 0.08));
    }

    private static void Play(byte[] wav)
    {
        if (!ThemeManager.SoundsOn) return;
        try
        {
            var player = new SoundPlayer(new MemoryStream(wav));
            player.Play(); // asynchronous
        }
        catch { /* no audio device - fine */ }
    }

    /// <summary>Bell-like notes (fundamental + soft overtones, exponential decay), 16-bit mono WAV.</summary>
    private static byte[] Chime(double[] freqs, double spacing, double volume)
    {
        const int rate = 44100;
        double noteLen = 1.4;
        int total = (int)(rate * (spacing * (freqs.Length - 1) + noteLen));
        var samples = new double[total];
        for (int n = 0; n < freqs.Length; n++)
        {
            int start = (int)(n * spacing * rate);
            for (int i = 0; start + i < total && i < noteLen * rate; i++)
            {
                double t = i / (double)rate;
                double env = Math.Exp(-t * 4.2) * Math.Min(1, t * 400);
                double f = freqs[n];
                samples[start + i] += env * (Math.Sin(2 * Math.PI * f * t) + 0.35 * Math.Sin(2 * Math.PI * f * 2.76 * t) + 0.12 * Math.Sin(2 * Math.PI * f * 5.4 * t));
            }
        }
        double peak = samples.Max(Math.Abs);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataLen = total * 2;
        w.Write("RIFF"u8); w.Write(36 + dataLen); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataLen);
        foreach (var s in samples) w.Write((short)(s / peak * volume * short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
