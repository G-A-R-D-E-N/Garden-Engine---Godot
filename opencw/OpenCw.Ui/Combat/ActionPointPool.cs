using System;

namespace OpenCommonwealth.Services.Combat;

// Requires: main thread only; vitals, when attached, are the single authority for the value and the maximum.
public sealed class ActionPointPool(Func<PlayerVitals?> vitals, Func<double> localMax)
{
    private double _local;

    public double Max => vitals() is { } held ? held.MaxActionPoints : Math.Max(0, localMax());

    public double Value
    {
        get => vitals() is { } held ? held.ActionPoints : Math.Min(_local, Max);
        set
        {
            var next = Math.Clamp(double.IsFinite(value) ? value : 0, 0, Max);
            if (vitals() is { } held) held.AddActionPoints(next - held.ActionPoints);
            else _local = next;
        }
    }
}
