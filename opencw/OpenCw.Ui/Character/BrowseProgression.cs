using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed class BrowseProgression : IProgressionService, IDisposable
{
    private static readonly int[] DefaultAttributes = { 5, 5, 5, 5, 5, 5, 5 };

    private readonly ProgressionCatalog _catalog = new(ProgressionCatalog.DefaultSpecialAttributes(), Array.Empty<ProgressionPerkDef>());
    private readonly Func<IReadOnlyList<SpecialEntry>>? _special;
    private readonly Func<IReadOnlyList<OwnedPerk>>? _ownedPerks;
    private int[] _attributes = DefaultAttributes;
    private int _revision;

    public BrowseProgression(Func<IReadOnlyList<SpecialEntry>>? special = null, Func<IReadOnlyList<OwnedPerk>>? ownedPerks = null)
    {
        _special = special;
        _ownedPerks = ownedPerks;
        PlayerExperience.Current.Changed += OnExperienceChanged;
        PlayerPerks.Current.Changed += OnPerksChanged;
        Refresh();
    }

    public event Action? Changed;

    public ProgressionSnapshot Snapshot { get; private set; } = InvalidSnapshot();

    public ChooseResult TryChoose(string entryId, int rank)
    {
        if (string.IsNullOrEmpty(entryId)) return ChooseResult.Unknown;
        var column = AttributeColumn(entryId);
        if (column < 0) return ChooseResult.Unknown;
        var max = _catalog.AttributeMaxRank(column);
        var current = _attributes[column];
        if (current >= max) return ChooseResult.AlreadyMax;
        if (rank != current + 1) return ChooseResult.Unknown;
        if (PerkPoints() < 1) return ChooseResult.NoPoints;
        PlayerExperience.Current.SpendAttribute(column);
        return ChooseResult.Ok;
    }

    public void Dispose()
    {
        PlayerExperience.Current.Changed -= OnExperienceChanged;
        PlayerPerks.Current.Changed -= OnPerksChanged;
    }

    private void OnExperienceChanged() => Refresh();

    private void OnPerksChanged() => Refresh();

    private void Refresh()
    {
        var exp = PlayerExperience.Current;
        var level = Math.Max(1, exp.Level);
        var perkPoints = PerkPoints();
        var curve = exp.Curve;
        var xpForNext = level >= XpCurve.MaxLevel ? 0.0 : curve.TotalFor(level + 1) - curve.TotalFor(level);
        var xpInto = level >= XpCurve.MaxLevel ? 0.0 : exp.Progress * xpForNext;
        _attributes = EffectiveAttributes();
        _revision++;
        Snapshot = new ProgressionSnapshot(
            new PerkChartSnapshot(_revision, level, perkPoints, _attributes.ToArray(), BuildEntries()),
            xpInto,
            xpForNext);
        Changed?.Invoke();
    }

    private IReadOnlyList<PerkChartEntry> BuildEntries()
    {
        var entries = new List<PerkChartEntry>(ProgressionCatalog.SpecialAttributeCount);
        var canSpend = PerkPoints() > 0;
        for (var column = 0; column < ProgressionCatalog.SpecialAttributeCount; column++)
        {
            var def = _catalog.AttributeAt(column);
            var max = _catalog.AttributeMaxRank(column);
            var current = _attributes[column];
            var ranks = new PerkRankInfo[max];
            for (var i = 0; i < max; i++)
                ranks[i] = new PerkRankInfo(string.Create(CultureInfo.InvariantCulture, $"{def.Name} at {i + 1}."));
            entries.Add(new PerkChartEntry(AttributeId(column), def.Name, PerkChartKind.Attribute, column, current, max, ranks, canSpend && current < max));
        }
        var owned = _ownedPerks?.Invoke();
        if (owned is { Count: > 0 })
        {
            foreach (var perk in owned)
            {
                var max = Math.Max(Math.Max(perk.Rank, perk.RankDescriptions.Count), 1);
                var rows = new PerkRankInfo[max];
                for (var i = 0; i < max; i++)
                {
                    var text = i < perk.RankDescriptions.Count ? perk.RankDescriptions[i] : string.Create(CultureInfo.InvariantCulture, $"Rank {i + 1}.");
                    rows[i] = new PerkRankInfo(text);
                }
                entries.Add(new PerkChartEntry(perk.Id, perk.Name, PerkChartKind.Perk, null, perk.Rank, max, rows, false));
            }
            return entries;
        }
        foreach (var pair in PlayerPerks.Current.Ranks.OrderBy(row => row.Key, StringComparer.Ordinal))
        {
            var rows = new PerkRankInfo[Math.Max(pair.Value, 1)];
            for (var i = 0; i < rows.Length; i++)
                rows[i] = new PerkRankInfo(string.Create(CultureInfo.InvariantCulture, $"Rank {i + 1}."));
            entries.Add(new PerkChartEntry(pair.Key, pair.Key, PerkChartKind.Perk, null, pair.Value, rows.Length, rows, false));
        }
        return entries;
    }

    private static int PerkPoints()
    {
        var exp = PlayerExperience.Current;
        var earned = Math.Min(ProgressionCurve.MaxPerkPoints, Math.Max(1, exp.Level) - 1L);
        return (int)Math.Max(0, earned - exp.Spent.Sum());
    }

    private int[] EffectiveAttributes()
    {
        var read = ReadAttributes();
        var merged = new int[ProgressionCatalog.SpecialAttributeCount];
        for (var column = 0; column < ProgressionCatalog.SpecialAttributeCount; column++)
            merged[column] = Math.Clamp(read[column] + PlayerExperience.Current.Spent[column], 1, _catalog.AttributeMaxRank(column));
        return merged;
    }

    private int[] ReadAttributes()
    {
        var rows = _special?.Invoke();
        return rows is { Count: 7 } ? rows.Select(r => double.IsFinite(r.Value) ? (int)Math.Clamp(Math.Round(r.Value), 1, 10) : 5).ToArray() : DefaultAttributes;
    }

    private static int AttributeColumn(string entryId) =>
        entryId.Length > 4 && entryId.StartsWith("attr", StringComparison.Ordinal) && int.TryParse(entryId.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var column) && column is >= 0 and < ProgressionCatalog.SpecialAttributeCount ? column : -1;

    private static string AttributeId(int column) => string.Create(CultureInfo.InvariantCulture, $"attr{column}");

    private static ProgressionSnapshot InvalidSnapshot() =>
        new(new PerkChartSnapshot(0, 1, 0, DefaultAttributes, Array.Empty<PerkChartEntry>()), 0, 1);
}