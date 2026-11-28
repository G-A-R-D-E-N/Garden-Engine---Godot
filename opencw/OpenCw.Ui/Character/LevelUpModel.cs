using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed record PerkRank(string Text, IReadOnlyList<string>? Requires = null);

public sealed record PerkDef(string Id, string Name, string Category, IReadOnlyList<PerkRank> Ranks);

public sealed record PerkPick(string PerkId, int Rank);

public sealed record RankComparison(string PerkName, int CurrentRank, int NextRank, string? CurrentText, string? NextText);

public enum PerkStatus { Available, Locked, Maxed, Picked }

public enum PickResult { Picked, UnknownPerk, NoPicksLeft, AlreadyPicked, Maxed, RequirementsNotMet, Finished }

public enum LevelUpConfirm { Confirmed, PicksRemaining, AlreadyFinished }

public interface IPerkRequirementChecker
{
    bool IsMet(string requirement, IReadOnlyDictionary<string, int> perkRanks);
}

public sealed class NoPerkRequirements : IPerkRequirementChecker
{
    public bool IsMet(string requirement, IReadOnlyDictionary<string, int> perkRanks) => true;
}

public sealed class PerkEntry
{
    internal PerkEntry(PerkDef def)
    {
        Def = def;
    }

    public PerkDef Def { get; }

    public int CurrentRank { get; internal set; }

    public int NextRank { get; internal set; }

    public PerkStatus Status { get; internal set; }

    public IReadOnlyList<string> UnmetRequirements { get; internal set; } = Array.Empty<string>();
}

public sealed class LevelUpModel
{
    private readonly Dictionary<string, PerkEntry> _entries = new();
    private readonly PerkEntry[] _ordered;
    private readonly Dictionary<string, int> _owned;
    private readonly List<PerkPick> _picks = new();
    private readonly IPerkRequirementChecker _checker;
    private readonly bool _requireAllPicksUsed;
    private readonly int _picksAvailable;
    private bool _showUnavailable;

    public LevelUpModel(IReadOnlyList<PerkDef> perks, IReadOnlyDictionary<string, int> ownedRanks, int picksAvailable,
                        IPerkRequirementChecker? checker = null, bool requireAllPicksUsed = false,
                        IFavouriteStore? favourites = null)
    {
        if (picksAvailable < 0) throw new ArgumentOutOfRangeException(nameof(picksAvailable));
        foreach (var perk in perks)
        {
            if (perk.Ranks.Count == 0) throw new ArgumentException($"Perk {perk.Id} has no ranks.", nameof(perks));
            if (!_entries.TryAdd(perk.Id, new PerkEntry(perk))) throw new ArgumentException($"Duplicate perk id {perk.Id}.", nameof(perks));
        }
        _owned = new Dictionary<string, int>(ownedRanks);
        _picksAvailable = picksAvailable;
        _checker = checker ?? new NoPerkRequirements();
        _requireAllPicksUsed = requireAllPicksUsed;
        _ordered = perks.Select(p => _entries[p.Id]).ToArray();
        Perks = new MenuList<PerkEntry>(_ordered, e => e.Def.Id, e => e.Def.Name, e => e.Def.Category, favourites);
        Perks.SetFilter("availability", e => _showUnavailable || e.Status == PerkStatus.Available);
        Categories = perks.Select(p => p.Category).Distinct().ToArray();
        Recompute();
    }

    public event Action? Changed;

    public MenuList<PerkEntry> Perks { get; }

    public IReadOnlyList<string> Categories { get; }

    public IReadOnlyList<PerkEntry> Entries => _ordered;

    public IReadOnlyList<PerkPick> Picks => _picks.ToArray();

    public int RemainingPicks => _picksAvailable - _picks.Count;

    public bool IsConfirmed { get; private set; }

    public bool IsCancelled { get; private set; }

    public bool IsFinished => IsConfirmed || IsCancelled;

    public IReadOnlyList<PerkPick> ConfirmedPicks { get; private set; } = Array.Empty<PerkPick>();

    public bool ShowUnavailable
    {
        get => _showUnavailable;
        set
        {
            _showUnavailable = value;
            Perks.Refresh();
        }
    }

    public PerkEntry? Entry(string perkId) => _entries.TryGetValue(perkId, out var entry) ? entry : null;

    public void Refresh()
    {
        Recompute();
        Changed?.Invoke();
    }

    public RankComparison? Compare(string perkId)
    {
        if (!_entries.TryGetValue(perkId, out var entry)) return null;
        var ranks = entry.Def.Ranks;
        return new RankComparison(
            entry.Def.Name,
            entry.CurrentRank,
            entry.NextRank,
            entry.CurrentRank > 0 ? ranks[entry.CurrentRank - 1].Text : null,
            entry.NextRank > 0 ? ranks[entry.NextRank - 1].Text : null);
    }

    public PickResult Pick(string perkId)
    {
        if (IsFinished) return PickResult.Finished;
        if (!_entries.TryGetValue(perkId, out var entry)) return PickResult.UnknownPerk;
        if (entry.Status == PerkStatus.Picked) return PickResult.AlreadyPicked;
        if (entry.Status == PerkStatus.Maxed) return PickResult.Maxed;
        if (RemainingPicks <= 0) return PickResult.NoPicksLeft;
        if (entry.Status == PerkStatus.Locked) return PickResult.RequirementsNotMet;
        _picks.Add(new PerkPick(perkId, entry.NextRank));
        Recompute();
        Changed?.Invoke();
        return PickResult.Picked;
    }

    public bool Unpick(string perkId)
    {
        if (IsFinished || _picks.RemoveAll(p => p.PerkId == perkId) == 0) return false;
        var kept = _picks.ToArray();
        _picks.Clear();
        foreach (var pick in kept)
        {
            var def = _entries[pick.PerkId].Def;
            if (Unmet(def, OwnedRank(def), Effective()).Count == 0) _picks.Add(pick);
        }
        Recompute();
        Changed?.Invoke();
        return true;
    }

    public LevelUpConfirm Confirm()
    {
        if (IsFinished) return LevelUpConfirm.AlreadyFinished;
        if (_requireAllPicksUsed && RemainingPicks > 0) return LevelUpConfirm.PicksRemaining;
        IsConfirmed = true;
        ConfirmedPicks = _picks.ToArray();
        Changed?.Invoke();
        return LevelUpConfirm.Confirmed;
    }

    public bool Cancel()
    {
        if (IsFinished) return false;
        IsCancelled = true;
        _picks.Clear();
        Recompute();
        Changed?.Invoke();
        return true;
    }

    private int OwnedRank(PerkDef def) => Math.Min(_owned.TryGetValue(def.Id, out var rank) ? rank : 0, def.Ranks.Count);

    private Dictionary<string, int> Effective()
    {
        var ranks = new Dictionary<string, int>(_owned);
        foreach (var pick in _picks) ranks[pick.PerkId] = pick.Rank;
        return ranks;
    }

    private List<string> Unmet(PerkDef def, int owned, IReadOnlyDictionary<string, int> effective) =>
        (def.Ranks[owned].Requires ?? Array.Empty<string>()).Where(r => !_checker.IsMet(r, effective)).ToList();

    private void Recompute()
    {
        var effective = Effective();
        foreach (var entry in _entries.Values)
        {
            var def = entry.Def;
            var owned = OwnedRank(def);
            entry.CurrentRank = owned;
            entry.UnmetRequirements = Array.Empty<string>();
            var pick = _picks.Find(p => p.PerkId == def.Id);
            if (pick is not null)
            {
                entry.NextRank = pick.Rank;
                entry.Status = PerkStatus.Picked;
            }
            else if (owned >= def.Ranks.Count)
            {
                entry.NextRank = 0;
                entry.Status = PerkStatus.Maxed;
            }
            else
            {
                entry.NextRank = owned + 1;
                var unmet = Unmet(def, owned, effective);
                entry.UnmetRequirements = unmet;
                entry.Status = unmet.Count == 0 ? PerkStatus.Available : PerkStatus.Locked;
            }
        }
        Perks.Refresh();
    }
}
