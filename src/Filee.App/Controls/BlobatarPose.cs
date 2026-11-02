using System;

namespace Filee.App.Controls;

/// <summary>
/// Lid: 0 open -&gt; 1 closed (negative = wide). Roll: radians. Brow: + = raised (the renderer lifts the eye).
/// Tuft: - = up. HeadRoll: radians.
/// </summary>
public readonly record struct BlobatarPose(
    float LidL, float LidR, float SquintL, float SquintR,
    float RollL, float RollR, float ScaleL, float ScaleR,
    float BrowL, float BrowR, float HeadRoll,
    float TuftL, float TuftR, float Smile, float Arousal)
{
    public static readonly BlobatarPose Idle = new(0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0.1f);

    public static BlobatarPose operator +(BlobatarPose a, BlobatarPose b) => new(
        a.LidL + b.LidL, a.LidR + b.LidR, a.SquintL + b.SquintL, a.SquintR + b.SquintR,
        a.RollL + b.RollL, a.RollR + b.RollR, a.ScaleL + b.ScaleL, a.ScaleR + b.ScaleR,
        a.BrowL + b.BrowL, a.BrowR + b.BrowR, a.HeadRoll + b.HeadRoll,
        a.TuftL + b.TuftL, a.TuftR + b.TuftR, a.Smile + b.Smile, a.Arousal + b.Arousal);

    public static BlobatarPose operator -(BlobatarPose a, BlobatarPose b) => new(
        a.LidL - b.LidL, a.LidR - b.LidR, a.SquintL - b.SquintL, a.SquintR - b.SquintR,
        a.RollL - b.RollL, a.RollR - b.RollR, a.ScaleL - b.ScaleL, a.ScaleR - b.ScaleR,
        a.BrowL - b.BrowL, a.BrowR - b.BrowR, a.HeadRoll - b.HeadRoll,
        a.TuftL - b.TuftL, a.TuftR - b.TuftR, a.Smile - b.Smile, a.Arousal - b.Arousal);

    public static BlobatarPose operator *(BlobatarPose a, float s) => new(
        a.LidL * s, a.LidR * s, a.SquintL * s, a.SquintR * s,
        a.RollL * s, a.RollR * s, a.ScaleL * s, a.ScaleR * s,
        a.BrowL * s, a.BrowR * s, a.HeadRoll * s,
        a.TuftL * s, a.TuftR * s, a.Smile * s, a.Arousal * s);
}

public enum BlobatarExpression { Curious, Sleepy, Proud, Thinking, Startled, Happy }

public sealed class BlobatarPoseBlender
{
    // Order MUST match BlobatarExpression.
    private static readonly BlobatarPose[] Table =
    {
        BlobatarPose.Idle with { HeadRoll = .06f, ScaleL = 1.03f, ScaleR = .98f, BrowL = 1.0f, BrowR = -.4f,
                                 LidR = .10f, RollL = -.04f, RollR = .06f, TuftL = -.6f, TuftR = -.3f, Arousal = .45f },
        BlobatarPose.Idle with { LidL = .62f, LidR = .62f, RollL = -.14f, RollR = .14f, HeadRoll = -.04f,
                                 TuftL = 2.5f, TuftR = 2.5f, Arousal = 0f },
        BlobatarPose.Idle with { LidL = .38f, LidR = .32f, SquintL = .22f, SquintR = .22f, RollL = .06f, RollR = -.02f,
                                 HeadRoll = -.07f, Smile = .3f, Arousal = .35f },
        BlobatarPose.Idle with { LidL = .12f, LidR = .12f, BrowR = 1.5f, HeadRoll = .04f, Arousal = .25f },
        BlobatarPose.Idle with { LidL = -.10f, LidR = -.10f, ScaleL = 1.18f, ScaleR = 1.18f, Arousal = 1f },
        // No lid: the renderer already squeezes the eyes with Smile.
        BlobatarPose.Idle with { Smile = 1f, TuftL = -2.5f, TuftR = -2.5f, Arousal = .8f },
    };

    private readonly BlobSpring[] _w = new BlobSpring[Table.Length];

    public BlobatarPoseBlender()
    {
        // k = 360 (spec said 110): a celebration must read within ~100 ms (a test checks Smile > 0.4 at 0.1 s).
        for (var i = 0; i < _w.Length; i++) _w[i] = new BlobSpring(360f, .9f);
    }

    public void Set(BlobatarExpression e, float target) => _w[(int)e].Target = Math.Clamp(target, 0f, 1f);
    public float Weight(BlobatarExpression e) => _w[(int)e].X;

    /// <summary>True while any weight is still moving.</summary>
    public bool Step(float dt)
    {
        var moving = false;
        for (var i = 0; i < _w.Length; i++)
        {
            _w[i].Step(dt);
            if (_w[i].Settled(.002f, .02f)) { _w[i].X = _w[i].Target; _w[i].V = 0f; }
            else moving = true;
        }
        return moving;
    }

    public BlobatarPose Current
    {
        get
        {
            var p = BlobatarPose.Idle;
            for (var i = 0; i < _w.Length; i++) p += (Table[i] - BlobatarPose.Idle) * _w[i].X;
            return p;
        }
    }
}

