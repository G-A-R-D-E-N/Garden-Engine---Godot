using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.PipBoy;

// Requires: main thread only; the footer polls every frame, so record weights are read again only when the carried stacks change.
public sealed class CarryWeightReading(Func<IReadOnlyList<InventoryStack>> stacks, Func<IItemRecords?> items)
{
    public const int RetryPolls = 120;

    private string? _held;
    private double? _weight;
    private bool _hadRecords;
    private int _polls;

    public double? Weight
    {
        get
        {
            var now = stacks();
            var key = string.Join('|', now.Select(stack => stack.Id + "*" + stack.Count));
            var records = items();
            if (key == _held && (_weight is not null || records is null || (_hadRecords && ++_polls < RetryPolls))) return _weight;
            _held = key;
            _polls = 0;
            _hadRecords = records is not null;
            _weight = records is not null ? PlayerStatusFacts.Weight(now, records) : null;
            return _weight;
        }
    }
}
