using System;
using Avalonia.Media;

namespace Filee.App.Services;

/// <summary>
/// OKLab color space helper for smooth, perceptually uniform color transitions.
/// Prevents muddy, desaturated intermediate shades during cross-fades.
/// </summary>
public readonly record struct OkLab(float L, float A, float B, float Alpha)
{
    public static OkLab FromColor(Color color)
    {
        // sRGB [0..1]
        float r = SrgbToLinear(color.R / 255f);
        float g = SrgbToLinear(color.G / 255f);
        float b = SrgbToLinear(color.B / 255f);
        float a = color.A / 255f;

        // Linear sRGB to LMS
        float l = 0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b;
        float m = 0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b;
        float s = 0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b;

        float l_ = MathF.Cbrt(l);
        float m_ = MathF.Cbrt(m);
        float s_ = MathF.Cbrt(s);

        return new OkLab(
            0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_,
            1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_,
            0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_,
            a
        );
    }

    public Color ToColor()
    {
        float l_ = L + 0.3963377774f * A + 0.2158037573f * B;
        float m_ = L - 0.1055613458f * A - 0.0638541728f * B;
        float s_ = L - 0.0894841775f * A - 1.2914855480f * B;

        float l = l_ * l_ * l_;
        float m = m_ * m_ * m_;
        float s = s_ * s_ * s_;

        float r = +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
        float g = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
        float b = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;

        byte rByte = (byte)Math.Clamp(MathF.Round(LinearToSrgb(r) * 255f), 0, 255);
        byte gByte = (byte)Math.Clamp(MathF.Round(LinearToSrgb(g) * 255f), 0, 255);
        byte bByte = (byte)Math.Clamp(MathF.Round(LinearToSrgb(b) * 255f), 0, 255);
        byte aByte = (byte)Math.Clamp(MathF.Round(Alpha * 255f), 0, 255);

        return Color.FromArgb(aByte, rByte, gByte, bByte);
    }

    public static Color Lerp(Color c1, Color c2, double t)
    {
        float factor = (float)Math.Clamp(t, 0.0, 1.0);
        var o1 = FromColor(c1);
        var o2 = FromColor(c2);

        var blended = new OkLab(
            o1.L + (o2.L - o1.L) * factor,
            o1.A + (o2.A - o1.A) * factor,
            o1.B + (o2.B - o1.B) * factor,
            o1.Alpha + (o2.Alpha - o1.Alpha) * factor
        );

        return blended.ToColor();
    }

    private static float SrgbToLinear(float c) =>
        c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float LinearToSrgb(float c) =>
        c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(MathF.Max(0f, c), 1f / 2.4f) - 0.055f;
}

