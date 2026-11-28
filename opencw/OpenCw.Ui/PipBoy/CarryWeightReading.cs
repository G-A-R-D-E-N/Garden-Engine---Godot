using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.PipBoy;

// Requires: main thread only; the footer polls every frame, so record weights are read again only when the carried stacks change.
public sealed class CarryWeightReading(Func<IReadOnlyList<InventoryStack>> stacks, Func<IItemRecords?> items)
{
    private string? _held;
    private double? _weight;

    public double? Weight
    {
        get
        {
            var now = stacks();
            var key = string.Join('|', now.Select(stack => stack.Id + "*" + stack.Count));
            if (key == _held) return _weight;
            _held = key;
            _weight = items() is { } records ? PlayerStatusFacts.Weight(now, records) : null;
            return _weight;
        }
    }
}
