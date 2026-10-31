// Subtle audio feedback and haptic chimes for donut interactions and conversion completions.

using System.Runtime.InteropServices;
using Filee.Core.Settings;

namespace Filee.App.Services;

public sealed class SoundService(UserDataStore store)
{
    private const uint SndAsync = 0x0001;
    private const uint SndMemory = 0x0004;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(byte[] pszSound, IntPtr hmod, uint fdwSound);

    private readonly byte[]? _hoverTick = GenerateHoverTick();
    private readonly byte[]? _flipSound = GenerateFlipSound();
    private readonly byte[]? _completeChime = GenerateCompleteChime();

    public void PlayHoverTick()
    {
        if (!store.Settings.SoundEffects || _hoverTick is null || !OperatingSystem.IsWindows())
            return;
        try
        {
            PlaySound(_hoverTick, IntPtr.Zero, SndAsync | SndMemory);
        }
        catch
        {
            // Audio errors should never crash or interrupt the UI
        }
    }

    public void PlayFlipSound()
    {
        if (!store.Settings.SoundEffects || _flipSound is null || !OperatingSystem.IsWindows())
            return;
        try
        {
            PlaySound(_flipSound, IntPtr.Zero, SndAsync | SndMemory);
        }
        catch
        {
        }
    }

    public void PlayCompleteChime()
    {
        if (!store.Settings.SoundEffects || _completeChime is null || !OperatingSystem.IsWindows())
            return;
        try
        {
            PlaySound(_completeChime, IntPtr.Zero, SndAsync | SndMemory);
        }
        catch
        {
        }
    }

    private static byte[] GenerateHoverTick()
    {
        const int sampleRate = 44100;
        const double duration = 0.012; // 12 ms
        var numSamples = (int)(sampleRate * duration);
        var samples = new float[numSamples];
        for (var i = 0; i < numSamples; i++)
        {
            var t = (double)i / sampleRate;
            var env = Math.Exp(-10.0 * (i / (double)numSamples));
            samples[i] = (float)(Math.Sin(2.0 * Math.PI * 1400.0 * t) * env * 0.12);
        }
        return CreateWav(samples, sampleRate);
    }

    private static byte[] GenerateFlipSound()
    {
        const int sampleRate = 44100;
        const double duration = 0.035; // 35 ms
        var numSamples = (int)(sampleRate * duration);
        var samples = new float[numSamples];
        for (var i = 0; i < numSamples; i++)
        {
            var t = (double)i / sampleRate;
            var frac = (double)i / numSamples;
            var freq = 500.0 + 400.0 * frac;
            var env = Math.Sin(Math.PI * frac);
            samples[i] = (float)(Math.Sin(2.0 * Math.PI * freq * t) * env * 0.15);
        }
        return CreateWav(samples, sampleRate);
    }

    private static byte[] GenerateCompleteChime()
    {
        const int sampleRate = 44100;
        var n1 = (int)(sampleRate * 0.07); // 70 ms
        var n2 = (int)(sampleRate * 0.12); // 120 ms
        var total = n1 + n2;
        var samples = new float[total];
        for (var i = 0; i < n1; i++)
        {
            var t = (double)i / sampleRate;
            var env = Math.Exp(-3.0 * (i / (double)n1));
            samples[i] = (float)(Math.Sin(2.0 * Math.PI * 587.33 * t) * env * 0.18);
        }
        for (var i = 0; i < n2; i++)
        {
            var t = (double)i / sampleRate;
            var env = Math.Exp(-3.5 * (i / (double)n2));
            samples[n1 + i] = (float)(Math.Sin(2.0 * Math.PI * 880.00 * t) * env * 0.20);
        }
        return CreateWav(samples, sampleRate);
    }

    private static byte[] CreateWav(float[] samples, int sampleRate)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var byteRate = sampleRate * 2;
        var subchunk2Size = samples.Length * 2;

        writer.Write("RIFF"u8);
        writer.Write(36 + subchunk2Size);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16); // subchunk1 size
        writer.Write((short)1); // PCM
        writer.Write((short)1); // Mono
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)2); // block align
        writer.Write((short)16); // bits per sample
        writer.Write("data"u8);
        writer.Write(subchunk2Size);

        foreach (var sample in samples)
        {
            var clamped = Math.Clamp(sample, -1.0f, 1.0f);
            writer.Write((short)(clamped * 32767));
        }

        return stream.ToArray();
    }
}
