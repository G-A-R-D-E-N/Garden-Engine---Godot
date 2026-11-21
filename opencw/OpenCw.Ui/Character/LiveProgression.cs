using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Character;

// Requires: main thread only; the reader is read on every Snapshot, so it must be the live one or null before a load order exists.
public sealed class LiveProgression : IProgressionService
{
    public const int AttributeRanks = 10;
    public const string LevelPath = "PERK\\Level";
    public const string PlayablePath = "PERK\\Playable";
    public const string HiddenPath = "PERK\\Hidden";
    public const string TraitPath = "PERK\\Trait";

    private sealed record Chain(string Root, string Name, IReadOnlyList<string> Steps);

    private readonly PlayerExperience _xp;
    private readonly PlayerPerks _perks;
    private readonly Func<IRecordReader?> _reader;
    private readonly Func<IReadOnlyList<SpecialEntry>> _special;
    private readonly Func<IDisposable?> _hold;
    private IReadOnlyList<Chain>? _chains;
    private IRecordReader? _chainsFor;
    private int _revision = 1;

    public LiveProgression(PlayerExperience xp, PlayerPerks perks, Func<IRecordReader?> reader, Func<IReadOnlyList<SpecialEntry>>? special = null, Func<IDisposable?>? hold = null)
    {
        _xp = xp;
        _perks = perks;
        _reader = reader;
        _special = special ?? (() => []);
        _hold = hold ?? (() => null);
        _xp.LevelGained += OnLevelGained;
    }

    public event Action? Changed;

    public ProgressionSnapshot Snapshot
    {
        get
        {
            var rows = _special();
            using var held = _hold();
            var entries = new List<PerkChartEntry>();
            var attributes = new int[PlayerSpecialRows.Ids.Count];
            for (var i = 0; i < attributes.Length; i++)
            {
                var value = rows.FirstOrDefault(row => row.Id == PlayerSpecialRows.Ids[i])?.Value;
                var known = value is { } v && double.IsFinite(v);
                attributes[i] = known ? (int)Math.Clamp(value!.Value, 1, AttributeRanks) : 1;
                var ranks = Enumerable.Range(1, AttributeRanks).Select(rank => new PerkRankInfo(string.Create(CultureInfo.InvariantCulture, $"{PlayerSpecialRows.Ids[i]} at {rank}."))).ToArray();
                entries.Add(new PerkChartEntry(AttributeId(i), PlayerSpecialRows.Ids[i], PerkChartKind.Attribute, i, attributes[i], AttributeRanks, ranks, known && attributes[i] < AttributeRanks));
            }
            if (_reader() is { } reader)
                foreach (var chain in ChainsOf(reader)) entries.Add(EntryOf(reader, chain));
            var chart = new PerkChartSnapshot(_revision, _xp.Level, _xp.PerkPoints, attributes, entries);
            var from = _xp.Curve.TotalFor(_xp.Level);
            return new ProgressionSnapshot(chart, Math.Max(0, _xp.Total - from), _xp.Curve.TotalFor(_xp.Level + 1) - from, XpCurve.MaxLevel);
        }
    }

    public ChooseResult TryChoose(string entryId, int rank)
    {
        if (entryId.StartsWith(AttributePrefix, StringComparison.Ordinal)) return TryRaise(entryId, rank);
        using var held = _hold();
        if (_reader() is not { } reader) return ChooseResult.Unknown;
        var chain = ChainsOf(reader).FirstOrDefault(c => string.Equals(c.Root, entryId, StringComparison.OrdinalIgnoreCase));
        if (chain is null) return ChooseResult.Unknown;
        var current = RankOf(chain);
        if (current >= chain.Steps.Count) return ChooseResult.AlreadyMax;
        if (rank != current + 1) return ChooseResult.Unknown;
        if (_xp.PerkPoints < 1) return ChooseResult.NoPoints;
        if (LevelOf(reader, chain.Steps[current]) > _xp.Level) return ChooseResult.Locked;
        if (!_xp.Spend(1)) return ChooseResult.NoPoints;
        if (current > 0) _perks.Remove(chain.Steps[current - 1]);
        _perks.Add(chain.Steps[current]);
        _revision++;
        Changed?.Invoke();
        return ChooseResult.Ok;
    }

    private const string AttributePrefix = "attr";

    // Requires: the special source is read before any lock is taken, as in Snapshot; an unreadable value answers Locked so a raise is never stored against an unknown base.
    private ChooseResult TryRaise(string entryId, int rank)
    {
        if (!int.TryParse(entryId.AsSpan(AttributePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var column) || column >= PlayerSpecialRows.Ids.Count) return ChooseResult.Unknown;
        var value = _special().FirstOrDefault(row => row.Id == PlayerSpecialRows.Ids[column])?.Value;
        if (value is not { } v || !double.IsFinite(v)) return ChooseResult.Locked;
        var current = (int)Math.Clamp(v, 1, AttributeRanks);
        if (current >= AttributeRanks) return ChooseResult.AlreadyMax;
        if (rank != current + 1) return ChooseResult.Unknown;
        if (_xp.PerkPoints < 1 || !_xp.RaiseSpecial(column)) return ChooseResult.NoPoints;
        _revision++;
        Changed?.Invoke();
        return ChooseResult.Ok;
    }

    private static string AttributeId(int column) => AttributePrefix + column.ToString(CultureInfo.InvariantCulture);

    private void OnLevelGained(int level)
    {
        _revision++;
        Changed?.Invoke();
    }

    private int RankOf(Chain chain)
    {
        for (var i = chain.Steps.Count - 1; i >= 0; i--)
            if (_perks.Has(chain.Steps[i])) return i + 1;
        return 0;
    }

    private static int LevelOf(IRecordReader reader, string id) =>
        int.TryParse(reader.Scalar(id, LevelPath), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) ? Math.Max(level, 0) : 0;

    private PerkChartEntry EntryOf(IRecordReader reader, Chain chain)
    {
        var current = RankOf(chain);
        var ranks = chain.Steps.Select(step =>
        {
            var level = LevelOf(reader, step);
            IReadOnlyList<UnmetRequirement>? unmet = level > _xp.Level ? new[] { new UnmetRequirement(UnmetKind.Level, level, _xp.Level, string.Empty) } : null;
            return new PerkRankInfo(reader.Text(step, RecordPlayerStatusSource.DescriptionPath) ?? string.Empty, level, null, unmet);
        }).ToArray();
        var available = current < ranks.Length && ranks[current].UnmetOrEmpty.Count == 0;
        return new PerkChartEntry(chain.Root, chain.Name, PerkChartKind.Perk, null, current, ranks.Length, ranks, available);
    }

    private IReadOnlyList<Chain> ChainsOf(IRecordReader reader)
    {
        if (_chains is not null && ReferenceEquals(_chainsFor, reader)) return _chains;
        var cards = reader.Winners("PERK").Where(id => Flag(reader, id, PlayablePath) && !Flag(reader, id, HiddenPath) && !Flag(reader, id, TraitPath)).ToArray();
        var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in cards)
            if (RecordKey.TryParse(reader.Scalar(id, RecordPlayerStatusSource.NextPerkPath), out var plugin, out var local) && local != 0)
                next[id] = RecordKey.Format(plugin, local);
        var followers = next.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chains = new List<Chain>();
        foreach (var root in cards.Where(id => !followers.Contains(id)))
        {
            var steps = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var step = root; step is not null && steps.Count < RecordPlayerStatusSource.MaxPerkRanks && seen.Add(step);)
            {
                steps.Add(step);
                step = next.GetValueOrDefault(step);
            }
            chains.Add(new Chain(root, reader.Name(root) ?? reader.EditorId(root) ?? root, steps));
        }
        _chainsFor = reader;
        return _chains = chains.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool Flag(IRecordReader reader, string id, string path) =>
        reader.Scalar(id, path) is { } text && (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase));
}
