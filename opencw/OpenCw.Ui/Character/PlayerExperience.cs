using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Ui.Character;

// Requires: main thread only; the total XP is the only saved value and the level is derived from it by the curve, so a curve change never leaves a stale level.
public sealed class PlayerExperience
{
    public const string SectionName = "player.xp.json";
    private const string TotalKey = "total";
    private const string PointsKey = "points";
    private const string SpecialKey = "special";
    public const int SpecialMax = 10;

    private readonly int[] _raises = new int[PlayerSpecialRows.Ids.Count];

    private XpCurve _curve = XpCurve.Default;

    public static PlayerExperience Current { get; } = new();

    public event Action? Changed;

    public event Action<int>? LevelGained;

    public int PerkPoints { get; private set; }

    public long Total { get; private set; }

    public bool IsEmpty => Total == 0 && PerkPoints == 0 && _raises.All(raise => raise == 0);

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
        var before = Level;
        Level = _curve.LevelAt(Total);
        var gained = Level - before;
        if (gained > 0) PerkPoints = Math.Min(PerkPoints + gained, ProgressionCurve.MaxPerkPoints);
        Changed?.Invoke();
        if (gained > 0) LevelGained?.Invoke(Level);
        return true;
    }

    // Invariant: only a whole cost no larger than the unspent points is taken.
    public bool Spend(int cost)
    {
        if (cost < 1 || cost > PerkPoints) return false;
        PerkPoints -= cost;
        Changed?.Invoke();
        return true;
    }

    public void Clear() => Set(0);

    // Invariant: the points added to one SPECIAL value on top of its record base; 0 for any name that is not SPECIAL.
    public int RaisedBy(string actorValue)
    {
        for (var i = 0; i < PlayerSpecialRows.Ids.Count; i++)
            if (string.Equals(PlayerSpecialRows.Ids[i], actorValue, StringComparison.OrdinalIgnoreCase)) return _raises[i];
        return 0;
    }

    // Invariant: spends one perk point and adds one raise in a single change; the caller owns the base-plus-raise ceiling because only it knows the record base.
    public bool RaiseSpecial(int index)
    {
        if (index < 0 || index >= _raises.Length || PerkPoints < 1 || _raises[index] >= SpecialMax) return false;
        _raises[index]++;
        PerkPoints--;
        Changed?.Invoke();
        return true;
    }

    // Invariant: replaces every raise without spending a perk point; a name that is not SPECIAL or a value outside 0..SpecialMax refuses the whole set.
    public bool SetSpecialRaises(IReadOnlyDictionary<string, int> raises)
    {
        var next = new int[_raises.Length];
        foreach (var (id, value) in raises)
        {
            var index = -1;
            for (var i = 0; i < PlayerSpecialRows.Ids.Count; i++)
                if (string.Equals(PlayerSpecialRows.Ids[i], id, StringComparison.OrdinalIgnoreCase)) index = i;
            if (index < 0 || value is < 0 or > SpecialMax) return false;
            next[index] = value;
        }
        next.CopyTo(_raises, 0);
        Changed?.Invoke();
        return true;
    }

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (IsEmpty) return new Dictionary<string, byte[]>();
        var row = new JsonObject { [TotalKey] = Total, [PointsKey] = PerkPoints };
        if (_raises.Any(raise => raise != 0)) row[SpecialKey] = new JsonArray(_raises.Select(raise => (JsonNode?)JsonValue.Create(raise)).ToArray());
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(row.ToJsonString()) };
    }

    // Constraint: replaces the total; a save without the section clears it; throws FormatException on a malformed section and leaves the total untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var total = 0L;
        var points = 0;
        var raises = new int[_raises.Length];
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                var row = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("player experience is not an object");
                total = (row[TotalKey] ?? throw new FormatException("player experience has no total")).GetValue<long>();
                if (total < 0) throw new FormatException("player experience is negative");
                points = row[PointsKey]?.GetValue<int>() ?? 0;
                if (points is < 0 or > ProgressionCurve.MaxPerkPoints) throw new FormatException("player perk points are out of range");
                if (row[SpecialKey] is { } special)
                {
                    var list = special as JsonArray ?? throw new FormatException("player SPECIAL raises are not a list");
                    if (list.Count != raises.Length) throw new FormatException("player SPECIAL raises have the wrong length");
                    for (var i = 0; i < raises.Length; i++)
                    {
                        raises[i] = (list[i] ?? throw new FormatException("player SPECIAL raise is missing")).GetValue<int>();
                        if (raises[i] is < 0 or > SpecialMax) throw new FormatException("player SPECIAL raise is out of range");
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("player experience is malformed", ex); }
        }
        Set(total, points, raises);
    }

    private void Set(long total, int points = 0, int[]? raises = null)
    {
        Total = total;
        PerkPoints = points;
        Array.Clear(_raises);
        raises?.CopyTo(_raises, 0);
        Level = _curve.LevelAt(total);
        Changed?.Invoke();
    }
}
