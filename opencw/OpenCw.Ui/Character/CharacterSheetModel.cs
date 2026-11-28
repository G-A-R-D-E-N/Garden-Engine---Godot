using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed record DerivedStat(string Id, string Label, Func<IReadOnlyDictionary<string, double>, double> Formula,
                                 bool HigherIsBetter = true);

public enum SheetChange { None, Better, Worse }

public sealed record SheetRow(string Id, string Label, double Current, double After, double Delta, SheetChange Change)
{
    public bool Changed => Change != SheetChange.None;
}

public sealed class CharacterSheetModel
{
    private const double Epsilon = 1e-9;

    private readonly DerivedStat[] _derived;
    private readonly Dictionary<string, double> _base;
    private Dictionary<string, double> _pending = new();

    public CharacterSheetModel(IReadOnlyList<DerivedStat> derived, IReadOnlyDictionary<string, double> baseInputs)
    {
        _derived = derived.ToArray();
        if (_derived.Select(d => d.Id).Distinct().Count() != _derived.Length)
            throw new ArgumentException("Derived stat ids must be unique.", nameof(derived));
        _base = new Dictionary<string, double>(baseInputs);
    }

    public event Action? Changed;

    public IReadOnlyList<SheetRow> Rows
    {
        get
        {
            var after = Merge();
            var view = new CompareView<IReadOnlyDictionary<string, double>>(
                _base, after, _derived.Select(d => new CompareStat<IReadOnlyDictionary<string, double>>(d.Id, d.Formula, d.HigherIsBetter)).ToArray());
            return view.Rows.Select((row, i) => ToRow(_derived[i], row)).ToArray();
        }
    }

    public void SetPending(IReadOnlyDictionary<string, double> overrides)
    {
        _pending = new Dictionary<string, double>(overrides);
        Changed?.Invoke();
    }

    public void SetPending(IReadOnlyDictionary<string, int> overrides) =>
        SetPending(overrides.ToDictionary(p => p.Key, p => (double)p.Value));

    private Dictionary<string, double> Merge()
    {
        var merged = new Dictionary<string, double>(_base);
        foreach (var (key, value) in _pending) merged[key] = value;
        return merged;
    }

    private static SheetRow ToRow(DerivedStat stat, StatComparison row)
    {
        var change = Math.Abs(row.Delta) <= Epsilon ? SheetChange.None : row.RightIsBetter ? SheetChange.Better : SheetChange.Worse;
        return new SheetRow(stat.Id, stat.Label, row.Left, row.Right, row.Delta, change);
    }
}
