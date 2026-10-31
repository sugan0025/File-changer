// Subtle audio feedback and haptic chimes for donut interactions and conversion completions.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Filee.Core.Settings;

namespace Filee.App.Services;

public sealed class SoundService : IDisposable
{
    private const uint SndAsync = 0x0001;
    private const uint SndMemory = 0x0004;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

    private readonly UserDataStore _store;
    private readonly IntPtr _hoverTick;
    private readonly IntPtr _flipSound;
    private readonly IntPtr _completeChime;

    public SoundService(UserDataStore store)
    {
        _store = store;
        if (OperatingSystem.IsWindows())
        {
            _hoverTick = AllocateWav(GenerateHoverTick());
            _flipSound = AllocateWav(GenerateFlipSound());
            _completeChime = AllocateWav(GenerateCompleteChime());
        }
    }

    private static IntPtr AllocateWav(byte[] wav)
    {
        var ptr = Marshal.AllocHGlobal(wav.Length);
        Marshal.Copy(wav, 0, ptr, wav.Length);
        return ptr;
    }

    public void PlayHoverTick()
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows() && _hoverTick != IntPtr.Zero)
        {
            try
            {
                PlaySound(_hoverTick, IntPtr.Zero, SndAsync | SndMemory);
            }
            catch
            {
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            PlayMacSound("Tink");
        }
    }

    public void PlayFlipSound()
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows() && _flipSound != IntPtr.Zero)
        {
            try
            {
                PlaySound(_flipSound, IntPtr.Zero, SndAsync | SndMemory);
            }
            catch
            {
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            PlayMacSound("Pop");
        }
    }

    public void PlayCompleteChime()
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows() && _completeChime != IntPtr.Zero)
        {
            try
            {
                PlaySound(_completeChime, IntPtr.Zero, SndAsync | SndMemory);
            }
            catch
            {
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            PlayMacSound("Glass");
        }
    }

    private static void PlayMacSound(string soundName)
    {
        try
        {
            var path = $"/System/Library/Sounds/{soundName}.aiff";
            if (File.Exists(path))
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "/usr/bin/afplay",
                    ArgumentList = { path },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
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

    public void Dispose()
    {
        if (_hoverTick != IntPtr.Zero) Marshal.FreeHGlobal(_hoverTick);
        if (_flipSound != IntPtr.Zero) Marshal.FreeHGlobal(_flipSound);
        if (_completeChime != IntPtr.Zero) Marshal.FreeHGlobal(_completeChime);
    }
}
