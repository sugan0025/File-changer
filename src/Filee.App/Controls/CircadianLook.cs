using System;
using System.Numerics;
using Avalonia.Media;
using Filee.App.Services;
using Filee.Core.Settings;

namespace Filee.App.Controls;

public readonly record struct CircadianLookFrame(
    Vector2 Light,          // unit vector TOWARD the light (screen space, y down)
    float Intensity,        // ambient brightness: ~0.28 night .. 1 day (drives pupil size, glint strength)
    float Gain,             // specular gain from CircadianService (0.7 afternoon .. 1.25 night)
    bool Active,            // false = Circadian Off: legacy flat look
    float Wm, float Wa, float Wn, float Wz,   // morning / afternoon / night / neutral weights (sum = 1)
    Color Hi, Color Mid, Color Lo, Color Rim, Color Sky, Color ShadowColor,
    int CacheKey);

public readonly record struct BlobatarShadow(float Dx, float Rx, float Ry, float Alpha, float CoreAlpha, Color Color);

/// <summary>
/// Resolves Blobatar's palette, key light and shadow from CircadianService + the current tone.
/// Phase weights and the light vector are low-passed (k = 6, critically damped) so scrubbing the clock never pops.
/// </summary>
public sealed class CircadianLook
{
    private BlobSpring _wm, _wa, _wn, _wz, _lx, _ly, _gain;
    private bool _init;
    private Color _hi, _mid, _lo, _rim, _sky, _sh;

    public CircadianLookFrame Current { get; private set; }

    /// <summary>True while weights, light or colours are still converging (keeps the frame loop alive).</summary>
    public bool Transitioning { get; private set; }

    public CircadianLook()
    {
        _wm = _wa = _wn = _wz = new BlobSpring(6f, 1f);
        _lx = _ly = _gain = new BlobSpring(6f, 1f);
    }

    public void Update(float dt, CircadianService? svc, BlobatarTone tone)
    {
        var active = svc is { IsActive: true };
        var phase = svc?.CurrentPhase ?? CircadianPhase.Night;
        float tm = active && phase == CircadianPhase.Morning ? 1f : 0f;
        float ta = active && phase == CircadianPhase.Afternoon ? 1f : 0f;
        float tn = active && phase == CircadianPhase.Night ? 1f : 0f;
        float tz = active ? 0f : 1f;

        // The service's angle is a slice-angle (donut convention). Take the horizontal side from it and always light
        // from above, so highlights never land on the belly.
        var th = active ? (float)svc!.CurrentKeyLightAngle : 0f;
        var lt = active ? Vector2.Normalize(new Vector2(MathF.Cos(th), -.6f)) : new Vector2(0f, -1f);
        var gt = active ? (float)Math.Clamp(svc!.CurrentSpecularGain, .4, 1.4) : 1f;

        if (!_init)
        {
            _wm.Snap(tm); _wa.Snap(ta); _wn.Snap(tn); _wz.Snap(tz);
            _lx.Snap(lt.X); _ly.Snap(lt.Y); _gain.Snap(gt);
            _init = true;
        }
        _wm.Target = tm; _wa.Target = ta; _wn.Target = tn; _wz.Target = tz;
        _lx.Target = lt.X; _ly.Target = lt.Y; _gain.Target = gt;
        var h = MathF.Min(dt, .1f);
        _wm.Step(h); _wa.Step(h); _wn.Step(h); _wz.Step(h); _lx.Step(h); _ly.Step(h); _gain.Step(h);

        var moving = !(_wm.Settled(.002f, .01f) && _wa.Settled(.002f, .01f) && _wn.Settled(.002f, .01f) &&
                       _wz.Settled(.002f, .01f) && _lx.Settled(.002f, .01f) && _ly.Settled(.002f, .01f) &&
                       _gain.Settled(.002f, .01f));

        float wm = Math.Clamp(_wm.X, 0f, 1f), wa = Math.Clamp(_wa.X, 0f, 1f);
        float wn = Math.Clamp(_wn.X, 0f, 1f), wz = Math.Clamp(_wz.X, 0f, 1f);
        var sum = wm + wa + wn + wz;
        if (sum > 1e-4f) { wm /= sum; wa /= sum; wn /= sum; wz /= sum; }

        var light = new Vector2(_lx.X, _ly.X);
        light = light.LengthSquared() > 1e-6f ? Vector2.Normalize(light) : new Vector2(0f, -1f);
        var intensity = wm * .85f + wa * 1f + wn * .28f + wz * 1f;

        var ti = (int)tone;
        var m = Pal[ti, 0]; var a = Pal[ti, 1]; var n = Pal[ti, 2]; var z = Pal[ti, 3];
        var blend = _colInit ? 1f - MathF.Exp(-12f * MathF.Min(dt, .1f)) : 1f;
        _colInit = true;
        var changed = false;
        changed |= Approach(ref _hi, Mix(m.Hi, a.Hi, n.Hi, z.Hi, wm, wa, wn, wz), blend);
        changed |= Approach(ref _mid, Mix(m.Mid, a.Mid, n.Mid, z.Mid, wm, wa, wn, wz), blend);
        changed |= Approach(ref _lo, Mix(m.Lo, a.Lo, n.Lo, z.Lo, wm, wa, wn, wz), blend);
        changed |= Approach(ref _rim, Mix(m.Rim, a.Rim, n.Rim, z.Rim, wm, wa, wn, wz), blend);
        changed |= Approach(ref _sky, Mix(m.Sky, a.Sky, n.Sky, z.Sky, wm, wa, wn, wz), blend);
        changed |= Approach(ref _sh, Mix(m.Shadow, a.Shadow, n.Shadow, z.Shadow, wm, wa, wn, wz), blend);
        Transitioning = moving || changed;

        const uint Q = 0xFCFCFCFCu;
        var qa = (int)((MathF.Atan2(light.Y, light.X) + MathF.PI) / BlobMath.Tau * 63f);
        var key = HashCode.Combine(
            HashCode.Combine(_hi.ToUInt32() & Q, _mid.ToUInt32() & Q, _lo.ToUInt32() & Q, _rim.ToUInt32() & Q),
            HashCode.Combine(_sky.ToUInt32() & Q, qa, (int)(_gain.X * 16f), (int)(intensity * 16f)));

        Current = new CircadianLookFrame(light, intensity, _gain.X, active, wm, wa, wn, wz,
            _hi, _mid, _lo, _rim, _sky, _sh, key);
    }

    private bool _colInit;

    /// <summary>Contact shadow. sx / sy = body scale, hop = height in viewBox units. At night it becomes a neon underglow.</summary>
    public static BlobatarShadow Shadow(in CircadianLookFrame l, float sx, float sy, float hop)
    {
        var dx = Math.Clamp(-l.Light.X / MathF.Max(-l.Light.Y, .25f), -2f, 2f) * 6f;
        var rx = (24f + 5f * MathF.Abs(dx) / 6f) * sx * (1f + .4f * l.Wn);
        var ry = 4.5f / sy;
        var lift = MathF.Max(0f, 1f - hop / 40f);
        var a = BlobMath.Lerp(.16f + .22f * l.Intensity, .40f, l.Wn) * lift;
        var core = .5f * l.Wn * lift;
        return new BlobatarShadow(dx, rx, ry, a, core, l.ShadowColor);
    }

    // ---------------- palettes ----------------

    private readonly record struct Palette(Color Hi, Color Mid, Color Lo, Color Rim, Color Sky, Color Shadow);

    private static readonly Color[] Anchor =
    {
        Color.Parse("#8471E2"), Color.Parse("#A499F7"), Color.Parse("#CECAFF"), Color.Parse("#D6D5E9"), Color.Parse("#D1CEFF"),
    };
    private static readonly Color[] DayRim = { Color.Parse("#EAF4FF"), Color.Parse("#FFE3B8") };
    private static readonly Color[] DaySky = { Color.Parse("#BFD9FF"), Color.Parse("#FFD2A6") };
    private static readonly Color[] DayShadow = { Color.Parse("#3A3C8F"), Color.Parse("#5B2C7F") };
    private static readonly Color[] NightRim =
    {
        Color.Parse("#8C7CFF"), Color.Parse("#C0A8FF"), Color.Parse("#6EF0FF"), Color.Parse("#A9B7FF"), Color.Parse("#FF8AE6"),
    };

    // [tone, bucket]  bucket: 0 morning (icy amethyst), 1 afternoon (radiant lilac), 2 night (neon obsidian), 3 neutral (legacy)
    private static readonly Palette[,] Pal = BuildPalettes();

    private static Palette[,] BuildPalettes()
    {
        var p = new Palette[5, 4];
        for (var t = 0; t < 5; t++)
        {
            for (var b = 0; b < 3; b++) p[t, b] = Derive(t, b);
            p[t, 3] = Neutral(Anchor[t]);
        }
        p[0, 0] = p[0, 0] with { Hi = Color.Parse("#D3D6FF"), Mid = Color.Parse("#9A92EE"), Lo = Color.Parse("#6C69CF") };
        p[0, 1] = p[0, 1] with { Hi = Color.Parse("#F0D6FA"), Mid = Color.Parse("#B38CEB"), Lo = Color.Parse("#8660CC") };
        p[0, 2] = p[0, 2] with { Hi = Color.Parse("#4A3FB5"), Mid = Color.Parse("#221A55"), Lo = Color.Parse("#0A0720") };
        return p;
    }

    private static Palette Neutral(Color c)
    {
        static byte Up(byte v) => (byte)Math.Clamp(v + (255 - v) * .14, 0, 255);
        static byte Dn(byte v) => (byte)Math.Clamp(v * .82, 0, 255);
        return new Palette(Color.FromRgb(Up(c.R), Up(c.G), Up(c.B)), c, Color.FromRgb(Dn(c.R), Dn(c.G), Dn(c.B)),
            Colors.White, Colors.White, Colors.Black);
    }

    private static Palette Derive(int tone, int bucket)
    {
        var (L, C, h) = ToOklch(Anchor[tone]);
        const float Deg = MathF.PI / 180f;
        float lm, cm, hm, hiDL, loDL;
        switch (bucket)
        {
            case 0: lm = L + .035f; cm = C * .82f; hm = h - 14f * Deg; hiDL = .14f; loDL = .16f; break;
            case 1: lm = L + .02f; cm = C * 1.12f; hm = h + 16f * Deg; hiDL = .14f; loDL = .16f; break;
            default: lm = L * .36f; cm = C * 1.30f; hm = h - 4f * Deg; hiDL = .10f; loDL = .12f; break;
        }
        lm = Math.Min(lm, .97f);
        var mid = FromOklch(lm, cm, hm);
        var hi = FromOklch(Math.Min(lm + hiDL, .99f), cm * .8f, hm);
        var lo = FromOklch(Math.Max(lm - loDL, .02f), cm * 1.05f, hm - 10f * Deg);
        return bucket switch
        {
            0 => new Palette(hi, mid, lo, DayRim[0], DaySky[0], DayShadow[0]),
            1 => new Palette(hi, mid, lo, DayRim[1], DaySky[1], DayShadow[1]),
            _ => new Palette(hi, mid, lo, NightRim[tone], Color.Parse("#6EF0FF"), NightRim[tone]),
        };
    }

    private static Color Mix(Color m, Color a, Color n, Color z, float wm, float wa, float wn, float wz)
    {
        static byte Ch(float v) => (byte)Math.Clamp(v + .5f, 0f, 255f);
        return Color.FromRgb(
            Ch(m.R * wm + a.R * wa + n.R * wn + z.R * wz),
            Ch(m.G * wm + a.G * wa + n.G * wn + z.G * wz),
            Ch(m.B * wm + a.B * wa + n.B * wn + z.B * wz));
    }

    private static bool Approach(ref Color cur, Color target, float blend)
    {
        if (cur == target) return false;
        int dr = target.R - cur.R, dg = target.G - cur.G, db = target.B - cur.B;
        if (Math.Abs(dr) <= 1 && Math.Abs(dg) <= 1 && Math.Abs(db) <= 1) { cur = target; return true; }
        static int S(int d, float b) { var s = (int)MathF.Round(d * b); return s == 0 && d != 0 ? Math.Sign(d) : s; }
        cur = Color.FromRgb((byte)(cur.R + S(dr, blend)), (byte)(cur.G + S(dg, blend)), (byte)(cur.B + S(db, blend)));
        return true;
    }

    // ---------------- OKLCH ----------------

    public static (float L, float C, float h) ToOklch(Color c)
    {
        float r = Lin(c.R / 255f), g = Lin(c.G / 255f), b = Lin(c.B / 255f);
        var l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
        var m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
        var s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
        var L = 0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s;
        var a = 1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s;
        var bb = 0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s;
        return (L, MathF.Sqrt(a * a + bb * bb), MathF.Atan2(bb, a));
    }

    public static Color FromOklch(float L, float C, float h)
    {
        float a = C * MathF.Cos(h), b = C * MathF.Sin(h);
        var l = Cube(L + 0.3963377774f * a + 0.2158037573f * b);
        var m = Cube(L - 0.1055613458f * a - 0.0638541728f * b);
        var s = Cube(L - 0.0894841775f * a - 1.2914855480f * b);
        return Color.FromRgb(
            Enc(4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s),
            Enc(-1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s),
            Enc(-0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s));
    }

    private static float Lin(float v) => v <= .04045f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f);

    private static byte Enc(float v)
    {
        v = Math.Clamp(v, 0f, 1f);
        v = v <= .0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1f / 2.4f) - .055f;
        return (byte)(v * 255f + .5f);
    }

    private static float Cube(float x) => x * x * x;
}

