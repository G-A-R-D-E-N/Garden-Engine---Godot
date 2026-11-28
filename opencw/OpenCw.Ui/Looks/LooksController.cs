using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Looks;

public enum LooksRowKind
{
    Category,
    Option,
    Done,
}

public sealed record LooksRow(LooksRowKind Kind, string Key, string Text, string? OptionId = null);

public sealed class LooksController : ListDetailController<LooksRow>
{
    public const string NoneText = "None";
    public const string DoneText = "Done";
    public const string DoneHelp = "Keep these looks and close.";
    public const string CategoriesHeader = "Categories";
    public const string ChangedText = "Changed";
    public const string DiscardText = "Press Back again to discard your changes.";
    public const string RefusedText = "That option is not available.";

    private readonly ILooksSession _session;
    private readonly List<LooksCategory> _categories;
    private readonly Dictionary<string, string?> _opening = new();
    private readonly Dictionary<string, string?> _current = new();
    private LooksCategory? _category;
    private string? _entry;
    private bool _syncing;
    private bool _confirmDiscard;

    private LooksController(ILooksSession session, List<LooksCategory> categories)
        : base(new PlainListModel<LooksRow>(row => RowKey(row.Kind, row.Key), row => row.Text))
    {
        _session = session;
        _categories = categories;
        foreach (var category in categories)
        {
            _opening[category.Id] = category.SelectedId;
            _current[category.Id] = category.SelectedId;
        }
        Rows.Accepted += OnAccepted;
        Rows.Changed += OnRowsChanged;
        ShowCategories(null);
    }

    public override string Title => _session.Context switch
    {
        LooksContext.Haircut => "Haircut",
        LooksContext.BodyMod => "Body",
        _ => "Looks",
    };

    public override string Header => _category?.Name ?? CategoriesHeader;

    public bool InOptions => _category is not null;

    public bool IsDirty => _categories.Any(category => !Equals(_current[category.Id], _opening[category.Id]));

    public IReadOnlyList<LooksCategory> Categories => _categories;

    public override IReadOnlyList<MenuHint> Hints => InOptions
        ? new[] { new MenuHint(MenuInputAction.Accept, "Keep"), new MenuHint(MenuInputAction.Back, "Cancel") }
        : new[]
        {
            new MenuHint(MenuInputAction.Accept, "Select"),
            new MenuHint(MenuInputAction.Left, "Previous"),
            new MenuHint(MenuInputAction.Right, "Next"),
            new MenuHint(MenuInputAction.Back, "Close"),
        };

    public override IReadOnlyList<MenuTextLine> DetailLines
    {
        get
        {
            var lines = new List<MenuTextLine>();
            var category = _category ?? (Rows.Selected is { Kind: LooksRowKind.Category } row ? Find(row.Key) : null);
            if (category is null)
            {
                if (Rows.Selected is { Kind: LooksRowKind.Done })
                    lines.Add(new MenuTextLine(DoneHelp, LineTone.Muted));
            }
            else
            {
                var current = _current[category.Id];
                lines.Add(new MenuTextLine(category.Name, LineTone.Heading));
                lines.Add(new MenuTextLine((InOptions ? "Previewing: " : "Current: ") + NameOf(category, current) + " (" + Position(category, current) + ")"));
                if (OptionOf(category, current)?.Description is { Length: > 0 } description)
                    lines.Add(new MenuTextLine(description, LineTone.Muted));
            }
            if (IsDirty)
                lines.Add(new MenuTextLine(ChangedText, LineTone.Good));
            return lines;
        }
    }

    public static LooksController? TryCreate(ILooksSession? session)
    {
        if (session is null || !session.IsValid) return null;
        var categories = Clean(session.Categories);
        return categories.Count == 0 ? null : new LooksController(session, categories);
    }

    public string? Current(string categoryId) => _current.GetValueOrDefault(categoryId);

    public override bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        if (action != MenuInputAction.Back && _confirmDiscard)
        {
            _confirmDiscard = false;
            Say(string.Empty);
        }
        switch (action)
        {
            case MenuInputAction.Back:
                return Back();
            case MenuInputAction.Left:
                return !InOptions && Step(-1);
            case MenuInputAction.Right:
                return !InOptions && Step(1);
            default:
                return Rows.Handle(action);
        }
    }

    public override void Tick(double deltaSeconds)
    {
        base.Tick(deltaSeconds);
        if (!IsFinished && !_session.IsValid) Finish(false);
    }

    private static string RowKey(LooksRowKind kind, string key) => (int)kind + "/" + key;

    private static List<LooksCategory> Clean(IReadOnlyList<LooksCategory>? source) =>
        (source ?? Array.Empty<LooksCategory>())
            .Where(category => category is not null && !string.IsNullOrWhiteSpace(category.Id) && !string.IsNullOrWhiteSpace(category.Name))
            .GroupBy(category => category.Id)
            .Select(group => group.First())
            .Select(category => category with
            {
                Options = (category.Options ?? Array.Empty<LooksOption>())
                    .Where(option => option is not null && !string.IsNullOrWhiteSpace(option.Id) && !string.IsNullOrWhiteSpace(option.Name))
                    .ToList(),
            })
            .Where(category => category.Options.Count > 0)
            .ToList();

    private static List<string?> Sequence(LooksCategory category)
    {
        var sequence = new List<string?>();
        if (category.Optional) sequence.Add(null);
        sequence.AddRange(category.Options.Select(option => option.Id));
        return sequence;
    }

    private static LooksOption? OptionOf(LooksCategory category, string? id) =>
        id is null ? null : category.Options.FirstOrDefault(option => option.Id == id);

    private static string NameOf(LooksCategory category, string? id) => OptionOf(category, id)?.Name ?? NoneText;

    private static string Position(LooksCategory category, string? id)
    {
        var sequence = Sequence(category);
        return (sequence.IndexOf(id) + 1).ToString(CultureInfo.InvariantCulture) + " / " + sequence.Count.ToString(CultureInfo.InvariantCulture);
    }

    private LooksCategory? Find(string id) => _categories.FirstOrDefault(category => category.Id == id);

    private void ShowCategories(string? selectId)
    {
        var rows = _categories
            .Select(category => new LooksRow(LooksRowKind.Category, category.Id, category.Name + ": " + NameOf(category, _current[category.Id])))
            .Append(new LooksRow(LooksRowKind.Done, string.Empty, DoneText))
            .ToList();
        Rows.ShowRows(rows, selectId is null ? null : RowKey(LooksRowKind.Category, selectId));
    }

    private void OpenOptions(LooksCategory category)
    {
        _category = category;
        _entry = _current[category.Id];
        var rows = new List<LooksRow>();
        if (category.Optional) rows.Add(new LooksRow(LooksRowKind.Option, string.Empty, NoneText));
        rows.AddRange(category.Options.Select(option => new LooksRow(LooksRowKind.Option, option.Id, option.Name, option.Id)));
        _syncing = true;
        Rows.ShowRows(rows, RowKey(LooksRowKind.Option, _entry ?? string.Empty));
        _syncing = false;
    }

    private void Leave(LooksCategory category)
    {
        _category = null;
        _entry = null;
        ShowCategories(category.Id);
    }

    private bool Back()
    {
        if (_category is { } category)
        {
            if (!Equals(_current[category.Id], _entry) && !Apply(category, _entry))
            {
                _current[category.Id] = _entry;
                _session.Preview(category.Id, _entry);
                Raise();
            }
            Leave(category);
            return true;
        }
        if (IsDirty && !_confirmDiscard)
        {
            _confirmDiscard = true;
            Say(DiscardText);
            Sound("menu.blocked");
            return true;
        }
        Cancel();
        return true;
    }

    private bool Step(int delta)
    {
        if (Rows.Selected is not { Kind: LooksRowKind.Category } row || Find(row.Key) is not { } category) return false;
        var sequence = Sequence(category);
        var index = sequence.IndexOf(_current[category.Id]);
        var next = index < 0
            ? (delta > 0 ? 0 : sequence.Count - 1)
            : ((index + delta) % sequence.Count + sequence.Count) % sequence.Count;
        if (Apply(category, sequence[next])) ShowCategories(category.Id);
        return true;
    }

    private bool Apply(LooksCategory category, string? optionId)
    {
        var result = _session.Preview(category.Id, optionId);
        if (!result.Ok)
        {
            Say(result.Reason.Length > 0 ? result.Reason : RefusedText);
            Sound("menu.blocked");
            return false;
        }
        _current[category.Id] = optionId;
        Raise();
        return true;
    }

    private void Reselect(LooksCategory category)
    {
        _syncing = true;
        Rows.SelectKey(RowKey(LooksRowKind.Option, _current[category.Id] ?? string.Empty));
        _syncing = false;
    }

    private void OnRowsChanged()
    {
        if (_syncing || _category is not { } category || Rows.Selected is not { Kind: LooksRowKind.Option } row) return;
        if (Equals(row.OptionId, _current[category.Id])) return;
        if (!Apply(category, row.OptionId)) Reselect(category);
    }

    private void OnAccepted(LooksRow row)
    {
        if (IsFinished) return;
        switch (row.Kind)
        {
            case LooksRowKind.Category:
                if (Find(row.Key) is { } opened) OpenOptions(opened);
                break;
            case LooksRowKind.Option:
                if (_category is not { } category) break;
                if (!Equals(_current[category.Id], row.OptionId) && !Apply(category, row.OptionId))
                {
                    Reselect(category);
                    break;
                }
                Sound("menu.confirm");
                Leave(category);
                break;
            case LooksRowKind.Done:
                Commit();
                break;
        }
    }

    private void Commit()
    {
        _session.Commit(_categories.Select(category => new LooksChoice(category.Id, _current[category.Id])).ToList());
        Sound("menu.confirm");
        Finish(true);
    }

    private void Cancel()
    {
        foreach (var category in _categories)
            if (!Equals(_current[category.Id], _opening[category.Id]))
                _session.Preview(category.Id, _opening[category.Id]);
        _session.Cancel();
        Finish(false);
    }
}
