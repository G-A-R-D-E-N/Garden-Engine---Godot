using System;

namespace OpenCommonwealth.Services.Dialogue;

public readonly record struct MouthBounds(float CenterY, float Height);

public static class JawMorph
{
    private const float LowerStart = 2.5f;
    private const float LowerEnd = 5f;

    public static MouthBounds? Measure(ReadOnlySpan<float> positions)
    {
        if (positions.Length < 3) return null;
        float low = float.MaxValue, high = float.MinValue;
        for (int i = 1; i < positions.Length; i += 3)
        {
            low = Math.Min(low, positions[i]);
            high = Math.Max(high, positions[i]);
        }
        return high > low ? new MouthBounds((low + high) / 2, high - low) : null;
    }

    public static float Weight(float y, MouthBounds mouth)
    {
        float h = mouth.Height;
        float below = Smooth((mouth.CenterY - y) / h + 0.5f);
        float chin = 1 - Smooth((mouth.CenterY - y - LowerStart * h) / ((LowerEnd - LowerStart) * h));
        return below * chin;
    }

    public static float[] Target(ReadOnlySpan<float> positions, MouthBounds mouth, float drop)
    {
        var target = positions.ToArray();
        for (int i = 1; i < target.Length; i += 3)
            target[i] -= drop * Weight(target[i], mouth);
        return target;
    }

    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}
