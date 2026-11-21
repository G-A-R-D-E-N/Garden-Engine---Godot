namespace OpenCommonwealth.Services.Ui.Swf;

public enum SwfBlend { Mix, Add, Sub, Mul }

public readonly record struct SwfFilter(byte Id, bool Inner, bool Knockout, byte Passes, uint Color, double BlurX, double BlurY, double Angle, double Distance, double Strength)
{
    public const byte DropShadow = 0;
    public const byte Glow = 2;
}

public sealed record SwfEffects(bool HasColor, int[] Multiply, int[] Add, byte? BlendMode, IReadOnlyList<SwfFilter> Filters)
{
    public static readonly SwfEffects None = new(false, new[] { 256, 256, 256, 256 }, new[] { 0, 0, 0, 0 }, null, Array.Empty<SwfFilter>());

    public (float R, float G, float B, float A) Tint => (Multiply[0] / 256f, Multiply[1] / 256f, Multiply[2] / 256f, Multiply[3] / 256f);

    public bool HasTint => HasColor && Multiply.Any(m => m != 256);

    public (float R, float G, float B, float A) Offset => (Add[0] / 255f, Add[1] / 255f, Add[2] / 255f, Add[3] / 255f);

    // Constraint: blend mode and filters are per placement, not concatenated by Radicle, so a leaf takes them from the nearest sprite above it when it has none.
    public SwfEffects Inherit(SwfEffects above) =>
        new(HasColor, Multiply, Add, BlendMode ?? above.BlendMode, Filters.Count > 0 ? Filters : above.Filters);

    public bool HasOffset => HasColor && Add.Any(a => a != 0);

    // Constraint: SWF blend mode numbers; Godot has only mix, add, sub and mul, so screen maps to add and every other mode to mix.
    public SwfBlend Blend => BlendMode switch { 3 => SwfBlend.Mul, 4 or 8 => SwfBlend.Add, 9 => SwfBlend.Sub, _ => SwfBlend.Mix };
}
