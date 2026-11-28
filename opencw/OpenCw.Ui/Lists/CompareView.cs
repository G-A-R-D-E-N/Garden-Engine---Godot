using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Lists;

public sealed record CompareStat<TItem>(string Name, Func<TItem, double> Value, bool HigherIsBetter);

public sealed record StatComparison(string Name, double Left, double Right, bool HigherIsBetter)
{
    public double Delta => Right - Left;

    private double RightGain => HigherIsBetter ? Right - Left : Left - Right;

    public bool RightIsBetter => RightGain > 0;

    public bool RightIsWorse => RightGain < 0;
}

public sealed class CompareView<TItem>
{
    public CompareView(TItem left, TItem right, IReadOnlyList<CompareStat<TItem>> stats)
    {
        Rows = stats
            .Select(stat => new StatComparison(stat.Name, stat.Value(left), stat.Value(right), stat.HigherIsBetter))
            .ToArray();
    }

    public IReadOnlyList<StatComparison> Rows { get; }
}
