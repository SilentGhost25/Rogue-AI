using System.IO;
using System.Media;

namespace LabKiosk.Services;

/// <summary>Small synthesized sounds (no asset files): a fanfare for a solve, a siren for a breach.</summary>
public static class Sfx
{
    private static SoundPlayer? _fanfare, _siren;

    public static void Fanfare() => Play(ref _fanfare, new[] { (523.25, 140), (659.25, 140), (783.99, 140), (1046.5, 380) }, 0.35);
    public static void Siren() => Play(ref _siren, new[] { (880.0, 220), (660.0, 220), (880.0, 220), (660.0, 220), (880.0, 220), (660.0, 220) }, 0.25);

    private static void Play(ref SoundPlayer? cache, (double Hz, int Ms)[] notes, double volume)
    {
        try
        {
            cache ??= new SoundPlayer(new MemoryStream(BuildWav(notes, volume)));
            cache.Play();   // asynchronous
        }
        catch { /* no audio device: the visual badge is enough */ }
    }

    private static byte[] BuildWav((double Hz, int Ms)[] notes, double volume)
    {
        const int rate = 22050;
        var samples = new List<short>();
        foreach (var (hz, ms) in notes)
        {
            var count = rate * ms / 1000;
            for (var i = 0; i < count; i++)
            {
                var envelope = Math.Exp(-3.0 * i / count);                       // quick decay per note
                var value = Math.Sin(2 * Math.PI * hz * i / rate) * envelope * volume;
                samples.Add((short)(value * short.MaxValue));
            }
        }
        using var ms2 = new MemoryStream();
        using var w = new BinaryWriter(ms2);
        var dataBytes = samples.Count * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataBytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataBytes);
        foreach (var s in samples) w.Write(s);
        return ms2.ToArray();
    }
}
