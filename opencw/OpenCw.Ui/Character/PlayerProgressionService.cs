using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed class PlayerProgressionService : IProgressionService
{
    public const int AttributeCount = 7;
    public const int StartingAttributeValue = 5;
    private const string AttributePrefix = "attr";

    private readonly ProgressionCatalog _catalog;
    private readonly ProgressionXpFormula _formula;
    private readonly List<ProgressionPerkDef> _perkDefs = new();
    private readonly Dictionary<string, ProgressionPerkDef> _perks = new(StringComparer.Ordinal);
    private readonly int[] _attributes = new int[AttributeCount];
    private readonly Dictionary<string, int> _perkRanks = new(StringComparer.Ordinal);
    private int _level;
    private double _xpIntoLevel;
    private int _perkPoints;
    private int _revision = 1;
    private ProgressionSnapshot _snapshot;

    public PlayerProgressionService(ProgressionCatalog? catalog = null, ProgressionSaveState? initialState = null)
    {
        _catalog = catalog ?? new ProgressionCatalog(ProgressionCatalog.DefaultSpecialAttributes(), Array.Empty<ProgressionPerkDef>());
        _formula = _catalog.XpFormula ?? new ProgressionXpFormula();
        if (_catalog.Perks is { } perks)
        {
            foreach (var perk in perks)
            {
                if (perk is null || string.IsNullOrEmpty(perk.Id) || AttributeColumn(perk.Id) >= 0 || !_perks.TryAdd(perk.Id, perk)) continue;
                _perkDefs.Add(perk);
            }
        }
        for (var column = 0; column < AttributeCount; column++)
            _attributes[column] = Math.Clamp(StartingAttributeValue, 1, _catalog.AttributeMaxRank(column));
        _level = 1;
        if (initialState is not null) Apply(initialState);
        _snapshot = BuildSnapshot();
    }

    public event Action? Changed;

    public ProgressionSnapshot Snapshot => _snapshot;

    public int AwardXp(double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0) return 0;
        var cap = _catalog.LevelCap;
        var maxLevel = cap is { } limit ? Math.Max(1, limit) : int.MaxValue;
        if (_level >= maxLevel) return 0;
        var before = _xpIntoLevel;
        var maxGain = maxLevel - _level;
        var gained = MaxLevelsGained(amount, maxGain);
        if (gained == 0)
        {
            var needed = _formula.XpForNextLevel(_level);
            if (!(needed > 0)) return 0;
            _xpIntoLevel = Math.Min(needed, _xpIntoLevel + amount);
            if (_xpIntoLevel == before) return 0;
        }
        else
        {
            var spent = CumulativeCost(gained);
            _level += gained;
            _perkPoints = (int)Math.Min(ProgressionCurve.MaxPerkPoints, _perkPoints + (long)gained);
            if (_level >= maxLevel)
            {
                _xpIntoLevel = cap is not null ? 0 : Math.Min(_formula.XpForNextLevel(_level), Math.Max(0, amount - spent));
            }
            else
            {
                var nextNeeded = _formula.XpForNextLevel(_level);
                var rem = Math.Max(0, amount - spent);
                _xpIntoLevel = nextNeeded > 0 ? Math.Min(nextNeeded, rem) : 0;
            }
        }
        _revision++;
        Rebuild();
        Changed?.Invoke();
        return gained;
    }

    private int MaxLevelsGained(double amount, int maxGain)
    {
        var lo = 0;
        var hi = maxGain;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo + 1) >> 1);
            var cost = CumulativeCost(mid);
            if (cost <= amount) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    private double CumulativeCost(int levelsToGain)
    {
        if (levelsToGain <= 0) return 0;
        var needed = _formula.XpForNextLevel(_level);
        if (!(needed > 0)) return double.PositiveInfinity;
        var first = Math.Max(0, needed - _xpIntoLevel);
        if (levelsToGain == 1) return first;
        var m = (double)(levelsToGain - 1);
        var baseXp = double.IsFinite(_formula.BaseXp) && _formula.BaseXp > 0 ? _formula.BaseXp : 200;
        var bump = double.IsFinite(_formula.BumpXp) && _formula.BumpXp >= 0 ? _formula.BumpXp : 75;
        var nextBase = (double)_level * bump + baseXp;
        return first + m * nextBase + bump * m * (m - 1.0) * 0.5;
    }

    public ChooseResult TryChoose(string entryId, int rank)
    {
        if (string.IsNullOrEmpty(entryId)) return ChooseResult.Unknown;
        var column = AttributeColumn(entryId);
        return column >= 0 ? ChooseAttribute(column, rank) : ChoosePerk(entryId, rank);
    }

    public ProgressionSaveState Capture() =>
        new(_level, _xpIntoLevel, _perkPoints, _attributes.ToArray(), new Dictionary<string, int>(_perkRanks, StringComparer.Ordinal));

    public void Restore(ProgressionSaveState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Apply(state);
        _revision++;
        Rebuild();
        Changed?.Invoke();
    }

    private void Apply(ProgressionSaveState state)
    {
        var level = Math.Max(1, state.Level);
        if (_catalog.LevelCap is { } cap) level = Math.Min(level, Math.Max(1, cap));
        _level = level;

        var source = state.Attributes;
        for (var column = 0; column < AttributeCount; column++)
        {
            var value = source is not null && column < source.Count ? source[column] : 1;
            _attributes[column] = Math.Clamp(value, 1, _catalog.AttributeMaxRank(column));
        }

        _perkPoints = Math.Clamp(state.PerkPoints, 0, ProgressionCurve.MaxPerkPoints);

        _perkRanks.Clear();
        if (state.PerkRanks is { } ranks)
        {
            var tentative = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in ranks)
            {
                if (pair.Key is null || !_perks.TryGetValue(pair.Key, out var perk)) continue;
                var maxRank = perk.Ranks is { } defs ? defs.Count : 0;
                if (maxRank > 0 && pair.Value > 0) tentative[pair.Key] = pair.Value;
            }
            RestorePerks(tentative);
        }

        var needed = _formula.XpForNextLevel(_level);
        var xp = double.IsFinite(state.XpIntoLevel) && state.XpIntoLevel > 0 ? state.XpIntoLevel : 0;
        _xpIntoLevel = needed > 0 ? Math.Min(xp, needed) : 0;
    }

    private ChooseResult ChooseAttribute(int column, int rank)
    {
        var max = _catalog.AttributeMaxRank(column);
        var current = _attributes[column];
        if (current >= max) return ChooseResult.AlreadyMax;
        if (rank != current + 1) return ChooseResult.Unknown;
        if (_perkPoints < 1) return ChooseResult.NoPoints;
        _attributes[column] = Math.Clamp(rank, 1, max);
        Spend(1);
        return ChooseResult.Ok;
    }

    private ChooseResult ChoosePerk(string entryId, int rank)
    {
        if (!_perks.TryGetValue(entryId, out var perk)) return ChooseResult.Unknown;
        if (perk.Ranks is not { } ranks || ranks.Count == 0) return ChooseResult.AlreadyMax;
        var current = _perkRanks.GetValueOrDefault(entryId);
        if (current >= ranks.Count) return ChooseResult.AlreadyMax;
        if (rank != current + 1) return ChooseResult.Unknown;
        var info = ranks[current];
        var cost = Math.Max(1, info.Cost);
        if (_perkPoints < cost) return ChooseResult.NoPoints;
        if (Unmet(perk, current).Count > 0) return ChooseResult.Locked;
        _perkRanks[entryId] = rank;
        Spend(cost);
        return ChooseResult.Ok;
    }

    private void RestorePerks(Dictionary<string, int> tentative)
    {
        var attributeSpend = 0;
        foreach (var value in _attributes) attributeSpend += Math.Max(0, value - StartingAttributeValue);
        var budget = Math.Max(0, _level - 1 - _perkPoints - attributeSpend);
        var order = tentative.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        bool progressed;
        do
        {
            progressed = false;
            foreach (var id in order)
            {
                var perk = _perks[id];
                var ranks = perk.Ranks!;
                var cap = Math.Min(tentative[id], ranks.Count);
                var have = _perkRanks.GetValueOrDefault(id);
                while (have < cap)
                {
                    var cost = Math.Max(1, ranks[have].Cost);
                    if (cost > budget || Unmet(perk, have, _perkRanks).Count > 0) break;
                    budget -= cost;
                    _perkRanks[id] = ++have;
                    progressed = true;
                }
            }
        } while (progressed);
        var dropped = 0;
        foreach (var id in order)
        {
            var ranks = _perks[id].Ranks!;
            for (var i = _perkRanks.GetValueOrDefault(id); i < Math.Min(tentative[id], ranks.Count); i++)
                dropped += Math.Max(1, ranks[i].Cost);
        }
        _perkPoints = (int)Math.Min(ProgressionCurve.MaxPerkPoints, (long)_perkPoints + Math.Min(budget, dropped));
    }

    private List<UnmetRequirement> Unmet(ProgressionPerkDef perk, int rankIndex, IReadOnlyDictionary<string, int>? ranksForPrereqs = null)
    {
        var unmet = new List<UnmetRequirement>();
        if (perk.Ranks is not { } ranks || rankIndex < 0 || rankIndex >= ranks.Count) return unmet;
        var info = ranks[rankIndex];
        if (info.RequiredLevel > _level)
            unmet.Add(new UnmetRequirement(UnmetKind.Level, info.RequiredLevel, _level, string.Empty));
        if (perk.Column is >= 0 and < AttributeCount && info.RequiredAttribute > 0)
        {
            var column = perk.Column.Value;
            if (info.RequiredAttribute > _attributes[column])
                unmet.Add(new UnmetRequirement(UnmetKind.Attribute, info.RequiredAttribute, _attributes[column], _catalog.AttributeAt(column).Name));
        }
        if (perk.PrerequisitePerkId is { Length: > 0 } prerequisite)
        {
            var needed = Math.Max(1, perk.PrerequisiteRank);
            var lookup = ranksForPrereqs ?? _perkRanks;
            var have = lookup.GetValueOrDefault(prerequisite);
            if (have < needed)
                unmet.Add(new UnmetRequirement(UnmetKind.Perk, needed, have, perk.PrerequisitePerkName ?? prerequisite));
        }
        return unmet;
    }

    private void Spend(int cost)
    {
        _perkPoints = Math.Max(0, _perkPoints - cost);
        _revision++;
        Rebuild();
        Changed?.Invoke();
    }

    private void Rebuild() => _snapshot = BuildSnapshot();

    private ProgressionSnapshot BuildSnapshot()
    {
        var entries = new List<PerkChartEntry>(AttributeCount + _perkDefs.Count);
        for (var column = 0; column < AttributeCount; column++)
        {
            var def = _catalog.AttributeAt(column);
            var max = _catalog.AttributeMaxRank(column);
            var current = _attributes[column];
            var ranks = new PerkRankInfo[max];
            for (var i = 0; i < max; i++)
                ranks[i] = new PerkRankInfo(string.Create(CultureInfo.InvariantCulture, $"{def.Name} at {i + 1}."));
            entries.Add(new PerkChartEntry(AttributeId(column), def.Name, PerkChartKind.Attribute, column, current, max, ranks, current < max));
        }
        foreach (var perk in _perkDefs)
        {
            if (perk.Ranks is not { } defs || defs.Count == 0) continue;
            var current = _perkRanks.GetValueOrDefault(perk.Id);
            var rows = new PerkRankInfo[defs.Count];
            for (var i = 0; i < defs.Count; i++)
            {
                var info = defs[i];
                var attribute = perk.Column is >= 0 and < AttributeCount && info.RequiredAttribute > 0
                    ? (perk.Column.Value, info.RequiredAttribute)
                    : ((int Column, int Amount)?)null;
                var unmet = i >= current ? Unmet(perk, i) : null;
                rows[i] = new PerkRankInfo(info.Description, info.RequiredLevel, attribute, unmet, Math.Max(1, info.Cost));
            }
            var available = current < defs.Count && rows[current].UnmetOrEmpty.Count == 0;
            entries.Add(new PerkChartEntry(perk.Id, perk.Name, PerkChartKind.Perk, perk.Column, current, defs.Count, rows, available));
        }
        var chart = new PerkChartSnapshot(_revision, _level, _perkPoints, _attributes.ToArray(), entries);
        return new ProgressionSnapshot(chart, _xpIntoLevel, _formula.XpForNextLevel(_level), _catalog.LevelCap);
    }

    private static string AttributeId(int column) => string.Create(CultureInfo.InvariantCulture, $"{AttributePrefix}{column}");

    private static int AttributeColumn(string entryId)
    {
        if (!entryId.StartsWith(AttributePrefix, StringComparison.Ordinal)) return -1;
        return int.TryParse(entryId.AsSpan(AttributePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var column)
            && column >= 0 && column < AttributeCount ? column : -1;
    }
}
