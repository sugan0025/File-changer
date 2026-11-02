// Shared spring and math helpers for the Blobatar physics (BlobatarPhysics, CircadianLook, BlobatarPose).
// Named BlobSpring / BlobMath so they never collide with DonutMenu's private Spring struct.

using System;

namespace Filee.App.Controls;

public static class BlobMath
{
    public const float Tau = MathF.PI * 2f;

    /// <summary>Smoothstep. Reversed edges (a &gt; b) are allowed.</summary>
    public static float Sm(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

/// <summary>Semi-implicit Euler spring. Step with dt &lt;= 1/120 s (the physics runs a fixed 120 Hz sub-step).</summary>
public struct BlobSpring
{
    public float X, V, Target, K, D;

    public BlobSpring(float k, float zeta, float x0 = 0f)
    {
        K = k;
        D = 2f * zeta * MathF.Sqrt(k);
        X = Target = x0;
        V = 0f;
    }

    public void Tune(float k, float zeta)
    {
        K = k;
        D = 2f * zeta * MathF.Sqrt(k);
    }

    public void Step(float dt)
    {
        V += (-K * (X - Target) - D * V) * dt;
        X += V * dt;
    }

    public void Kick(float dv) => V += dv;

    public void Snap(float x)
    {
        X = Target = x;
        V = 0f;
    }

    public readonly bool Settled(float tol, float vtol) => MathF.Abs(X - Target) < tol && MathF.Abs(V) < vtol;
}
