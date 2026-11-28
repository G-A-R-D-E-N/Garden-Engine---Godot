using System;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Ui.Records;

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

    private static DirectionalSpeeds D(MovementRow row) => new(M(row.Walk), M(row.Run), M(row.Sprint));

    private static MovementSpeeds Scale(MovementTypeSpeeds units) =>
        new(D(units.Forward), D(units.Back), D(units.Left), D(units.Right));

    public static readonly MovementSpeeds Default = Scale(VanillaMovementSlots.Default.Units);

    public static readonly MovementSpeeds Sneak = Scale(VanillaMovementSlots.Sneak.Units);

    public static readonly MovementSpeeds Swim = Scale(VanillaMovementSlots.Swim.Units);

    public static readonly MovementSpeeds Sighted = Scale(VanillaMovementSlots.Sighted.Units);

    public static readonly MovementSpeeds PowerArmor = Scale(VanillaMovementSlots.PowerArmor.Units);

    public static readonly MovementSet Table = new(Default, Sneak, Swim, Sighted, PowerArmor);

    public static MovementSet Read(IRecordReader reader, Action<string>? log = null)
    {
        var units = VanillaMovementSlots.ReadAll(reader, log);
        return new(
            Scale(units.Default),
            Scale(units.Sneak),
            Scale(units.Swim),
            Scale(units.Sighted),
            Scale(units.PowerArmor));
    }
}

public sealed record MovementSet(MovementSpeeds Default, MovementSpeeds Sneak, MovementSpeeds Swim, MovementSpeeds Sighted, MovementSpeeds PowerArmor)
{
    public MovementSpeeds Select(bool swimming, bool sneaking, bool aiming, bool powerArmor) =>
        swimming ? Swim
        : sneaking ? Sneak
        : aiming ? Sighted
        : powerArmor ? PowerArmor
        : Default;
}
