using System;
using System.IO;
using System.Runtime.InteropServices;
using Filee.App.Services;
using Filee.Core.Settings;

namespace Filee.App.Controls;

/// <summary>
/// Procedural cues for Blobatar, rendered to PCM and played with winmm PlaySound (same approach as SoundService, so
/// no new dependency). Windows only; silent elsewhere. Respects Settings -&gt; Sound effects.
/// Day and night share one pitch set {D, E, F#, A, B}; only the tonic changes, so a cue never clashes across 19:00.
/// </summary>
public sealed class BlobatarAudio
{
    public static BlobatarAudio Shared { get; } = new();

    private const int Fs = 48000;
    private const float DayTonic = 587.33f, NightTonic = 493.88f;    // D5 major pent. / B4 minor pent.
    private static readonly int[] DayDeg = { 0, 2, 4, 7, 9 }, NightDeg = { 0, 3, 5, 7, 10 };

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

    private readonly IntPtr[] _ring = new IntPtr[3];     // PlaySound is async: a buffer is freed 3 plays later
    private readonly Random _rng = new();
    private int _ringIdx;
    private long _last;
    private float _wm, _wa = 1f, _wn;

    public float Volume { get; set; } = .8f;
    public bool Muted { get; set; }

    public void SetTimeOfDay(float wm, float wa, float wn) { _wm = wm; _wa = wa; _wn = wn; }

    private bool Night => _wn > .5f;
    private float Hz(float st) => (Night ? NightTonic : DayTonic) * MathF.Pow(2f, st / 12f);
    private float H2 => _wm * .08f + _wa * .25f + _wn * .02f;      // glassy -&gt; warm -&gt; pure
    private float Level => Volume * (Night ? .5f : 1f);              // night: -6 dB

    public void Handle(BlobatarEvent e, float x)
    {
        if (Muted || !OperatingSystem.IsWindows()) return;
        var store = AppHost.TryGet<UserDataStore>();
        if (store is null || !store.Settings.SoundEffects) return;
        var now = Environment.TickCount64;
        if (e != BlobatarEvent.Celebrate && now - _last < 30) return;
        _last = now;
        var pcm = Render(e, x);
        if (pcm is not null) Play(pcm);
    }

    private float[]? Render(BlobatarEvent e, float x)
    {
        float[] B(float sec) => new float[(int)(Fs * sec)];
        float[] b;
        switch (e)
        {
            case BlobatarEvent.Wink:
                b = B(.15f); Tone(b, 0, Hz(7), 2f, .06f, .11f, .04f, 1f, H2); return Fin(b, .30f);
            case BlobatarEvent.Press:
                b = B(.1f); Tone(b, 0, Hz(-12), -3f, .05f, .06f, .03f, 1f, 0f); return Fin(b, .25f);
            case BlobatarEvent.Squish:
                b = B(.2f); Noise(b, 0, .03f, .35f);
                Tone(b, 0, Hz(0), -(5f + 4f * Math.Clamp(x, 0f, 1.2f)), .09f, .12f, .05f, 1f, H2); return Fin(b, .35f);
            case BlobatarEvent.Jump:
                b = B(.2f); Tone(b, 0, Hz(0), 12f, .08f, .14f, .08f, 1f, H2); return Fin(b, .30f);
            case BlobatarEvent.Land:
                b = B(.2f); Tone(b, 0, 110f, -7.8f, .12f, .15f, .04f, 1f, 0f);
                return Fin(b, Math.Clamp(x / 150f, .05f, 1f) * .45f);
            case BlobatarEvent.Curious:
                b = B(.25f); Tone(b, 0, Hz(7), Night ? 3f : 2f, .16f, .2f, .1f, 1f, H2); return Fin(b, .28f);
            case BlobatarEvent.Proud:
                b = B(.5f); Tone(b, 0, Hz(7), 0f, 0f, .18f, .08f, 1f, H2);
                Tone(b, .12f, Hz(12), 0f, 0f, .30f, .12f, 1f, H2); return Fin(b, .30f);
            case BlobatarEvent.Celebrate:
                return Celebrate();
            case BlobatarEvent.ThinkTick:
                b = B(.02f); Tone(b, 0, 1200f, 0f, 0f, .01f, .003f, 1f, 0f); return Fin(b, .06f);
            case BlobatarEvent.Yawn:
                b = B(.6f); Tone(b, 0, Hz(7), -12f, .5f, .55f, .25f, 1f, 0f); LowPass(b, 1200f); return Fin(b, .25f);
            default: return null;
        }
    }

    private float[] Celebrate()
    {
        var b = new float[(int)(Fs * 1.4f)];
        var n = Night ? new[] { 0f, 3f, 7f, 12f, 15f } : new[] { 0f, 4f, 7f, 12f, 16f };
        for (var i = 0; i < 4; i++) Tone(b, i * .07f, Hz(n[i]), 0f, 0f, .16f, .07f, 1f, H2);
        Tone(b, 4 * .07f, Hz(n[4]), 0f, 0f, .5f, .16f, 1f, H2, .12f, 3f);         // last note: chorus + 3f partial
        var deg = Night ? NightDeg : DayDeg;                                          // sparkles: +24 st, pentatonic only
        for (var i = 0; i < 3; i++)
            Tone(b, .45f + i * .17f + (float)_rng.NextDouble() * .1f, Hz(deg[_rng.Next(deg.Length)] + 24f), 0f, 0f, .07f, .025f, .35f, 0f);
        if (Night) LowPass(b, 2500f);
        return Fin(b, .32f);
    }

    // ---------------- DSP ----------------

    private static void Tone(float[] b, float start, float f0, float glideSt, float glideT, float dur, float tau,
        float amp, float h2, float h3 = 0f, float cents = 0f)
    {
        var i0 = (int)(start * Fs); var n = (int)(dur * Fs);
        double p1 = 0, p2 = 0;
        for (var i = 0; i < n && i0 + i < b.Length; i++)
        {
            var t = i / (float)Fs;
            var g = glideT > 0f ? MathF.Min(t / glideT, 1f) : 1f; g = 1f - (1f - g) * (1f - g);
            var f = f0 * MathF.Pow(2f, glideSt * g / 12f);
            p1 += BlobMath.Tau * f / Fs;
            var s = Wave(p1, h2, h3);
            if (cents > 0f) { p2 += BlobMath.Tau * f * MathF.Pow(2f, cents / 1200f) / Fs; s = .5f * (s + Wave(p2, h2, h3)); }
            var env = MathF.Min(t / .003f, 1f) * MathF.Exp(-t / tau) * MathF.Min(1f, (dur - t) / .005f);
            b[i0 + i] += s * env * amp;
        }
    }

    private static float Wave(double p, float h2, float h3) =>
        (float)(Math.Sin(p) + h2 * Math.Sin(2 * p) + h3 * Math.Sin(3 * p));

    private void Noise(float[] b, float start, float dur, float amp)
    {
        var i0 = (int)(start * Fs); var n = (int)(dur * Fs); float y = 0;
        for (var i = 0; i < n && i0 + i < b.Length; i++)
        {
            y += .35f * (((float)_rng.NextDouble() * 2f - 1f) - y);
            b[i0 + i] += y * MathF.Exp(-(i / (float)Fs) / .008f) * amp;
        }
    }

    private static void LowPass(float[] b, float cutoff)
    {
        var a = 1f - MathF.Exp(-BlobMath.Tau * cutoff / Fs); float y = 0;
        for (var i = 0; i < b.Length; i++) { y += a * (b[i] - y); b[i] = y; }
    }

    /// <summary>Normalises to the cue's peak (scaled by volume / night level) with short fades.</summary>
    private float[] Fin(float[] s, float peak)
    {
        var max = 0f;
        foreach (var v in s) max = MathF.Max(max, MathF.Abs(v));
        if (max <= 0f) return s;
        var k = peak * Level / max;
        var rin = (int)(Fs * .0003f); var rout = (int)(Fs * .008f);
        for (var i = 0; i < s.Length; i++)
        {
            var v = s[i] * k;
            if (i < rin) v *= i / (float)rin;
            var j = s.Length - 1 - i;
            if (j < rout) v *= .5f * (1f - MathF.Cos(MathF.PI * j / rout));
            s[i] = v;
        }
        return s;
    }

    private void Play(float[] pcm)
    {
        var wav = CreateWav(pcm);
        var ptr = Marshal.AllocHGlobal(wav.Length);
        Marshal.Copy(wav, 0, ptr, wav.Length);
        var old = _ring[_ringIdx];
        _ring[_ringIdx] = ptr; _ringIdx = (_ringIdx + 1) % _ring.Length;
        try { PlaySound(ptr, IntPtr.Zero, 0x0001 | 0x0002 | 0x0004); } catch { }    // ASYNC | NODEFAULT | MEMORY
        if (old != IntPtr.Zero) Marshal.FreeHGlobal(old);
    }

    private static byte[] CreateWav(float[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var data = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8); w.Write("fmt "u8);
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Fs); w.Write(Fs * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data);
        foreach (var s in samples) w.Write((short)(Math.Clamp(s, -1f, 1f) * 32767));
        return ms.ToArray();
    }
}

