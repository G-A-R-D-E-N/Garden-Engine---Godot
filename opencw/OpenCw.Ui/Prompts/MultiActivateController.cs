using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Prompts;

public sealed record ActivationOption(string Text, bool Enabled = true, string DisabledReason = "", int? Slot = null);

public sealed record ActivateRow(int Index, int Slot, string Text, bool Enabled, string Reason);

public sealed class MultiActivateController : ListDetailController<ActivateRow>
{
    public const int MaxOptions = 4;
    public const int MinOptions = 2;
    public const double LookAwaySeconds = 0.5;
    public const string DisabledText = "Not available.";

    private static readonly string[] SlotNames = { "Down", "Right", "Left", "Up" };

    private readonly Func<bool> _isValid;
    private readonly Func<bool> _isAimed;
    private Action<int>? _chosen;
    private IReadOnlyList<ActivationOption> _options;
    private double _awayFor;

    private MultiActivateController(IReadOnlyList<ActivationOption> options, Func<bool> isValid, Func<bool> isAimed,
                                    Action<int> chosen)
        : base(new PlainListModel<ActivateRow>(row => row.Index.ToString(CultureInfo.InvariantCulture), TextOf))
    {
        _options = options;
        _isValid = isValid;
        _isAimed = isAimed;
        _chosen = chosen;
        Rebuild(null);
        Rows.Accepted += row => Choose(row.Index);
        Finished += _ => _chosen = null;
    }

    public override string Title => "Activate";

    public override IReadOnlyList<MenuHint> Hints => new[]
    {
        new MenuHint(MenuInputAction.Accept, "Choose"),
        new MenuHint(MenuInputAction.Back, "Cancel"),
    };

    public override IReadOnlyList<MenuTextLine> DetailLines
    {
        get
        {
            var row = Rows.Selected;
            if (row is null || row.Enabled) return Array.Empty<MenuTextLine>();
            return new[] { new MenuTextLine(row.Reason.Length > 0 ? row.Reason : DisabledText, LineTone.Bad) };
        }
    }

    public static MultiActivateController? TryCreate(IReadOnlyList<ActivationOption>? options, Func<bool>? isValid,
                                                     Func<bool>? isAimed, Action<int>? chosen)
    {
        if (options is null || isValid is null || isAimed is null || chosen is null) return null;
        var kept = Clean(options);
        if (kept is null || !isValid()) return null;
        return new MultiActivateController(kept, isValid, isAimed, chosen);
    }

    public override bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        switch (action)
        {
            case MenuInputAction.Down:
                return ChooseSlot(0);
            case MenuInputAction.Right:
                return ChooseSlot(1);
            case MenuInputAction.Left:
                return ChooseSlot(2);
            case MenuInputAction.Up:
                return ChooseSlot(3);
            case MenuInputAction.Accept:
                if (Rows.Selected is { } row) Choose(row.Index);
                return true;
            case MenuInputAction.Back:
                Finish(false);
                return true;
            default:
                return false;
        }
    }

    public override void Tick(double deltaSeconds)
    {
        base.Tick(deltaSeconds);
        if (IsFinished) return;
        if (!_isValid())
        {
            Finish(false);
            return;
        }
        if (_isAimed())
        {
            _awayFor = 0;
            return;
        }
        if (double.IsFinite(deltaSeconds) && deltaSeconds > 0) _awayFor += deltaSeconds;
        if (_awayFor >= LookAwaySeconds) Finish(false);
    }

    public bool SetOptions(IReadOnlyList<ActivationOption>? options)
    {
        if (IsFinished) return false;
        var kept = options is null ? null : Clean(options);
        if (kept is null)
        {
            Finish(false);
            return false;
        }
        var focus = Rows.SelectedIndex;
        _options = kept;
        Rebuild(null);
        if (focus >= 0 && focus < Rows.RowCount && _options[focus].Enabled) Rows.Select(focus);
        return true;
    }

    private static IReadOnlyList<ActivationOption>? Clean(IReadOnlyList<ActivationOption> options)
    {
        var kept = options.Where(option => option is not null && !string.IsNullOrWhiteSpace(option.Text)).Take(MaxOptions).ToList();
        return kept.Count < MinOptions ? null : kept;
    }

    private static string TextOf(ActivateRow row) => SlotNames[row.Slot] + ": " + row.Text;

    private static int[] SlotsFor(IReadOnlyList<ActivationOption> options)
    {
        var wanted = options.Select(option => option.Slot).ToList();
        var valid = wanted.All(slot => slot is >= 0 and < MaxOptions) && wanted.Distinct().Count() == wanted.Count;
        return valid ? wanted.Select(slot => slot!.Value).ToArray() : Enumerable.Range(0, options.Count).ToArray();
    }

    private void Rebuild(string? selectKey)
    {
        var slots = SlotsFor(_options);
        var rows = _options.Select((option, index) => new ActivateRow(index, slots[index], option.Text, option.Enabled, option.DisabledReason))
            .ToList();
        Rows.ShowRows(rows, selectKey);
        var first = rows.FindIndex(row => row.Enabled);
        if (first >= 0) Rows.Select(first);
    }

    private bool ChooseSlot(int slot)
    {
        var row = Rows.Rows.FirstOrDefault(candidate => candidate.Slot == slot);
        if (row is not null) Choose(row.Index);
        return true;
    }

    private void Choose(int index)
    {
        if (IsFinished || index < 0 || index >= _options.Count) return;
        var option = _options[index];
        if (!option.Enabled)
        {
            Say(option.DisabledReason.Length > 0 ? option.DisabledReason : DisabledText);
            Sound("menu.blocked");
            return;
        }
        var chosen = _chosen;
        _chosen = null;
        Sound("menu.confirm");
        Finish(true);
        chosen?.Invoke(index);
    }
}
