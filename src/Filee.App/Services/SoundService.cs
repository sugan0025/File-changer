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
        private const uint WHDR_DONE = 0x00000001;
        private const uint WHDR_INQUEUE = 0x00000010;
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
                    var isAvailable = (_hdrs[idx].dwFlags & WHDR_INQUEUE) == 0 || (_hdrs[idx].dwFlags & WHDR_DONE) != 0;
                    if (isAvailable)
                    {
                        _ringIndex = (idx + 1) % PoolSize;
                        _hdrs[idx].dwFlags &= ~WHDR_DONE;
                        var res = waveOutWrite(_hWaveOut, ref _hdrs[idx], (uint)_hdrSize);
                        return res == 0;
                    }
                }

                // If all buffers are currently in queue, return false to allow fallback to PlayWindows
                return false;
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
    private readonly IntPtr _nav;
    private readonly IntPtr _buttonClick;
    private readonly IntPtr _tab;
    private readonly IntPtr _confirm;
    private readonly IntPtr _toggleOn;
    private readonly IntPtr _toggleOff;

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
            _nav = AllocateWav(CreateWav(RenderNav(), SampleRate));
            _buttonClick = AllocateWav(CreateWav(RenderButtonClick(), SampleRate));
            _tab = AllocateWav(CreateWav(RenderTab(), SampleRate));
            _confirm = AllocateWav(CreateWav(RenderConfirm(), SampleRate));
            _toggleOn = AllocateWav(CreateWav(RenderToggle(true), SampleRate));
            _toggleOff = AllocateWav(CreateWav(RenderToggle(false), SampleRate));
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

    public void PlayNav()
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows())
            PlayWindows(_nav);
        else if (OperatingSystem.IsMacOS())
            PlayMacSound("Ping");
    }

    public void PlayButtonClick()
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows())
            PlayWindows(_buttonClick);
        else if (OperatingSystem.IsMacOS())
            PlayMacSound("Tink");
    }

    public void PlayTab()
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows())
            PlayWindows(_tab);
        else if (OperatingSystem.IsMacOS())
            PlayMacSound("Pop");
    }

    public void PlayConfirm()
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows())
            PlayWindows(_confirm);
        else if (OperatingSystem.IsMacOS())
            PlayMacSound("Hero");
    }

    public void PlayToggle(bool on)
    {
        if (!_store.Settings.SoundEffects)
            return;

        if (OperatingSystem.IsWindows())
            PlayWindows(on ? _toggleOn : _toggleOff);
        else if (OperatingSystem.IsMacOS())
            PlayMacSound(on ? "Blow" : "Bottle");
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

    private static float[] RenderNav()
    {
        // 50ms modern fluid UI navigation tab transition (warm, resonant, micro-tap)
        var s = new float[(int)(SampleRate * 0.050)];
        var phase = 0.0;
        var bp = new Svf();
        var rng = new Rng(0x51DEB4u);
        for (var i = 0; i < s.Length; i++)
        {
            var t = i / (double)SampleRate;
            var f = 560.0 - 180.0 * Math.Pow(t / 0.050, 0.6);
            phase += Tau * f / SampleRate;
            var env = Math.Sin(Math.PI * Math.Min(1.0, t / 0.050)) * Math.Exp(-t / 0.020);
            var tone = Math.Sin(phase) + 0.20 * Math.Sin(2 * phase);
            bp.Process(rng.Next(), 1900, 1 / 1.5, out _, out var band);
            var noiseTap = band * Math.Exp(-t / 0.002) * 0.35;
            s[i] = (float)(tone * env * 0.85 + noiseTap);
        }
        return Finish(s, 0.26, 6);
    }

    private static float[] RenderButtonClick()
    {
        // 22ms tactile micro-switch button click
        var s = new float[(int)(SampleRate * 0.022)];
        var phase = 0.0;
        var bp = new Svf();
        var rng = new Rng(0xB0770u);
        for (var i = 0; i < s.Length; i++)
        {
            var t = i / (double)SampleRate;
            var f = 1100.0 * Math.Exp(-t / 0.004);
            phase += Tau * f / SampleRate;
            var env = Math.Exp(-t / 0.007);
            bp.Process(rng.Next(), 2600, 1 / 1.4, out _, out var band);
            s[i] = (float)((Math.Sin(phase) * 0.5 + band * 0.65) * env);
        }
        return Finish(s, 0.22, 3);
    }

    private static float[] RenderTab()
    {
        // 28ms gentle pill/tab switch pop
        var s = new float[(int)(SampleRate * 0.028)];
        var phase = 0.0;
        for (var i = 0; i < s.Length; i++)
        {
            var t = i / (double)SampleRate;
            phase += Tau * 640 * Math.Exp(-t / 0.010) / SampleRate;
            var env = Math.Exp(-t / 0.009);
            s[i] = (float)(Math.Sin(phase) * env);
        }
        return Finish(s, 0.18, 4);
    }

    private static float[] RenderConfirm()
    {
        // 110ms warm dual-tone confirmation chime
        var s = new float[(int)(SampleRate * 0.11)];
        double[] freqs = [659.25, 880.0];
        foreach (var f in freqs)
        {
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (double)SampleRate;
                var env = (1 - Math.Exp(-t / 0.001)) * Math.Exp(-t / 0.032);
                s[i] += (float)(Math.Sin(Tau * f * t) * env * 0.45);
            }
        }
        return Finish(s, 0.25, 12);
    }

    private static float[] RenderToggle(bool on)
    {
        // 35ms mechanical toggle switch snap
        var s = new float[(int)(SampleRate * 0.035)];
        var phase = 0.0;
        var f0 = on ? 680.0 : 420.0;
        var f1 = on ? 920.0 : 320.0;
        var bp = new Svf();
        var rng = new Rng(on ? 0x010101u : 0x0F0F0Fu);
        for (var i = 0; i < s.Length; i++)
        {
            var t = i / (double)SampleRate;
            var u = t / 0.035;
            var f = f0 + (f1 - f0) * u;
            phase += Tau * f / SampleRate;
            var env = Math.Sin(Math.PI * Math.Pow(u, 0.4)) * Math.Exp(-t / 0.014);
            bp.Process(rng.Next(), on ? 3400 : 2100, 1 / 1.8, out _, out var band);
            s[i] = (float)((Math.Sin(phase) + 0.25 * Math.Sin(2 * phase)) * env * 0.7 + band * Math.Exp(-t / 0.003) * 0.45);
        }
        return Finish(s, 0.24, 4);
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
        if (_nav != IntPtr.Zero) Marshal.FreeHGlobal(_nav);
        if (_buttonClick != IntPtr.Zero) Marshal.FreeHGlobal(_buttonClick);
        if (_tab != IntPtr.Zero) Marshal.FreeHGlobal(_tab);
        if (_confirm != IntPtr.Zero) Marshal.FreeHGlobal(_confirm);
        if (_toggleOn != IntPtr.Zero) Marshal.FreeHGlobal(_toggleOn);
        if (_toggleOff != IntPtr.Zero) Marshal.FreeHGlobal(_toggleOff);
    }
}
