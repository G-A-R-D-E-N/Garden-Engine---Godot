using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Ui.Character;

// Requires: main thread only; the total XP is the only saved value and the level is derived from it by the curve, so a curve change never leaves a stale level.
public sealed class PlayerExperience
{
    public const string SectionName = "player.xp.json";
    private const string TotalKey = "total";

    private XpCurve _curve = XpCurve.Default;

    public static PlayerExperience Current { get; } = new();

    public event Action? Changed;

    public long Total { get; private set; }

    public bool IsEmpty => Total == 0;

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
        return true;
    }

    public void Clear() => Set(0);

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (Total == 0) return new Dictionary<string, byte[]>();
        var row = new JsonObject { [TotalKey] = Total };
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(row.ToJsonString()) };
    }

    // Constraint: replaces the total; a save without the section clears it; throws FormatException on a malformed section and leaves the total untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var total = 0L;
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                var row = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("player experience is not an object");
                total = (row[TotalKey] ?? throw new FormatException("player experience has no total")).GetValue<long>();
                if (total < 0) throw new FormatException("player experience is negative");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("player experience is malformed", ex); }
        }
        Set(total);
    }

    private void Set(long total)
    {
        Total = total;
        Level = _curve.LevelAt(total);
        Changed?.Invoke();
    }
}
