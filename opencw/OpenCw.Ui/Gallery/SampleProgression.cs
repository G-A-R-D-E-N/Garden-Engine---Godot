using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;

namespace OpenCommonwealth.Services.Ui.Gallery;

public sealed class SampleProgression : IProgressionService
{
    private sealed record Rank(int Level, int Attribute, int Cost = 1);

    private sealed record PerkDef(string Id, string Name, int? Column, Rank[] Ranks, string? NeedsId = null, string? NeedsName = null);

    private static readonly string[] AttributeNames = { "Stat A", "Stat B", "Stat C", "Stat D", "Stat E", "Stat F", "Stat G" };

    private static readonly PerkDef[] Perks =
    {
        new("perk_a", "Perk A", 0, new[] { new Rank(1, 1), new Rank(5, 3), new Rank(9, 5) }),
        new("perk_b", "Perk B", 0, new[] { new Rank(3, 4, 2) }),
        new("perk_c", "Perk C", 1, new[] { new Rank(1, 2), new Rank(6, 4) }),
        new("perk_d", "Perk D", 2, new[] { new Rank(2, 3) }),
        new("perk_e", "Perk E", 3, new[] { new Rank(1, 1), new Rank(4, 3), new Rank(8, 5) }),
        new("perk_f", "Perk F", 4, new[] { new Rank(5, 5) }, "perk_c", "Perk C"),
        new("perk_g", "Perk G", 5, new[] { new Rank(1, 2) }),
        new("perk_h", "Perk H", 6, new[] { new Rank(2, 2), new Rank(7, 4) }),
        new("perk_x", "Perk X", null, new[] { new Rank(1, 0) }),
    };

    private readonly int[] _attributes = { 5, 5, 4, 5, 6, 4, 3 };
    private readonly Dictionary<string, int> _ranks = new();
    private int _level;
    private int _points;
    private int _revision = 1;

    public SampleProgression(int level = 6, int points = 2)
    {
        _level = Math.Max(1, level);
        _points = Math.Max(0, points);
    }

    public event Action? Changed;

    public ProgressionSnapshot Snapshot => new(new PerkChartSnapshot(_revision, _level, _points, _attributes.ToArray(), BuildEntries()), 120, 300);

    public void GainLevel()
    {
        _level++;
        _points++;
        _revision++;
        Changed?.Invoke();
    }

    public ChooseResult TryChoose(string entryId, int rank)
    {
        var attribute = Enumerable.Range(0, AttributeNames.Length).FirstOrDefault(i => AttributeId(i) == entryId, -1);
        if (attribute >= 0)
        {
            if (_attributes[attribute] >= 10) return ChooseResult.AlreadyMax;
            if (rank != _attributes[attribute] + 1) return ChooseResult.Unknown;
            if (_points < 1) return ChooseResult.NoPoints;
            _attributes[attribute] = rank;
            return Spend(1);
        }
        var perk = Array.Find(Perks, p => p.Id == entryId);
        if (perk is null) return ChooseResult.Unknown;
        var current = _ranks.GetValueOrDefault(perk.Id);
        if (current >= perk.Ranks.Length) return ChooseResult.AlreadyMax;
        if (rank != current + 1) return ChooseResult.Unknown;
        var info = perk.Ranks[current];
        if (_points < info.Cost) return ChooseResult.NoPoints;
        if (Unmet(perk, current).Count > 0) return ChooseResult.Locked;
        _ranks[perk.Id] = rank;
        return Spend(info.Cost);
    }

    private ChooseResult Spend(int cost)
    {
        _points -= cost;
        _revision++;
        Changed?.Invoke();
        return ChooseResult.Ok;
    }

    private static string AttributeId(int column) => "attr" + column;

    private List<PerkChartEntry> BuildEntries()
    {
        var entries = new List<PerkChartEntry>();
        for (var c = 0; c < AttributeNames.Length; c++)
        {
            var name = AttributeNames[c];
            var ranks = Enumerable.Range(1, 10).Select(i => new PerkRankInfo($"{name} at {i}.")).ToArray();
            entries.Add(new PerkChartEntry(AttributeId(c), name, PerkChartKind.Attribute, c, _attributes[c], 10, ranks, _attributes[c] < 10));
        }
        foreach (var perk in Perks)
        {
            var current = _ranks.GetValueOrDefault(perk.Id);
            var ranks = perk.Ranks.Select((r, i) => new PerkRankInfo($"{perk.Name} rank {i + 1}.", r.Level,
                perk.Column is { } col && r.Attribute > 0 ? (col, r.Attribute) : null, Unmet(perk, i), r.Cost)).ToArray();
            var available = current < perk.Ranks.Length && ranks[current].UnmetOrEmpty.Count == 0;
            entries.Add(new PerkChartEntry(perk.Id, perk.Name, PerkChartKind.Perk, perk.Column, current, perk.Ranks.Length, ranks, available));
        }
        return entries;
    }

    private List<UnmetRequirement> Unmet(PerkDef perk, int rankIndex)
    {
        var rank = perk.Ranks[rankIndex];
        var unmet = new List<UnmetRequirement>();
        if (rank.Level > _level) unmet.Add(new UnmetRequirement(UnmetKind.Level, rank.Level, _level, string.Empty));
        if (perk.Column is { } column && rank.Attribute > _attributes[column])
            unmet.Add(new UnmetRequirement(UnmetKind.Attribute, rank.Attribute, _attributes[column], AttributeNames[column]));
        if (perk.NeedsId is { } needs && _ranks.GetValueOrDefault(needs) < 1)
            unmet.Add(new UnmetRequirement(UnmetKind.Perk, 1, 0, perk.NeedsName ?? needs));
        return unmet;
    }
}
