using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed class LevelUpController
{
    private readonly LevelUpModel _model;
    private readonly LiveList _source;
    private bool _showLocked;

    public LevelUpController(LevelUpModel model, IFavouriteStore? favourites = null, string title = "Level up")
    {
        _model = model;
        Title = title;
        _source = new LiveList(model.Entries, Listed);
        Perks = new ListMenuModel<PerkEntry>(_source, e => e.Def.Id, e => e.Def.Name, e => e.Def.Category, DetailOf, favourites);
        Perks.Accepted += OnAccepted;
        Perks.Changed += Raise;
    }

    public event Action? Changed;

    public event Action<bool>? Finished;

    public string Title { get; }

    public ListMenuModel<PerkEntry> Perks { get; }

    public string Message { get; private set; } = string.Empty;

    public bool IsFinished => _model.IsFinished;


    public IReadOnlyList<PerkPick> ConfirmedPicks => _model.ConfirmedPicks;

    public string Header => FormattableString.Invariant($"Picks left: {_model.RemainingPicks}");

    public IReadOnlyList<MenuTextLine> DetailLines
    {
        get
        {
            if (Perks.Selected is not { } entry) return new[] { new MenuTextLine("No perk selected.", LineTone.Muted) };
            var lines = new List<MenuTextLine>
            {
                new(entry.Def.Name, LineTone.Heading),
                new("Status: " + entry.Status, StatusTone(entry.Status)),
            };
            var compare = _model.Compare(entry.Def.Id)!;
            lines.Add(compare.CurrentText is null
                ? new MenuTextLine("Not owned yet.", LineTone.Muted)
                : new MenuTextLine(FormattableString.Invariant($"Current rank {compare.CurrentRank}: {compare.CurrentText}")));
            lines.Add(compare.NextText is null
                ? new MenuTextLine("At the highest rank.", LineTone.Muted)
                : new MenuTextLine(FormattableString.Invariant($"Next rank {compare.NextRank}: {compare.NextText}"), LineTone.Good));
            if (entry.UnmetRequirements.Count > 0)
                lines.Add(new MenuTextLine("Requires: " + string.Join(", ", entry.UnmetRequirements), LineTone.Bad));
            return lines;
        }
    }

    public IReadOnlyList<MenuHint> Hints
    {
        get
        {
            var hints = new List<MenuHint>();
            foreach (var hint in Perks.Hints)
            {
                if (Perks.IsSearching) hints.Add(hint);
                else if (hint.Action == MenuInputAction.Accept) hints.Add(hint with { Label = AcceptLabel() });
                else if (hint.Action == MenuInputAction.Back && hint.Label == "Back") hints.Add(hint with { Label = "Cancel" });
                else hints.Add(hint);
            }
            if (!Perks.IsSearching)
            {
                hints.Add(new MenuHint(MenuInputAction.Sort, "Confirm"));
                hints.Add(new MenuHint(MenuInputAction.Compare, _showLocked ? "Hide locked" : "Show locked"));
            }
            return hints;
        }
    }

    public bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        if (!Perks.IsSearching)
        {
            if (action == MenuInputAction.Sort)
            {
                Confirm();
                return true;
            }
            if (action == MenuInputAction.Compare)
            {
                ToggleLocked();
                return true;
            }
            if (action == MenuInputAction.Back && Perks.SearchText.Length == 0)
            {
                Cancel();
                return true;
            }
        }
        return Perks.Handle(action);
    }

    public void ToggleLocked()
    {
        _showLocked = !_showLocked;
        Message = _showLocked ? "Showing locked perks." : "Hiding locked perks.";
        Rebuild();
    }

    public bool Confirm()
    {
        switch (_model.Confirm())
        {
            case LevelUpConfirm.Confirmed:
                Finished?.Invoke(true);
                Raise();
                return true;
            case LevelUpConfirm.PicksRemaining:
                Message = FormattableString.Invariant($"Use all {_model.RemainingPicks} remaining picks first.");
                Raise();
                return false;
            default:
                return false;
        }
    }

    public void Cancel()
    {
        if (!_model.Cancel()) return;
        Finished?.Invoke(false);
        Raise();
    }

    public void Rebuild()
    {
        _source.Rebuild();
        Perks.Refresh();
        Raise();
    }

    private bool Listed(PerkEntry entry) =>
        _showLocked || entry.Status == PerkStatus.Available || entry.Status == PerkStatus.Picked;

    private string AcceptLabel() => Perks.Selected?.Status switch
    {
        PerkStatus.Picked => "Unpick",
        PerkStatus.Available => "Pick",
        _ => "Select",
    };

    private string DetailOf(PerkEntry entry) => entry.Status switch
    {
        PerkStatus.Picked => "Picked",
        PerkStatus.Locked => "Locked",
        PerkStatus.Maxed => "Max",
        _ => "Rank " + entry.NextRank.ToString(CultureInfo.InvariantCulture) + "/" + entry.Def.Ranks.Count.ToString(CultureInfo.InvariantCulture),
    };

    private void OnAccepted(PerkEntry entry)
    {
        var name = entry.Def.Name;
        if (entry.Status == PerkStatus.Picked)
        {
            _model.Unpick(entry.Def.Id);
            Message = "Removed " + name + ".";
        }
        else
        {
            Message = _model.Pick(entry.Def.Id) switch
            {
                PickResult.Picked => "Picked " + name + ".",
                PickResult.NoPicksLeft => "No picks left.",
                PickResult.RequirementsNotMet => "Requirements not met.",
                PickResult.Maxed => name + " is at its highest rank.",
                _ => string.Empty,
            };
        }
        Rebuild();
    }

    private void Raise() => Changed?.Invoke();

    private static LineTone StatusTone(PerkStatus status) => status switch
    {
        PerkStatus.Available => LineTone.Good,
        PerkStatus.Picked => LineTone.Good,
        PerkStatus.Locked => LineTone.Bad,
        _ => LineTone.Muted,
    };

    private sealed class LiveList : IReadOnlyList<PerkEntry>
    {
        private readonly IReadOnlyList<PerkEntry> _all;
        private readonly Func<PerkEntry, bool> _listed;
        private List<PerkEntry> _items;

        public LiveList(IReadOnlyList<PerkEntry> all, Func<PerkEntry, bool> listed)
        {
            _all = all;
            _listed = listed;
            _items = all.Where(listed).ToList();
        }

        public int Count => _items.Count;

        public PerkEntry this[int index] => _items[index];

        public void Rebuild() => _items = _all.Where(_listed).ToList();

        public IEnumerator<PerkEntry> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
