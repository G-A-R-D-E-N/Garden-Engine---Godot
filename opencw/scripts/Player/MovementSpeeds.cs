using System;
using OpenCommonwealth.Services;

namespace OpenCommonwealth;

public enum MoveTier { Walk, Run, Sprint }

public readonly struct DirectionalSpeeds
{
    public readonly float Walk;
    public readonly float Run;
    public readonly float Sprint;

    public DirectionalSpeeds(float walk, float run, float sprint)
    {
        Walk = walk;
        Run = run;
        Sprint = sprint;
    }

    public float this[MoveTier tier] => tier switch
    {
        MoveTier.Sprint => Sprint > 0f ? Sprint : Run,
        MoveTier.Run => Run,
        _ => Walk,
    };
}

public readonly struct MovementSpeeds
{
    public readonly DirectionalSpeeds Forward;
    public readonly DirectionalSpeeds Back;
    public readonly DirectionalSpeeds Left;
    public readonly DirectionalSpeeds Right;

    public MovementSpeeds(DirectionalSpeeds forward, DirectionalSpeeds back, DirectionalSpeeds left, DirectionalSpeeds right)
    {
        Forward = forward;
        Back = back;
        Left = left;
        Right = right;
    }

    public float Speed(float strafe, float forward, MoveTier tier)
    {
        float sx = (strafe >= 0f ? Right : Left)[tier];
        float sy = (forward >= 0f ? Forward : Back)[tier];
        float ax = sx > 0f ? strafe / sx : 0f;
        float ay = sy > 0f ? forward / sy : 0f;
        float d = MathF.Sqrt(ax * ax + ay * ay);
        return d > 0f ? 1f / d : 0f;
    }
}

public static class VanillaMovement
{
    private static float M(float units) => units * WorldScale.Fo4ToGodot;

    private static DirectionalSpeeds D(float walk, float run, float sprint) =>
        new(M(walk), M(run), M(sprint));

    public static readonly MovementSpeeds Default = new(
        D(80.1f, 370f, 500f),
        D(71.93f, 260f, 0f),
        D(80.09f, 370f, 0f),
        D(79.75f, 370f, 0f));

    public static readonly MovementSpeeds Sneak = new(
        D(90f, 205f, 388.5f),
        D(90f, 205f, 0f),
        D(90f, 205f, 0f),
        D(90f, 205f, 0f));

    public static readonly MovementSpeeds Swim = new(
        D(80.1f, 370f, 500f),
        D(71.93f, 205.25f, 0f),
        D(80.09f, 370f, 0f),
        D(79.75f, 370f, 0f));

    public static readonly MovementSpeeds Sighted = new(
        D(100.76f, 200f, 350f),
        D(100.76f, 200f, 0f),
        D(100.76f, 200f, 0f),
        D(100.76f, 200f, 0f));

    public static readonly MovementSpeeds PowerArmor = new(
        D(112.48f, 311.66f, 497.06f),
        D(112.48f, 311.66f, 0f),
        D(112.48f, 311.66f, 0f),
        D(112.48f, 311.66f, 0f));
}
