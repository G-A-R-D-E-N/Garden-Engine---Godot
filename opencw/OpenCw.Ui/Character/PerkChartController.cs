using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Character;

public enum PipState { Filled, Outlined, Empty }

public sealed record ChartColumn(int Index, string Title, int? Value, IReadOnlyList<PerkChartEntry> Rows);

public sealed class PerkChartController
{
    public const int AttributeColumns = 7;
    public const int OtherColumn = 7;
    public const double ErrorSeconds = 2.0;
    public const double ChoiceTimeoutSeconds = 3.0;
    public const string NoPerksMessage = "No perks are available yet.";
    public const string NothingAvailableMessage = "Nothing is available to take";

    private readonly HashSet<string> _newIds = new();
    private readonly HashSet<string> _reported = new();
    private PerkChartSnapshot? _snapshot;
    private List<PerkChartEntry> _valid = new();
    private List<ChartColumn> _columns = new();
    private string? _selectedId;
    private int _viewed = 1;
    private bool _availableOnly;
    private bool _confirming;
    private bool _awaiting;
    private double _awaitLeft;
    private double _errorLeft;
    private string? _awaitId;
    private int _awaitRank;

    public PerkChartController(PerkChartMode mode = PerkChartMode.Spend)
    {
        Mode = mode;
    }

    public event Action<string, int>? ChoiceRequested;

    public event Action<string>? SelectionChanged;

    public event Action? CloseRequested;

    public event Action<string>? SoundRequested;

    public event Action<string>? EntryRejected;

    public event Action? Changed;

    public PerkChartMode Mode { get; private set; }

    public int Revision => _snapshot?.Revision ?? -1;

    public int Level => _snapshot?.Level ?? 0;

    public int PerkPoints => _snapshot?.PerkPoints ?? 0;

    public IReadOnlyList<ChartColumn> Columns => _columns;

    public string? SelectedId => _selectedId;

    public bool IsConfirming => _confirming;

    public bool IsAwaitingModel => _awaiting;

    public bool AvailableOnly => _availableOnly;

    public int ViewedRank => _viewed;

    public string ErrorLine { get; private set; } = string.Empty;

    public PerkChartEntry? Selected => _selectedId is null ? null : _valid.Find(e => e.Id == _selectedId);

    public string HeaderLine => F($"Level {Level}");

    public string? PointsLine => Mode == PerkChartMode.Browse && PerkPoints <= 0 ? null : F($"Perk points: {PerkPoints}");

    public string? EmptyMessage
    {
        get
        {
            if (_valid.Count == 0) return NoPerksMessage;
            return _availableOnly && !_valid.Any(e => e.Kind == PerkChartKind.Perk && e.Available) ? NothingAvailableMessage : null;
        }
    }

    public bool CanChoose => Mode == PerkChartMode.Spend && !_awaiting && !_confirming && Selected is { } e
        && e.CurrentRank < e.MaxRank && e.Available && PerkPoints >= CostOf(e) && _viewed == e.CurrentRank + 1;

    public string DetailName => Selected?.Name.ToUpperInvariant() ?? string.Empty;

    public string DetailDescription => Selected is { } e && _viewed >= 1 && _viewed <= e.MaxRank ? e.Ranks[_viewed - 1].Description : string.Empty;

    public string ConfirmPrompt => Selected is { } e ? F($"Take {e.Name} rank {_viewed}? Points after: {PerkPoints - CostOf(e)}") : string.Empty;

    public string? ViewingLine
    {
        get
        {
            if (Selected is not { } e || e.MaxRank < 1 || _viewed == e.CurrentRank + 1) return null;
            var note = _viewed > e.CurrentRank + 1 ? " (not your next rank)" : _viewed <= e.CurrentRank ? " (already taken)" : string.Empty;
            return F($"Viewing rank {_viewed} of {e.MaxRank}") + note;
        }
    }

    public IReadOnlyList<string> RequirementLines
    {
        get
        {
            if (_confirming) return new[] { ConfirmPrompt };
            if (Selected is not { } e || _viewed <= e.CurrentRank || _viewed > e.MaxRank) return Array.Empty<string>();
            return e.Ranks[_viewed - 1].UnmetOrEmpty.Select(Describe).ToArray();
        }
    }

    public IReadOnlyList<PipState> Pips
    {
        get
        {
            if (Selected is not { } e || e.MaxRank <= 1) return Array.Empty<PipState>();
            var outline = Mode == PerkChartMode.Spend && e.Available && PerkPoints >= CostOf(e);
            return Enumerable.Range(1, e.MaxRank)
                .Select(r => r <= e.CurrentRank ? PipState.Filled : r == e.CurrentRank + 1 && outline ? PipState.Outlined : PipState.Empty)
                .ToArray();
        }
    }

    public IReadOnlyList<MenuHint> Hints
    {
        get
        {
            var hints = new List<MenuHint>();
            if (_confirming)
            {
                hints.Add(new MenuHint(MenuInputAction.Accept, "Confirm"));
                hints.Add(new MenuHint(MenuInputAction.Back, "Cancel"));
                return hints;
            }
            if (Mode == PerkChartMode.Spend && Selected is { } e && e.CurrentRank < e.MaxRank) hints.Add(new MenuHint(MenuInputAction.Accept, "Choose"));
            hints.Add(new MenuHint(MenuInputAction.Back, "Close"));
            hints.Add(new MenuHint(MenuInputAction.TabPrev, "Previous rank"));
            hints.Add(new MenuHint(MenuInputAction.TabNext, "Next rank"));
            hints.Add(new MenuHint(MenuInputAction.Compare, _availableOnly ? "Show all" : "Show only available"));
            return hints;
        }
    }

    public static string Badge(PerkChartEntry e) =>
        e.CurrentRank >= e.MaxRank ? "OWNED" : e.CurrentRank > 0 ? "IN PROGRESS" : e.Available ? "AVAILABLE" : "LOCKED";

    public static string? RankText(PerkChartEntry e) => e.MaxRank > 1 ? F($"{e.CurrentRank} / {e.MaxRank}") : null;

    public bool IsNew(string id) => _newIds.Contains(id);

    public void MarkNew(IEnumerable<string> ids)
    {
        foreach (var id in ids) _newIds.Add(id);
        Raise();
    }

    public void SetMode(PerkChartMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        if (mode == PerkChartMode.Browse) _confirming = false;
        Raise();
    }

    public bool SetSnapshot(PerkChartSnapshot snapshot)
    {
        if (_snapshot is not null && snapshot.Revision < _snapshot.Revision) return false;
        var newer = _snapshot is null || snapshot.Revision > _snapshot.Revision;
        var before = Selected;
        var column = before is null ? -1 : ColumnKey(before);
        var row = before is null ? 0 : RowOf(before.Id);
        _snapshot = snapshot;
        _valid = Validate(snapshot.Entries);
        if (_awaiting && (newer || _valid.Any(e => e.Id == _awaitId && e.CurrentRank >= _awaitRank)))
        {
            _awaiting = false;
            _awaitLeft = 0;
        }
        Rebuild();
        Reselect(before, column, row);
        if (_confirming && !StillConfirmable())
        {
            _confirming = false;
            SetError("That choice is no longer available");
        }
        Raise();
        return true;
    }

    public bool Select(string id) => SelectUser(id);

    public bool Handle(MenuInputAction action)
    {
        if (_confirming)
        {
            if (action == MenuInputAction.Accept) Confirm();
            else if (action == MenuInputAction.Back) CancelConfirm();
            return true;
        }
        switch (action)
        {
            case MenuInputAction.Up: Move(0, -1); return true;
            case MenuInputAction.Down: Move(0, 1); return true;
            case MenuInputAction.Left: Move(-1, 0); return true;
            case MenuInputAction.Right: Move(1, 0); return true;
            case MenuInputAction.TabPrev: ViewRank(-1); return true;
            case MenuInputAction.TabNext: ViewRank(1); return true;
            case MenuInputAction.Compare:
            case MenuInputAction.Favourite: ToggleAvailableOnly(); return true;
            case MenuInputAction.Accept: Accept(); return true;
            case MenuInputAction.Back: CloseRequested?.Invoke(); return true;
            default: return false;
        }
    }

    public void Accept()
    {
        if (Mode != PerkChartMode.Spend || _awaiting || _confirming || Selected is not { } e || e.CurrentRank >= e.MaxRank) return;
        if (PerkPoints < CostOf(e))
        {
            SetError("No perk points to spend");
            return;
        }
        if (!e.Available)
        {
            SetError("Requirements not met");
            Sound("cancel");
            return;
        }
        if (_viewed != e.CurrentRank + 1) return;
        _confirming = true;
        Sound("ok");
        Raise();
    }

    public void Confirm()
    {
        if (!_confirming || Selected is not { } e) return;
        _confirming = false;
        _awaiting = true;
        _awaitLeft = ChoiceTimeoutSeconds;
        _awaitId = e.Id;
        _awaitRank = _viewed;
        Sound("ok");
        if (e.Kind == PerkChartKind.Perk) Sound(_viewed == 1 ? "perk.first" : "perk.rank");
        ChoiceRequested?.Invoke(e.Id, _viewed);
        Raise();
    }

    public void CancelConfirm()
    {
        if (!_confirming) return;
        _confirming = false;
        Sound("cancel");
        Raise();
    }

    public void Dismiss()
    {
        if (!_confirming) return;
        _confirming = false;
        Raise();
    }

    public void Unlock()
    {
        if (!_awaiting) return;
        _awaiting = false;
        _awaitLeft = 0;
        Raise();
    }

    public void ShowError(string text) => SetError(text);

    public void ToggleAvailableOnly()
    {
        if (_confirming) return;
        var before = Selected;
        var column = before is null ? -1 : ColumnKey(before);
        var row = before is null ? 0 : RowOf(before.Id);
        _availableOnly = !_availableOnly;
        Rebuild();
        Reselect(before, column, row);
        Raise();
    }

    public void ViewRank(int delta)
    {
        if (_confirming || Selected is not { } e) return;
        var next = Math.Clamp(_viewed + delta, 1, Math.Max(1, e.MaxRank));
        if (next == _viewed) return;
        _viewed = next;
        Sound("rank.view");
        Raise();
    }

    public void Tick(double seconds)
    {
        if (!(seconds > 0)) return;
        var dirty = false;
        if (_errorLeft > 0)
        {
            _errorLeft -= seconds;
            if (_errorLeft <= 0)
            {
                ErrorLine = string.Empty;
                dirty = true;
            }
        }
        if (_awaiting)
        {
            _awaitLeft -= seconds;
            if (_awaitLeft <= 0)
            {
                _awaiting = false;
                SetError("Could not apply that choice");
                dirty = true;
            }
        }
        if (dirty) Raise();
    }

    private static string F(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string Describe(UnmetRequirement u) => u.Kind switch
    {
        UnmetKind.Level => F($"Requires level {u.Needed} (you are {u.Have})"),
        UnmetKind.Attribute => F($"Requires {u.Name} {u.Needed} (you have {u.Have})"),
        UnmetKind.Perk => F($"Requires {u.Name} rank {u.Needed}"),
        _ => "Requires " + u.Name,
    };

    private int CostOf(PerkChartEntry e) => Math.Max(1, e.Ranks[Math.Min(e.CurrentRank, e.MaxRank - 1)].Cost);

    private static int ColumnKey(PerkChartEntry e) => e.Column is >= 0 and < AttributeColumns ? e.Column.Value : OtherColumn;

    private List<PerkChartEntry> Validate(IReadOnlyList<PerkChartEntry> entries)
    {
        var seen = new HashSet<string>();
        var valid = new List<PerkChartEntry>();
        foreach (var e in entries)
        {
            var ok = !string.IsNullOrEmpty(e.Id) && !string.IsNullOrWhiteSpace(e.Name) && e.MaxRank >= 1 && e.CurrentRank >= 0
                && e.CurrentRank <= e.MaxRank && e.Ranks.Count == e.MaxRank && seen.Add(e.Id);
            if (ok) valid.Add(e);
            else if (_reported.Add(e.Id ?? string.Empty)) EntryRejected?.Invoke(e.Id ?? string.Empty);
        }
        return valid;
    }

    private void Rebuild()
    {
        var attributes = _snapshot?.Attributes ?? Array.Empty<int>();
        var visible = _valid.Where(e => !_availableOnly || e.Kind == PerkChartKind.Attribute || e.Available);
        _columns = visible.GroupBy(ColumnKey).OrderBy(g => g.Key).Select(g =>
        {
            var head = _valid.Find(e => e.Kind == PerkChartKind.Attribute && ColumnKey(e) == g.Key);
            var title = g.Key == OtherColumn ? "Other" : head?.Name ?? F($"Column {g.Key + 1}");
            int? value = g.Key < attributes.Count && g.Key < OtherColumn ? attributes[g.Key] : null;
            var rows = g.OrderBy(e => e.Kind == PerkChartKind.Attribute ? 0 : 1)
                .ThenBy(e => e.Kind == PerkChartKind.Attribute ? 0 : e.Ranks[0].RequiredAttribute?.Amount ?? 0)
                .ThenBy(e => e.Kind == PerkChartKind.Attribute ? 0 : e.Ranks[0].RequiredLevel)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new ChartColumn(g.Key, title, value, rows);
        }).ToList();
    }

    private void Reselect(PerkChartEntry? before, int column, int row)
    {
        var previous = _selectedId;
        if (before is not null && Position(before.Id) is not null)
        {
            _selectedId = before.Id;
        }
        else if (before is not null && _columns.Find(c => c.Index == column) is { Rows.Count: > 0 } same)
        {
            _selectedId = same.Rows[Math.Min(row, same.Rows.Count - 1)].Id;
        }
        else
        {
            _selectedId = _columns.Count > 0 ? _columns[0].Rows[0].Id : null;
        }
        var now = Selected;
        if (_selectedId != previous || now is null || now.CurrentRank != before?.CurrentRank) ResetViewed();
        else _viewed = Math.Clamp(_viewed, 1, now.MaxRank);
        if (_selectedId != previous && _selectedId is not null) SelectionChanged?.Invoke(_selectedId);
    }

    private void ResetViewed()
    {
        _viewed = Selected is { } e ? (e.CurrentRank < e.MaxRank ? e.CurrentRank + 1 : e.MaxRank) : 1;
    }

    private (int Col, int Row)? Position(string id)
    {
        for (var c = 0; c < _columns.Count; c++)
        {
            for (var r = 0; r < _columns[c].Rows.Count; r++)
            {
                if (_columns[c].Rows[r].Id == id) return (c, r);
            }
        }
        return null;
    }

    private int RowOf(string id) => Position(id)?.Row ?? 0;

    private void Move(int dx, int dy)
    {
        if (_selectedId is null || Position(_selectedId) is not { } pos) return;
        var col = Math.Clamp(pos.Col + dx, 0, _columns.Count - 1);
        var row = dx != 0 ? Math.Min(pos.Row, _columns[col].Rows.Count - 1) : Math.Clamp(pos.Row + dy, 0, _columns[col].Rows.Count - 1);
        if (col == pos.Col && row == pos.Row) return;
        SelectUser(_columns[col].Rows[row].Id);
    }

    private bool SelectUser(string id)
    {
        if (_confirming || Position(id) is null) return false;
        if (id == _selectedId) return true;
        _selectedId = id;
        ResetViewed();
        Sound("focus.move");
        if (Selected is { Kind: PerkChartKind.Perk, CurrentRank: > 0 }) Sound("perk.preview");
        SelectionChanged?.Invoke(id);
        Raise();
        return true;
    }

    private bool StillConfirmable() =>
        Selected is { } e && e.Available && e.CurrentRank < e.MaxRank && _viewed == e.CurrentRank + 1 && PerkPoints >= CostOf(e);

    private void SetError(string text)
    {
        ErrorLine = text;
        _errorLeft = ErrorSeconds;
        Raise();
    }

    private void Sound(string name) => SoundRequested?.Invoke(name);

    private void Raise() => Changed?.Invoke();
}
