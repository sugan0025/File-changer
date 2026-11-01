// Subtle audio feedback and haptic chimes for donut interactions and conversion completions.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Filee.Core.Settings;

namespace Filee.App.Services;

public sealed class SoundService : IDisposable
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;

    private const int SampleRate = 48000;
    private const double Tau = 2 * Math.PI;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    private const uint WHDR_INQUEUE = 0x00000010;

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID, ref WAVEFORMATEX lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr hWaveOut, ref WAVEHDR lpWaveHdr, uint uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, ref WAVEHDR lpWaveHdr, uint uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr hWaveOut, ref WAVEHDR lpWaveHdr, uint uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr hWaveOut);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr hWaveOut);

    private sealed class FastTickPlayer : IDisposable
    {
        private const int PoolSize = 16;
        private readonly IntPtr _hWaveOut;
        private readonly IntPtr[] _ptrs = new IntPtr[PoolSize];
        private readonly WAVEHDR[] _hdrs = new WAVEHDR[PoolSize];
        private readonly int _hdrSize = Marshal.SizeOf<WAVEHDR>();
        private int _ringIndex;
        private bool _disposed;
        public bool IsAvailable => !_disposed && _hWaveOut != IntPtr.Zero;

        public FastTickPlayer()
        {
            try
            {
                var fmt = new WAVEFORMATEX
                {
                    wFormatTag = 1,
                    nChannels = 1,
                    nSamplesPerSec = (uint)SampleRate,
                    nAvgBytesPerSec = (uint)(SampleRate * 2),
                    nBlockAlign = 2,
                    wBitsPerSample = 16,
                    cbSize = 0
                };

                if (waveOutOpen(out _hWaveOut, -1, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0)
                {
                    _hWaveOut = IntPtr.Zero;
                    return;
                }

                for (var i = 0; i < PoolSize; i++)
                {
                    var variant = i % 3;
                    var samples = RenderTick(variant, 0.022);
                    var byteLen = samples.Length * 2;
                    _ptrs[i] = Marshal.AllocHGlobal(byteLen);

                    var raw = new short[samples.Length];
                    for (var s = 0; s < samples.Length; s++)
                    {
                        var clamped = Math.Clamp(samples[s], -1.0f, 1.0f);
                        raw[s] = (short)(clamped * 32767);
                    }
                    Marshal.Copy(raw, 0, _ptrs[i], raw.Length);

                    _hdrs[i] = new WAVEHDR
                    {
                        lpData = _ptrs[i],
                        dwBufferLength = (uint)byteLen,
                        dwFlags = 0
                    };
                    waveOutPrepareHeader(_hWaveOut, ref _hdrs[i], (uint)_hdrSize);
                }
            }
            catch
            {
                _hWaveOut = IntPtr.Zero;
            }
        }

        public bool PlayTick()
        {
            if (!IsAvailable)
                return false;

            lock (_hdrs)
            {
                for (var attempt = 0; attempt < PoolSize; attempt++)
                {
                    var idx = (_ringIndex + attempt) % PoolSize;
                    if ((_hdrs[idx].dwFlags & WHDR_INQUEUE) == 0)
                    {
                        _ringIndex = (idx + 1) % PoolSize;
                        var res = waveOutWrite(_hWaveOut, ref _hdrs[idx], (uint)_hdrSize);
                        return res == 0;
                    }
                }

                var fallback = _ringIndex;
                _ringIndex = (_ringIndex + 1) % PoolSize;
                if ((_hdrs[fallback].dwFlags & WHDR_INQUEUE) == 0)
                {
                    return waveOutWrite(_hWaveOut, ref _hdrs[fallback], (uint)_hdrSize) == 0;
                }
                return true;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                if (_hWaveOut != IntPtr.Zero)
                {
                    waveOutReset(_hWaveOut);
                    for (var i = 0; i < PoolSize; i++)
                    {
                        if (_ptrs[i] != IntPtr.Zero)
                        {
                            waveOutUnprepareHeader(_hWaveOut, ref _hdrs[i], (uint)_hdrSize);
                            Marshal.FreeHGlobal(_ptrs[i]);
                            _ptrs[i] = IntPtr.Zero;
                        }
                    }
                    waveOutClose(_hWaveOut);
                }
            }
            catch
            {
            }
        }
    }

    private readonly UserDataStore _store;
    private readonly FastTickPlayer? _fastTickPlayer;
    private readonly IntPtr[] _ticks = new IntPtr[3];
    private int _tickIndex;
    private readonly IntPtr _flipSound;
    private readonly IntPtr _completeChime;
    private readonly IntPtr _fanOut;
    private readonly IntPtr _dropCatch;
    private readonly IntPtr _cancel;
    private readonly IntPtr _error;

    public SoundService(UserDataStore store)
    {
        _store = store;
        if (OperatingSystem.IsWindows())
        {
            _fastTickPlayer = new FastTickPlayer();
            for (var v = 0; v < 3; v++)
                _ticks[v] = AllocateWav(CreateWav(RenderTick(v), SampleRate));

            _flipSound = AllocateWav(CreateWav(RenderFlip(), SampleRate));
            _completeChime = AllocateWav(CreateWav(RenderGlassChime(), SampleRate));
            _fanOut = AllocateWav(CreateWav(RenderFanOut(), SampleRate));
            _dropCatch = AllocateWav(CreateWav(RenderDropCatch(), SampleRate));
            _cancel = AllocateWav(CreateWav(RenderCancel(), SampleRate));
            _error = AllocateWav(CreateWav(RenderError(), SampleRate));
        }
    }

    private static IntPtr AllocateWav(byte[] wav)
    {
        var ptr = Marshal.AllocHGlobal(wav.Length);
        Marshal.Copy(wav, 0, ptr, wav.Length);
        return ptr;
    }

    private void PlayWindows(IntPtr wav)
    {
        if (wav == IntPtr.Zero)
            return;
        try
        {
            PlaySound(wav, IntPtr.Zero, SndAsync | SndMemory | SndNoDefault);
        }
        catch
        {
        }
    }

    public void PlayHoverTick()
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows())
        {
            if (_fastTickPlayer is not null && _fastTickPlayer.IsAvailable && _fastTickPlayer.PlayTick())
            {
                return;
            }

            PlayWindows(_ticks[_tickIndex]);
            _tickIndex = (_tickIndex + 1) % 3;
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

        if (OperatingSystem.IsWindows())
            PlayWindows(_flipSound);
        else if (OperatingSystem.IsMacOS())
            PlayMacSound("Pop");
    }

    public void PlayCompleteChime()
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows())
            PlayWindows(_completeChime);
        else if (OperatingSystem.IsMacOS())
            PlayMacSound("Glass");
    }

    public void PlayFanOut()
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows())
            PlayWindows(_fanOut);
        else if (OperatingSystem.IsMacOS())
            PlayMacSound("Pop");
    }

    public void PlayDropCatch()
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows())
            PlayWindows(_dropCatch);
        else if (OperatingSystem.IsMacOS())
            PlayMacSound("Bottle");
    }

    public void PlayCancel()
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows())
            PlayWindows(_cancel);
        else if (OperatingSystem.IsMacOS())
            PlayMacSound("Morse");
    }

    public void PlayError()
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows())
            PlayWindows(_error);
        else if (OperatingSystem.IsMacOS())
            PlayMacSound("Basso");
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

    // ───────── Tiny DSP Synthesis Kit ─────────

    private sealed class Rng
    {
        private uint _s;
        public Rng(uint seed) => _s = seed == 0 ? 1 : seed;
        public double Next()
        {
            _s ^= _s << 13;
            _s ^= _s >> 17;
            _s ^= _s << 5;
            return _s / (double)uint.MaxValue * 2 - 1;
        }
    }

    private struct Svf
    {
        private double _low, _band;
        public void Process(double x, double fc, double q, out double low, out double band)
        {
            var f = 2 * Math.Sin(Math.PI * fc / SampleRate);
            _low += f * _band;
            var high = x - _low - q * _band;
            _band += f * high;
            low = _low;
            band = _band;
        }
    }

    private static float[] Finish(float[] s, double peak, double fadeOutMs)
    {
        var max = 0f;
        foreach (var v in s)
            max = Math.Max(max, Math.Abs(v));
        if (max <= 0)
            return s;

        var k = (float)(peak / max);
        var rampIn = Math.Max(1, (int)(SampleRate * 0.00025));
        var rampOut = Math.Max(1, (int)(SampleRate * fadeOutMs / 1000.0));
        for (var i = 0; i < s.Length; i++)
        {
            var v = s[i] * k;
            if (i < rampIn)
                v *= i / (float)rampIn;
            var j = s.Length - 1 - i;
            if (j < rampOut)
                v *= (float)(0.5 * (1 - Math.Cos(Math.PI * j / rampOut)));
            s[i] = v;
        }
        return s;
    }

    private static float[] RenderTick(int variant, double durationSec = 0.030)
    {
        var s = new float[(int)(SampleRate * durationSec)];
        var pitch = 1 + (variant - 1) * 0.035;
        var bp = new Svf();
        var rng = new Rng(0x9E3779B9u + (uint)variant * 7919u);
        for (var i = 0; i < s.Length; i++)
        {
            var t = i / (double)SampleRate;
            bp.Process(rng.Next(), 3200 * pitch, 1 / 1.4, out _, out var band);
            var noise = band * Math.Exp(-t / 0.0012);
            var tink = Math.Sin(Tau * 2350 * pitch * t) * Math.Exp(-t / 0.0042) * 0.55
                     + Math.Sin(Tau * 3720 * pitch * t) * Math.Exp(-t / 0.0026) * 0.30;
            var thock = Math.Sin(Tau * 170 * t) * Math.Exp(-t / 0.0075) * 0.40;
            s[i] = (float)(noise * 0.9 + tink + thock);
        }
        return Finish(s, 0.32, 4);
    }

    private static float[] RenderFlip()
    {
        var s = new float[(int)(SampleRate * 0.16)];
        var svf = new Svf();
        var rng = new Rng(0xC0FFEEu);
        var phase = 0.0;
        for (var i = 0; i < s.Length; i++)
        {
            var u = i / (double)s.Length;
            var env = Math.Exp(-Math.Pow((u - 0.38) / 0.22, 2));
            svf.Process(rng.Next(), 700 * Math.Pow(4, u), 1 / 1.6, out _, out var band);
            phase += Tau * (260 + 180 * u) / SampleRate;
            s[i] = (float)((band + 0.12 * Math.Sin(phase)) * env);
        }
        return Finish(s, 0.20, 25);
    }

    private static float[] RenderFanOut()
    {
        var s = new float[(int)(SampleRate * 0.075)];
        var rng = new Rng(0xBEEFu);
        var phase = 0.0;
        for (var i = 0; i < s.Length; i++)
        {
            var t = i / (double)SampleRate;
            phase += Tau * 380 * Math.Pow(900.0 / 380, Math.Min(1.0, t / 0.045)) / SampleRate;
            var env = (1 - Math.Exp(-t / 0.0008)) * Math.Exp(-t / 0.022);
            s[i] = (float)((Math.Sin(phase) + 0.15 * Math.Sin(2 * phase)) * env + rng.Next() * Math.Exp(-t / 0.0005) * 0.2);
        }
        return Finish(s, 0.20, 6);
    }

    private static float[] RenderDropCatch()
    {
        var s = new float[(int)(SampleRate * 0.16)];
        var rng = new Rng(0xD0D0u);
        var bp = new Svf();
        var phase = 0.0;
        for (var i = 0; i < s.Length; i++)
        {
            var t = i / (double)SampleRate;
            phase += Tau * (55 + 70 * Math.Exp(-t / 0.018)) / SampleRate;
            var env = Math.Exp(-t / 0.05) * (1 - Math.Exp(-t / 0.0007));
            var body = (Math.Sin(phase) + 0.15 * Math.Sin(2 * phase)) * env;
            var knock = Math.Sin(Tau * 220 * t) * Math.Exp(-t / 0.02) * 0.3;
            bp.Process(rng.Next(), 1200, 1 / 1.2, out _, out var band);
            s[i] = (float)(body + knock + band * Math.Exp(-t / 0.0015) * 0.45);
        }
        return Finish(s, 0.35, 8);
    }

    private static float[] RenderCancel()
    {
        var s = new float[(int)(SampleRate * 0.11)];
        var rng = new Rng(0xCA11u);
        var svf = new Svf();
        for (var i = 0; i < s.Length; i++)
        {
            var t = i / (double)SampleRate;
            svf.Process(rng.Next(), 300 + 1900 * Math.Exp(-t / 0.03), 1 / 0.7, out var low, out _);
            s[i] = (float)(low * (1 - Math.Exp(-t / 0.004)) * Math.Exp(-t / 0.035));
        }
        return Finish(s, 0.18, 10);
    }

    private static float[] RenderError()
    {
        var s = new float[(int)(SampleRate * 0.30)];
        foreach (var (f, start) in new[] { (392.0, 0.0), (311.13, 0.10) })
            for (var i = 0; i < s.Length; i++)
            {
                var tr = i / (double)SampleRate - start;
                if (tr < 0)
                    continue;
                var env = (1 - Math.Exp(-tr / 0.002)) * Math.Exp(-tr / 0.06);
                var w = Math.Sin(Tau * f * tr) + 0.30 * Math.Sin(Tau * 2 * f * tr) + 0.12 * Math.Sin(Tau * 3 * f * tr);
                var am = 1 - 0.25 * (0.5 + 0.5 * Math.Sin(Tau * 38 * tr));
                s[i] += (float)(w * env * am);
            }
        return Finish(s, 0.28, 25);
    }

    private static void AddPartials(float[] buf, int start, double f0, double gain, double[] ratios, double[] amps, double[] taus)
    {
        for (var k = 0; k < ratios.Length; k++)
        {
            var f = f0 * ratios[k];
            if (f > SampleRate * 0.45)
                continue;
            for (var i = 0; start + i < buf.Length; i++)
            {
                var t = i / (double)SampleRate;
                if (t > 10 * taus[k])
                    break;
                buf[start + i] += (float)(gain * amps[k] * (1 - Math.Exp(-t / 0.0006)) * Math.Exp(-t / taus[k]) * Math.Sin(Tau * f * t));
            }
        }
    }

    private static void AddEcho(float[] buf, double delaySeconds, double gain)
    {
        var d = (int)(SampleRate * delaySeconds);
        var src = (float[])buf.Clone();
        for (var i = d; i < buf.Length; i++)
            buf[i] += (float)(gain * src[i - d]);
    }

    private static float[] RenderGlassChime()
    {
        var s = new float[(int)(SampleRate * 1.2)];
        double[] ratios = [1, 2.756, 5.404], amps = [1, 0.35, 0.12], taus = [0.34, 0.14, 0.06];
        foreach (var (f, start, g) in new[] { (587.33, 0.0, 0.8), (739.99, 0.075, 0.9), (880.00, 0.150, 1.0) })
            AddPartials(s, (int)(SampleRate * start), f, g, ratios, amps, taus);
        AddEcho(s, 0.043, 0.28);
        AddEcho(s, 0.089, 0.14);
        return Finish(s, 0.30, 90);
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
        _fastTickPlayer?.Dispose();
        for (var v = 0; v < 3; v++)
            if (_ticks[v] != IntPtr.Zero) Marshal.FreeHGlobal(_ticks[v]);
        if (_flipSound != IntPtr.Zero) Marshal.FreeHGlobal(_flipSound);
        if (_completeChime != IntPtr.Zero) Marshal.FreeHGlobal(_completeChime);
        if (_fanOut != IntPtr.Zero) Marshal.FreeHGlobal(_fanOut);
        if (_dropCatch != IntPtr.Zero) Marshal.FreeHGlobal(_dropCatch);
        if (_cancel != IntPtr.Zero) Marshal.FreeHGlobal(_cancel);
        if (_error != IntPtr.Zero) Marshal.FreeHGlobal(_error);
    }
}
