using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Ui.Character;

// Requires: main thread only; the total XP is the only saved value and the level is derived from it by the curve, so a curve change never leaves a stale level.
public sealed class PlayerExperience
{
    public const string SectionName = "player.xp.json";
    private const string TotalKey = "total";
    private const string SpentKey = "spent";

    private readonly int[] _spent = new int[ProgressionCatalog.SpecialAttributeCount];

    private XpCurve _curve = XpCurve.Default;

    public static PlayerExperience Current { get; } = new();

    public event Action? Changed;

    public event Action<long>? Gained;

    public long Total { get; private set; }

    public bool IsEmpty => Total == 0 && _spent.All(v => v == 0);

    public IReadOnlyList<int> Spent => _spent;

    public void SpendAttribute(int column)
    {
        if (column < 0 || column >= _spent.Length) return;
        _spent[column]++;
        Changed?.Invoke();
    }

    public int Level { get; private set; } = 1;

    public double Progress => _curve.Progress(Total, Level);

    public XpCurve Curve
    {
        get => _curve;
        set
        {
            if (value == _curve) return;
            _curve = value;
            Level = _curve.LevelAt(Total);
            Changed?.Invoke();
        }
    }

    // Invariant: only a positive amount counts, and the total saturates at long.MaxValue.
    public bool Add(long amount)
    {
        if (amount <= 0) return false;
        Total = amount > long.MaxValue - Total ? long.MaxValue : Total + amount;
        Level = _curve.LevelAt(Total);
        Changed?.Invoke();
        Gained?.Invoke(amount);
        return true;
    }

    public void Clear()
    {
        Array.Clear(_spent, 0, _spent.Length);
        Set(0);
    }

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (IsEmpty) return new Dictionary<string, byte[]>();
        var row = new JsonObject { [TotalKey] = Total, [SpentKey] = new JsonArray(_spent.Select(v => (JsonNode)v).ToArray()) };
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(row.ToJsonString()) };
    }

    // Constraint: replaces the total; a save without the section clears it; throws FormatException on a malformed section and leaves the total untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var total = 0L;
        var spent = new int[_spent.Length];
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                var row = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("player experience is not an object");
                total = (row[TotalKey] ?? throw new FormatException("player experience has no total")).GetValue<long>();
                if (total < 0) throw new FormatException("player experience is negative");
                if (row[SpentKey] is JsonArray rows)
                    for (var i = 0; i < spent.Length && i < rows.Count; i++)
                        spent[i] = Math.Clamp(rows[i]?.GetValue<int>() ?? 0, 0, 255);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("player experience is malformed", ex); }
        }
        Array.Copy(spent, _spent, spent.Length);
        Set(total);
    }

    private void Set(long total)
    {
        Total = total;
        Level = _curve.LevelAt(total);
        Changed?.Invoke();
    }
}
