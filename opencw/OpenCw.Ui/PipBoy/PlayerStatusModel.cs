using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed class PlayerStatusModel : IPipBoyTabSource
{
    private const string SpecialPrefix = "special:";
    private const string PerkPrefix = "perk:";
    private const string LimbPrefix = "limb:";
    public const string TorsoKey = "limb:torso";
    private const string EffectsKey = "effects";
    private static readonly LimbKind[] LimbOrder = Enum.GetValues<LimbKind>();

    private readonly IPlayerStatusSource _source;
    private readonly StatusSubTab[] _tabs;
    private readonly Func<IReadOnlyList<StatusRow>>? _facts;
    private readonly List<StatusRow> _rows = new();
    private readonly Dictionary<string, OwnedPerk> _perks = new();
    private string? _viewedKey;
    private int _viewed;

    public PlayerStatusModel(IPlayerStatusSource source, Func<IReadOnlyList<StatusRow>>? facts = null, bool statusOnly = false, bool withPerks = false, bool withSpecial = false)
    {
        _source = source;
        _facts = facts;
        _tabs = !statusOnly ? Enum.GetValues<StatusSubTab>()
            : Enum.GetValues<StatusSubTab>().Where(tab => tab == StatusSubTab.Status || tab == StatusSubTab.Special && withSpecial || tab == StatusSubTab.Perks && withPerks).ToArray();
        SubTabLabels = _tabs.Select(tab => tab.ToString().ToUpperInvariant()).ToArray();
        View = new MenuList<StatusRow>(_rows, row => row.Key, row => row.Label + " " + row.Value);
        Rebuild();
        View.SelectVisible(0);
    }

    public event Action? Changed;

    public event Action<AidKind>? AidRequested;

    public MenuList<StatusRow> View { get; }

    public PipBoyTab Tab => PipBoyTab.Stats;

    public StatusSubTab SubTab { get; private set; }

    public int SubTabIndex => Array.IndexOf(_tabs, SubTab);

    public IReadOnlyList<string> SubTabLabels { get; }

    public string? CurrentKey => View.Current?.Key;

    public string Heading => _source.PlayerName ?? string.Empty;

    public bool EffectsShown { get; private set; }

    public bool HasEffects => _source.Effects.Any(effect => effect.Lines.Count > 0);

    public int EffectScroll { get; private set; }

    public int EffectViewRows { get; set; } = 12;

    public string EmptyMessage => SubTab == StatusSubTab.Perks && _rows.Count == 0 ? "NO PERKS" : string.Empty;

    public static string LimbKey(LimbKind limb) => LimbPrefix + limb.ToString().ToLowerInvariant();

    public void SetSubTab(int index) => SetSubTab(_tabs[Math.Clamp(index, 0, _tabs.Length - 1)]);

    public void SetSubTab(StatusSubTab tab)
    {
        if (tab == SubTab || Array.IndexOf(_tabs, tab) < 0) return;
        SubTab = tab;
        EffectsShown = false;
        EffectScroll = 0;
        _viewedKey = null;
        Rebuild();
        View.SelectVisible(0);
        Changed?.Invoke();
    }

    public void Refresh()
    {
        _viewedKey = null;
        if (EffectsShown && !HasEffects) EffectsShown = false;
        Rebuild();
        EffectScroll = Math.Min(EffectScroll, MaxScroll());
        Changed?.Invoke();
    }

    public bool Back()
    {
        if (!EffectsShown) return false;
        EffectsShown = false;
        Rebuild();
        Changed?.Invoke();
        return true;
    }

    public IReadOnlyList<ConditionRow> Condition
    {
        get
        {
            var rows = new List<ConditionRow>();
            foreach (var limb in LimbOrder)
            {
                if (_source.Limb(limb) is not { } condition) continue;
                var fraction = Math.Clamp(Finite(condition.Fraction), 0, 1);
                var crippled = condition.Crippled && fraction <= 0;
                rows.Add(new ConditionRow(LimbKey(limb), Spaced(limb.ToString()), fraction, fraction,
                                          crippled ? "CRIPPLED" : Percent(fraction), crippled));
            }
            if (_source.Health is { } health && _source.MaxHealth is { } max && double.IsFinite(health) && double.IsFinite(max))
                rows.Add(TorsoRow(Math.Max(health, 0), max));
            return rows;
        }
    }

    public IReadOnlyList<StatusRow> Facts => _facts?.Invoke() ?? [];

    public double? RadiationFraction => _source.Radiation is { } radiation && double.IsFinite(radiation) && radiation > 0
        ? Math.Clamp(radiation, 0, 1)
        : null;

    public string? DamageLine => TotalsLine(_source.DamageTotals);

    public string? ResistLine => TotalsLine(_source.ResistTotals);

    public IReadOnlyList<AidAction> Aid
    {
        get
        {
            var actions = new List<AidAction>();
            if (_source.Stimpak is { } stimpak) actions.Add(AidFor(AidKind.Stimpak, stimpak));
            if (_source.RadAway is { } radaway) actions.Add(AidFor(AidKind.RadAway, radaway));
            return actions;
        }
    }

    public bool RequestAid(AidKind kind)
    {
        if (!Aid.Any(action => action.Kind == kind && action.Enabled)) return false;
        AidRequested?.Invoke(kind);
        return true;
    }

    public bool ToggleEffects()
    {
        if (SubTab != StatusSubTab.Status || !HasEffects) return false;
        EffectsShown = !EffectsShown;
        EffectScroll = 0;
        Rebuild();
        Changed?.Invoke();
        return true;
    }

    public IReadOnlyList<EffectGroupView> EffectView =>
        _source.Effects
               .GroupBy(effect => effect.Group)
               .OrderBy(group => group.Key)
               .Select(group => new EffectGroupView(group.Key, group.Select(EntryOf).Where(entry => entry.Lines.Count > 0).ToArray()))
               .Where(group => group.Entries.Count > 0)
               .ToArray();

    public IReadOnlyList<string> EffectLines
    {
        get
        {
            var lines = new List<string>();
            foreach (var group in EffectView)
            {
                foreach (var entry in group.Entries)
                {
                    lines.Add(entry.Source + ":");
                    lines.AddRange(entry.Lines);
                }
            }
            return lines;
        }
    }

    public bool ScrollEffects(int delta)
    {
        if (!EffectsShown) return false;
        var next = Math.Clamp(EffectScroll + delta, 0, MaxScroll());
        if (next == EffectScroll) return false;
        EffectScroll = next;
        Changed?.Invoke();
        return true;
    }

    public SpecialEntry? SelectedSpecial =>
        SubTab == StatusSubTab.Special && View.Current is { } row
            ? _source.Special.FirstOrDefault(entry => SpecialPrefix + entry.Id == row.Key)
            : null;

    public OwnedPerk? SelectedPerk
    {
        get
        {
            if (SubTab != StatusSubTab.Perks || View.Current is not { } row || !_perks.TryGetValue(row.Key, out var perk)) return null;
            if (_viewedKey != perk.Id)
            {
                _viewedKey = perk.Id;
                _viewed = Math.Clamp(perk.Rank, 1, MaxRank(perk));
            }
            return perk;
        }
    }

    public int ViewedRank => SelectedPerk is null ? 0 : _viewed;

    public bool CanPrevRank => SelectedPerk is { } perk && MaxRank(perk) > 1 && ViewedRank > 1;

    public bool CanNextRank => SelectedPerk is { } perk && MaxRank(perk) > 1 && ViewedRank < MaxRank(perk);

    public bool PrevRank() => StepRank(-1);

    public bool NextRank() => StepRank(1);

    public string RankLine
    {
        get
        {
            if (SelectedPerk is not { } perk || MaxRank(perk) <= 1) return string.Empty;
            var text = string.Create(CultureInfo.InvariantCulture, $"RANK {ViewedRank} / {MaxRank(perk)}");
            return ViewedRank > perk.Rank ? text + " (not owned)" : text;
        }
    }

    public string SelectedDescription
    {
        get
        {
            if (SelectedSpecial is { } special) return special.Description;
            if (SelectedPerk is not { } perk) return string.Empty;
            var index = ViewedRank - 1;
            return index >= 0 && index < perk.RankDescriptions.Count ? perk.RankDescriptions[index] : string.Empty;
        }
    }

    public bool Select(string key)
    {
        var tab = key.StartsWith(SpecialPrefix, StringComparison.Ordinal) ? StatusSubTab.Special
            : key.StartsWith(PerkPrefix, StringComparison.Ordinal) ? StatusSubTab.Perks
            : StatusSubTab.Status;
        var previous = SubTab;
        var before = CurrentKey;
        SetSubTab(tab);
        var index = View.Visible.ToList().FindIndex(row => row.Key == key);
        if (index >= 0) return View.SelectVisible(index);
        SetSubTab(previous);
        if (before is not null) View.SelectVisible(View.Visible.ToList().FindIndex(row => row.Key == before));
        return false;
    }

    public IReadOnlyList<PipBoySearchHit> Search(IReadOnlyList<string> tokens)
    {
        var hits = new List<PipBoySearchHit>();
        foreach (var entry in _source.Special.Where(entry => ListSearch.Matches(entry.Name + " " + entry.Description, tokens)))
            hits.Add(new PipBoySearchHit(PipBoyTab.Stats, SpecialPrefix + entry.Id, entry.Name, SpecialText(entry)));
        foreach (var perk in _source.Perks.Where(perk => ListSearch.Matches(perk.Name + " " + string.Join(' ', perk.RankDescriptions), tokens)))
            hits.Add(new PipBoySearchHit(PipBoyTab.Stats, PerkPrefix + perk.Id, perk.Name,
                                         string.Create(CultureInfo.InvariantCulture, $"Rank {perk.Rank}")));
        return hits;
    }

    private static int MaxRank(OwnedPerk perk) => Math.Max(Math.Max(perk.RankDescriptions.Count, perk.Rank), 1);

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;

    private static string Percent(double fraction) =>
        Math.Round(fraction * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string Whole(double value) => Math.Floor(value).ToString("0", CultureInfo.InvariantCulture);

    private static string Spaced(string name) =>
        name switch { "LeftArm" => "Left arm", "RightArm" => "Right arm", "LeftLeg" => "Left leg", "RightLeg" => "Right leg", _ => name };

    private static string Signed(double value) => (value > 0 ? "+" : string.Empty) + NumberText.Format(value);

    private static string SpecialText(SpecialEntry entry)
    {
        var marker = entry.Modifier > 0 ? "+" : entry.Modifier < 0 ? "-" : string.Empty;
        var whole = Math.Abs(entry.Value - Math.Round(entry.Value)) < 1e-9;
        return marker + entry.Value.ToString(whole ? "0" : "0.0", CultureInfo.InvariantCulture);
    }

    private static string? TotalsLine(IReadOnlyList<DamageTotal>? totals)
    {
        if (totals is null) return null;
        var shown = totals.Where(total => double.IsFinite(total.Value) && total.Value != 0)
                          .Select(total => total.Kind.ToUpperInvariant() + " " + NumberText.Format(total.Value))
                          .ToArray();
        return shown.Length == 0 ? "PHYSICAL 0" : string.Join("  ", shown);
    }

    private AidAction AidFor(AidKind kind, AidSupply supply) =>
        new(kind, string.Create(CultureInfo.InvariantCulture, $"USE {supply.Name.ToUpperInvariant()} ({supply.Count})"),
            supply.Count > 0 && !_source.ReadOnly);

    private static EffectEntryView EntryOf(EffectSource source)
    {
        var merged = new List<EffectLine>();
        foreach (var line in source.Lines)
        {
            var index = merged.FindIndex(existing => existing.Name == line.Name);
            if (index < 0)
            {
                merged.Add(line);
                continue;
            }
            var first = merged[index];
            merged[index] = first with
            {
                Value = first.Value + line.Value,
                Duration = Math.Max(first.Duration, line.Duration),
                CustomDescription = first.CustomDescription || line.CustomDescription,
            };
        }
        var lines = merged.Select(line => line.CustomDescription
            ? line.Name.ToUpperInvariant()
            : line.Name.ToUpperInvariant() + " " + Signed(line.Value) + (line.Percent ? "%" : string.Empty)).ToArray();
        return new EffectEntryView(source.Name.ToUpperInvariant(), lines);
    }

    private ConditionRow TorsoRow(double health, double max)
    {
        var fraction = max > 0 ? Math.Clamp(health / max, 0, 1) : 0;
        var gain = _source.PendingHealthGain is { } pending && double.IsFinite(pending) && pending > 0 ? pending : 0;
        var pendingFraction = gain > 0 && max > 0 ? Math.Clamp(Math.Min(health + gain, max) / max, 0, 1) : fraction;
        return new ConditionRow(TorsoKey, "Torso", fraction, pendingFraction, Whole(health) + "/" + Whole(max), false);
    }

    private int MaxScroll() => Math.Max(0, EffectLines.Count - Math.Max(EffectViewRows, 1));

    private bool StepRank(int delta)
    {
        if (SelectedPerk is not { } perk) return false;
        var next = ViewedRank + delta;
        if (next < 1 || next > MaxRank(perk)) return false;
        _viewed = next;
        Changed?.Invoke();
        return true;
    }

    private void Rebuild()
    {
        _rows.Clear();
        _perks.Clear();
        switch (SubTab)
        {
            case StatusSubTab.Status:
                BuildStatusRows();
                break;
            case StatusSubTab.Special:
                foreach (var entry in _source.Special)
                    _rows.Add(new StatusRow(SpecialPrefix + entry.Id, entry.Name, SpecialText(entry)));
                break;
            default:
                foreach (var perk in _source.Perks.OrderBy(perk => perk.Name, StringComparer.Ordinal))
                {
                    var key = PerkPrefix + perk.Id;
                    _perks[key] = perk;
                    _rows.Add(new StatusRow(key, perk.Rank > 1 ? string.Create(CultureInfo.InvariantCulture, $"{perk.Name} ({perk.Rank})") : perk.Name, string.Empty));
                }
                break;
        }
        View.Refresh();
    }

    private void BuildStatusRows()
    {
        foreach (var row in Condition) _rows.Add(new StatusRow(row.Key, row.Label, row.Text));
        if (RadiationFraction is { } radiation) _rows.Add(new StatusRow("radiation", "Radiation", Percent(radiation)));
        if (DamageLine is { } damage) _rows.Add(new StatusRow("damage", "Damage", damage));
        if (ResistLine is { } resist) _rows.Add(new StatusRow("resist", "Resistance", resist));
        _rows.AddRange(Facts);
        foreach (var action in Aid) _rows.Add(new StatusRow("aid:" + action.Kind.ToString().ToLowerInvariant(), action.Label, string.Empty));
        if (HasEffects) _rows.Add(new StatusRow(EffectsKey, EffectsShown ? "Hide effects" : "Show effects", string.Empty));
    }
}
