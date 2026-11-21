using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.PipBoy;

// Requires: live ids are AVIF or PERK keys or editor ids; SPECIAL descriptions stay the live source's.
public sealed class RecordPlayerStatusSource : IPlayerStatusSource
{
    public const int MaxPerkRanks = 16;
    public const string DescriptionPath = "PERK\\Description";
    public const string NextPerkPath = "PERK\\NextPerk";

    private readonly IPlayerStatusSource _live;
    private readonly IRecordReader _reader;
    private readonly Described<SpecialEntry> _special;
    private readonly Described<OwnedPerk> _perks;

    public RecordPlayerStatusSource(IPlayerStatusSource live, IRecordReader reader)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(reader);
        _live = live;
        _reader = reader;
        _special = new Described<SpecialEntry>(() => _live.Special, DescribeSpecial);
        _perks = new Described<OwnedPerk>(() => _live.Perks, DescribePerk);
    }

    public string? PlayerName => _live.PlayerName;

    public double? Health => _live.Health;

    public double? MaxHealth => _live.MaxHealth;

    public double? PendingHealthGain => _live.PendingHealthGain;

    public double? Radiation => _live.Radiation;

    public IReadOnlyList<DamageTotal>? DamageTotals => _live.DamageTotals;

    public IReadOnlyList<DamageTotal>? ResistTotals => _live.ResistTotals;

    public AidSupply? Stimpak => _live.Stimpak;

    public AidSupply? RadAway => _live.RadAway;

    public IReadOnlyList<EffectSource> Effects => _live.Effects;

    public bool ReadOnly => _live.ReadOnly;

    public IReadOnlyList<SpecialEntry> Special => _special.Get();

    public IReadOnlyList<OwnedPerk> Perks => _perks.Get();

    public LimbCondition? Limb(LimbKind limb) => _live.Limb(limb);

    private SpecialEntry DescribeSpecial(SpecialEntry entry)
    {
        if (Resolve(entry.Id, "AVIF") is not { } id)
            return entry;
        return entry with { Name = _reader.Name(id) ?? entry.Name };
    }

    private OwnedPerk DescribePerk(OwnedPerk perk)
    {
        if (Resolve(perk.Id, "PERK") is not { } id)
            return perk;
        var ranks = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (string? step = id; step is not null && ranks.Count < MaxPerkRanks && seen.Add(step);)
        {
            ranks.Add(_reader.Text(step, DescriptionPath) ?? string.Empty);
            step = RecordKey.TryParse(_reader.Scalar(step, NextPerkPath), out var plugin, out var local) && local != 0 ? RecordKey.Format(plugin, local) : null;
        }
        return perk with
        {
            Name = _reader.Name(id) ?? perk.Name,
            RankDescriptions = ranks.Any(text => text.Length > 0) ? ranks : perk.RankDescriptions,
        };
    }

    private string? Resolve(string id, string signature)
    {
        if (RecordKey.TryParse(id, out _, out _))
            return _reader.Signature(id) == signature ? id : null;
        return _reader.FindEditorId(id, signature);
    }

    private sealed class Described<T>(Func<IReadOnlyList<T>> live, Func<T, T> describe)
    {
        private T[]? _source;
        private IReadOnlyList<T>? _result;

        public IReadOnlyList<T> Get()
        {
            var current = live();
            if (_result is null || !current.SequenceEqual(_source!))
            {
                _source = current.ToArray();
                _result = _source.Select(describe).ToArray();
            }
            return _result;
        }
    }
}
