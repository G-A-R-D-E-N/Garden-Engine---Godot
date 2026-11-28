using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Service;

public sealed class ModManagerModel
{
    private readonly IModLibrary _library;
    private readonly List<ModEntry> _working = new();
    private readonly ListMenuModel<ModEntry> _list;
    private List<ModEntry> _baseline = new();
    private IReadOnlyList<ModProblem> _problems = Array.Empty<ModProblem>();
    private bool _closePending;

    public ModManagerModel(IModLibrary library)
    {
        _library = library;
        var sorts = new[]
        {
            new SortKey<ModEntry>("Name", ByName),
            new SortKey<ModEntry>("Type", (a, b) => Then(a.Kind.CompareTo(b.Kind), a, b)),
            new SortKey<ModEntry>("State", (a, b) => Then(b.Enabled.CompareTo(a.Enabled), a, b)),
        };
        _list = new ListMenuModel<ModEntry>(_working, e => e.FileName, e => e.FileName,
            detailOf: e => ServiceText.KindLabel(e.Kind), sorts: sorts);
        View = new ServiceListView<ModEntry>(_list, RowText) { Extra = Handle, HintsOf = Hints, SummaryText = () => Summary };
        Reload();
    }

    public ServiceListView<ModEntry> View { get; }

    public string Status { get; private set; } = string.Empty;

    public bool RestartRequired { get; private set; }

    public IReadOnlyList<ModEntry> Order => _working;

    public IReadOnlyList<ModProblem> Problems => _problems;

    public ModEntry? Selected => _list.Selected;

    public bool Dirty => !_working.Select(e => (e.FileName, e.Enabled)).SequenceEqual(_baseline.Select(e => (e.FileName, e.Enabled)));

    public string Summary
    {
        get
        {
            var text = _list.Summary + FormattableString.Invariant($", {_working.Count(e => e.Enabled)} enabled");
            if (Dirty) text += ", unsaved changes";
            var errors = _problems.Count(p => p.Severity == ProblemSeverity.Error);
            return errors > 0 ? text + FormattableString.Invariant($", {errors} problem(s)") : text;
        }
    }

    public string DetailText
    {
        get
        {
            var text = Selected is { } entry ? Details(entry) : "No plugins to show.";
            return Status.Length > 0 ? text + "\n\n" + Status : text;
        }
    }

    public void Reload()
    {
        _baseline = _library.Load().ToList();
        _closePending = false;
        RestartRequired = false;
        Status = string.Empty;
        RestoreBaseline();
    }

    public bool ToggleSelected()
    {
        if (Selected is not { } entry) return false;
        if (entry.Mandatory) return Refuse(entry.FileName + " is required and cannot be disabled.");
        if (!entry.Enabled && entry.Missing) return Refuse(entry.FileName + " is missing and cannot be enabled.");
        _working[IndexOf(entry)] = entry with { Enabled = !entry.Enabled };
        return Edited((entry.Enabled ? "Disabled " : "Enabled ") + entry.FileName + ".");
    }

    public bool MoveSelected(int delta)
    {
        if (Selected is not { } entry) return false;
        if (_list.SortIndex >= 0) return Refuse("Clear the sort to see load order before moving a plugin.");
        var from = IndexOf(entry);
        var to = from + delta;
        if (to < 0 || to >= _working.Count) return Refuse(entry.FileName + (delta < 0 ? " is already first." : " is already last."));
        var other = _working[to];
        if (entry.Mandatory || other.Mandatory)
            return Refuse((entry.Mandatory ? entry.FileName : other.FileName) + " is required and keeps its place.");
        _working[to] = entry;
        _working[from] = other;
        return Edited(entry.FileName + (delta < 0 ? " now loads earlier." : " now loads later."));
    }

    public bool Apply()
    {
        if (!Dirty) return Refuse("There are no changes to apply.");
        var problems = _library.Validate(_working.ToList());
        var errors = problems.Where(p => p.Severity == ProblemSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            _problems = problems;
            var more = errors.Count > 1 ? FormattableString.Invariant($" (and {errors.Count - 1} more)") : string.Empty;
            return Refuse("Cannot apply: " + errors[0].Message + more);
        }
        var result = _library.Apply(_working.ToList());
        if (!result.Ok) return Refuse("Apply failed: " + result.Message);
        _baseline = _working.ToList();
        _closePending = false;
        RestartRequired = true;
        Revalidate();
        SetStatus("Applied. Restart the game to use the new load order.");
        return true;
    }

    public void Discard()
    {
        _closePending = false;
        RestoreBaseline();
        SetStatus("Changes discarded.");
    }

    public bool Focus(string fileName)
    {
        _list.SetSearch(string.Empty);
        var index = _list.Rows.ToList().FindIndex(e => e.FileName == fileName);
        if (index < 0) return Refuse(fileName + " is not in the load order.");
        return _list.Select(index);
    }

    public string Details(ModEntry entry)
    {
        var text = new StringBuilder();
        text.AppendLine(entry.FileName);
        text.AppendLine("Type: " + ServiceText.KindLabel(entry.Kind));
        text.AppendLine("State: " + (entry.Enabled ? "Enabled" : "Disabled") + (entry.Mandatory ? " (required)" : string.Empty));
        text.AppendLine(FormattableString.Invariant($"Load order: {IndexOf(entry) + 1} of {_working.Count}"));
        text.AppendLine("Path: " + entry.Path);
        text.AppendLine("Masters: " + (entry.Masters.Count == 0 ? "none" : string.Join(", ", entry.Masters)));
        text.AppendLine("Records: " + (entry.RecordCount < 0 ? "unknown" : entry.RecordCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (entry.Missing) text.AppendLine("The file is missing from the configured folders.");
        if (entry.Error.Length > 0) text.AppendLine("Error: " + entry.Error);
        foreach (var problem in _problems.Where(p => p.FileName == entry.FileName))
            text.AppendLine((problem.Severity == ProblemSeverity.Error ? "Problem: " : "Warning: ") + problem.Message);
        return text.ToString().TrimEnd();
    }

    private bool Handle(MenuInputAction action)
    {
        var wasPending = _closePending;
        if (action != MenuInputAction.Back) _closePending = false;
        switch (action)
        {
            case MenuInputAction.Accept: ToggleSelected(); return true;
            case MenuInputAction.TabPrev: MoveSelected(-1); return true;
            case MenuInputAction.TabNext: MoveSelected(1); return true;
            case MenuInputAction.Favourite: Apply(); return true;
            case MenuInputAction.Compare: Discard(); return true;
            case MenuInputAction.Back: return GuardClose(wasPending);
            default: return false;
        }
    }

    private bool GuardClose(bool wasPending)
    {
        if (!Dirty) return false;
        if (wasPending)
        {
            Discard();
            return false;
        }
        _closePending = true;
        SetStatus("Unsaved changes. Press Back again to discard them and close, or Apply them first.");
        return true;
    }

    private IReadOnlyList<MenuHint> Hints()
    {
        var toggle = Selected is { Enabled: true } ? "Disable" : "Enable";
        return
        [
            new MenuHint(MenuInputAction.Accept, toggle),
            new MenuHint(MenuInputAction.Back, Dirty ? "Back (unsaved changes)" : "Back"),
            new MenuHint(MenuInputAction.Search, "Search"),
            new MenuHint(MenuInputAction.Sort, View.SortLabel),
            new MenuHint(MenuInputAction.Compare, "Discard changes"),
            new MenuHint(MenuInputAction.Favourite, "Apply"),
            new MenuHint(MenuInputAction.TabPrev, "Move earlier"),
            new MenuHint(MenuInputAction.TabNext, "Move later"),
        ];
    }

    private string RowText(ModEntry entry)
    {
        var text = (IndexOf(entry) + 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture)
            + (entry.Enabled ? " [x] " : " [ ] ") + entry.FileName;
        if (entry.Mandatory) text += " (required)";
        if (entry.Missing) text += " (missing)";
        else if (entry.Error.Length > 0) text += " (unreadable)";
        return text;
    }

    private int IndexOf(ModEntry entry) => _working.FindIndex(e => e.FileName == entry.FileName);

    private void RestoreBaseline()
    {
        _working.Clear();
        _working.AddRange(_baseline);
        Revalidate();
        _list.Refresh();
    }

    private void Revalidate() => _problems = _library.Validate(_working.ToList());

    private bool Edited(string status)
    {
        Revalidate();
        SetStatus(status);
        return true;
    }

    private bool Refuse(string status)
    {
        SetStatus(status);
        return false;
    }

    private void SetStatus(string status)
    {
        Status = status;
        _list.Refresh();
    }

    private static int ByName(ModEntry a, ModEntry b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);

    private static int Then(int primary, ModEntry a, ModEntry b) => primary != 0 ? primary : ByName(a, b);
}
