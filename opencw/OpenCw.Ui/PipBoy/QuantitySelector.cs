using System;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public enum QuantityStep
{
    Fine,
    Medium,
    Coarse,
}

public sealed class QuantitySelector
{
    public const string Title = "HOW MANY?";

    public QuantitySelector(string itemId, int stack, int revision)
    {
        ItemId = itemId;
        Max = Math.Max(stack, 1);
        Revision = revision;
        Value = Max;
    }

    public string ItemId { get; }

    public int Max { get; }

    public int Revision { get; }

    public int Value { get; private set; }

    public int StepSize(QuantityStep step) => step switch
    {
        QuantityStep.Fine => 1,
        QuantityStep.Medium => Math.Max(Max / 20, 1),
        _ => Math.Max(Max / 4, 1),
    };

    public bool Step(int direction, QuantityStep step) => Set(Value + Math.Sign(direction) * StepSize(step));

    public bool Set(int value)
    {
        var next = Math.Clamp(value, 1, Max);
        if (next == Value) return false;
        Value = next;
        return true;
    }
}
