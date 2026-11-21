using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Lists;

public sealed record SortKey<TItem>(string Name, Comparison<TItem> Compare)
{
    public static SortKey<TItem> By<TValue>(string name, Func<TItem, TValue> select)
        where TValue : IComparable<TValue>
    {
        var comparer = Comparer<TValue>.Default;
        return new SortKey<TItem>(name, (a, b) => comparer.Compare(select(a), select(b)));
    }
}
