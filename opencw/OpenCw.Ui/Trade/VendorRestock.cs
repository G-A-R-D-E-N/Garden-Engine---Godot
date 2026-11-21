using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Trade;

// Requires: main thread; stamps are game hours of the last stock reset per vendor chest and are not saved, so a load starts a new interval.
public sealed class VendorRestock
{
    public const double IntervalHours = 48;

    private readonly Dictionary<string, double> _stamps = new(StringComparer.OrdinalIgnoreCase);

    public static VendorRestock Current { get; } = new();

    public bool Has(string chestKey) => _stamps.ContainsKey(chestKey);

    public bool Due(string chestKey, double nowHours) =>
        double.IsFinite(nowHours) && _stamps.TryGetValue(chestKey, out var at) && (nowHours - at >= IntervalHours || nowHours < at);

    public void Mark(string chestKey, double nowHours)
    {
        if (double.IsFinite(nowHours)) _stamps[chestKey] = nowHours;
    }

    public void Clear() => _stamps.Clear();
}
