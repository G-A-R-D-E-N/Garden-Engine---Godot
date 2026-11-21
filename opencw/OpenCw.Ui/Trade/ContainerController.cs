using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Trade;

public sealed record ContainerRow(ContainerSide Side, string Id, string Name, int Count);

public sealed class ContainerController : ListDetailController<ContainerRow>
{
    public const int WholeStackLimit = 5;
    public const int QuantityJump = 5;
    public const string EmptyText = ContainerText.Empty;
    public const string TheftText = ContainerText.Theft;
    public const string OverText = ContainerText.OverEncumbered;
    public const string ChangedText = ContainerText.Changed;
    public const string UnavailableText = ContainerText.Unavailable;
    public const string FailedText = ContainerText.Failed;

    private readonly IContainerRules _rules;
    private ContainerSide _side = ContainerSide.Container;
    private int _revision;
    private bool _busy;
    private bool _picking;
    private ContainerSide _pickSide;
    private string _pickId = string.Empty;
    private int _quantity = 1;

    public ContainerController(IContainerRules rules)
        : base(new PlainListModel<ContainerRow>(row => row.Id, TextOf))
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
        _revision = rules.Revision;
        Refresh(true);
        Rows.Accepted += _ => OnRowAccepted();
    }

    public event Action? TookAll;

    public ContainerOutcome Outcome { get; private set; }

    public double PlayerWeight => _rules.PlayerStacks.Sum(stack => stack.Weight * Math.Max(0, stack.Count));

    public bool IsOverEncumbered => _rules.CarryLimit > 0 && PlayerWeight > _rules.CarryLimit;

    public override string Title => _rules.Title;

    public override string Header => ContainerText.Header(_rules.Mode, PlayerWeight, _rules.CarryLimit);

    public string Verb => ContainerText.Verb(_rules.Mode, _side == ContainerSide.Container);

    public string TakeAllLabel => ContainerText.TakeAllLabel(_rules.Mode);

    public override IReadOnlyList<MenuHint> Hints
    {
        get
        {
            if (_picking)
                return new[] { new MenuHint(MenuInputAction.Accept, Verb), new MenuHint(MenuInputAction.Back, ContainerText.Cancel) };
            var hints = new List<MenuHint>();
            if (Rows.RowCount > 0) hints.Add(new MenuHint(MenuInputAction.Accept, Verb));
            if (_side == ContainerSide.Container && Rows.RowCount > 0) hints.Add(new MenuHint(MenuInputAction.Favourite, TakeAllLabel));
            hints.Add(new MenuHint(MenuInputAction.Back, ContainerText.Close));
            return hints;
        }
    }

    public override IReadOnlyList<MenuTextLine> DetailLines
    {
        get
        {
            var lines = new List<MenuTextLine>
            {
                new(_side == ContainerSide.Container ? ModeWord(_rules.Mode) : _rules.PlayerListHeader, LineTone.Heading),
            };
            if (_rules.Mode == ContainerMode.Steal && _side == ContainerSide.Container)
                lines.Add(new MenuTextLine(TheftText, LineTone.Bad));
            var side = _picking ? _pickSide : Rows.Selected?.Side;
            var id = _picking ? _pickId : Rows.Selected?.Id;
            if (side is not { } shownSide || id is null || Find(shownSide, id) is not { } stack)
                lines.Add(new MenuTextLine(EmptyText, LineTone.Muted));
            else
                AddCard(lines, shownSide, stack);
            if (IsOverEncumbered) lines.Add(new MenuTextLine(OverText, LineTone.Bad));
            return lines;
        }
    }

    public static string ModeWord(ContainerMode mode) => ContainerText.Mode(mode);

    public override bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        if (_busy) return true;
        if (_picking) return HandlePicker(action);
        switch (action)
        {
            case MenuInputAction.Left:
                return SwitchSide(ContainerSide.Container);
            case MenuInputAction.Right:
                return SwitchSide(ContainerSide.Player);
            case MenuInputAction.Accept:
                Transfer();
                return true;
            case MenuInputAction.Favourite:
                TakeAll();
                return true;
            case MenuInputAction.Back:
                Outcome = ContainerOutcome.Closed;
                Finish(false);
                return true;
            default:
                return Rows.Handle(action);
        }
    }

    public override void Tick(double deltaSeconds)
    {
        base.Tick(deltaSeconds);
        if (IsFinished || _busy || _rules.Revision == _revision) return;
        _revision = _rules.Revision;
        if (_picking && Find(_pickSide, _pickId) is null)
        {
            _picking = false;
            Say(ChangedText);
        }
        Refresh(false);
        FocusOtherIfEmpty();
    }

    private static string TextOf(ContainerRow row) =>
        row.Count > 1 ? row.Name + " x " + CountText.Short(row.Count) : row.Name;

    private IReadOnlyList<ContainerStack> StacksOf(ContainerSide side) =>
        side == ContainerSide.Container ? _rules.ContainerStacks : _rules.PlayerStacks;

    private ContainerStack? Find(ContainerSide side, string id) =>
        StacksOf(side).FirstOrDefault(stack => stack.Id == id && stack.Count > 0);

    private int ClampQuantity() => Find(_pickSide, _pickId) is { } stack ? Math.Clamp(_quantity, 1, stack.Count) : 1;

    private void Refresh(bool reset)
    {
        var rows = StacksOf(_side)
            .Where(stack => !string.IsNullOrWhiteSpace(stack.Id) && stack.Count > 0)
            .Select(stack => new ContainerRow(_side, stack.Id, stack.Name, stack.Count))
            .ToList();
        if (reset) Rows.ReplaceRows(rows);
        else Rows.SetRows(rows);
        Rows.Summary = _side == ContainerSide.Container ? _rules.Title : _rules.PlayerListHeader;
    }

    private void FocusOtherIfEmpty()
    {
        if (Rows.RowCount > 0) return;
        var other = _side == ContainerSide.Container ? ContainerSide.Player : ContainerSide.Container;
        if (!StacksOf(other).Any(stack => stack.Count > 0 && !string.IsNullOrWhiteSpace(stack.Id))) return;
        _side = other;
        Refresh(true);
    }

    private bool SwitchSide(ContainerSide side)
    {
        if (_side == side) return true;
        _side = side;
        Sound("menu.tab");
        Refresh(true);
        return true;
    }

    private void OnRowAccepted()
    {
        if (!IsFinished && !_picking && !_busy) Transfer();
    }

    private void Blocked(string text)
    {
        Say(text);
        Sound("menu.blocked");
    }

    private void Stale()
    {
        Refresh(false);
        FocusOtherIfEmpty();
        Blocked(ChangedText);
    }

    private void Transfer()
    {
        if (Rows.Selected is not { } row)
        {
            Sound("menu.blocked");
            return;
        }
        if (Find(row.Side, row.Id) is not { } stack)
        {
            Stale();
            return;
        }
        if (stack.MoveWholeStack)
            AfterMove(Move(row.Side, stack.Id, stack.Count));
        else if (stack.Count <= WholeStackLimit)
            AfterMove(Move(row.Side, stack.Id, 1));
        else
            BeginPick(row.Side, stack.Id);
    }

    private void AfterMove(bool moved)
    {
        if (!moved || IsFinished) return;
        _revision = _rules.Revision;
        Refresh(false);
        FocusOtherIfEmpty();
        Sound("menu.confirm");
    }

    private bool Move(ContainerSide side, string id, int count)
    {
        var refusal = Refusal(side, id, count);
        if (refusal.Length > 0)
        {
            Blocked(refusal);
            return false;
        }
        var result = Call(side, id, count);
        switch (result.Status)
        {
            case TransferStatus.Done:
                return true;
            case TransferStatus.Caught:
                Caught();
                return false;
            default:
                Blocked(result.Reason.Length > 0 ? result.Reason : FailedText);
                return false;
        }
    }

    private void Caught()
    {
        Outcome = ContainerOutcome.Caught;
        Finish(false);
    }

    private string Refusal(ContainerSide side, string id, int count)
    {
        try
        {
            return _rules.Refusal(side, id, count) ?? string.Empty;
        }
        catch (Exception)
        {
            return UnavailableText;
        }
    }

    private ContainerTransferResult Call(ContainerSide side, string id, int count)
    {
        _busy = true;
        try
        {
            return _rules.Transfer(side, id, count) ?? new ContainerTransferResult(TransferStatus.Refused, UnavailableText);
        }
        catch (Exception)
        {
            return new ContainerTransferResult(TransferStatus.Refused, UnavailableText);
        }
        finally
        {
            _busy = false;
        }
    }

    private void TakeAll()
    {
        if (_side != ContainerSide.Container || Rows.RowCount == 0)
        {
            Sound("menu.blocked");
            return;
        }
        var first = string.Empty;
        var moved = false;
        foreach (var stack in _rules.ContainerStacks.Where(s => s.Count > 0 && !string.IsNullOrWhiteSpace(s.Id)).ToList())
        {
            var refusal = Refusal(ContainerSide.Container, stack.Id, stack.Count);
            if (refusal.Length > 0)
            {
                if (first.Length == 0) first = refusal;
                continue;
            }
            var result = Call(ContainerSide.Container, stack.Id, stack.Count);
            if (result.Status == TransferStatus.Caught)
            {
                Caught();
                return;
            }
            if (result.Status == TransferStatus.Refused)
            {
                if (first.Length == 0) first = result.Reason.Length > 0 ? result.Reason : FailedText;
                continue;
            }
            moved = true;
        }
        _revision = _rules.Revision;
        Refresh(false);
        FocusOtherIfEmpty();
        if (first.Length > 0) Blocked(first);
        else if (moved)
        {
            Sound("menu.confirm");
            TookAll?.Invoke();
        }
    }

    private void BeginPick(ContainerSide side, string id)
    {
        _pickSide = side;
        _pickId = id;
        _quantity = 1;
        _picking = true;
        Sound("menu.forward");
        Raise();
    }

    private bool HandlePicker(MenuInputAction action)
    {
        if (Find(_pickSide, _pickId) is not { } stack)
        {
            _picking = false;
            Stale();
            return true;
        }
        var current = ClampQuantity();
        switch (action)
        {
            case MenuInputAction.Up or MenuInputAction.Right:
                SetQuantity(current + 1, stack.Count);
                break;
            case MenuInputAction.Down or MenuInputAction.Left:
                SetQuantity(current - 1, stack.Count);
                break;
            case MenuInputAction.PageUp:
                SetQuantity(current + QuantityJump, stack.Count);
                break;
            case MenuInputAction.PageDown:
                SetQuantity(current - QuantityJump, stack.Count);
                break;
            case MenuInputAction.Accept:
                _picking = false;
                AfterMove(Move(_pickSide, stack.Id, current));
                Raise();
                break;
            case MenuInputAction.Back:
                _picking = false;
                Sound("menu.back");
                Raise();
                break;
        }
        return true;
    }

    private void SetQuantity(int value, int max)
    {
        _quantity = Math.Clamp(value, 1, max);
        Raise();
    }

    private int Chance(ContainerSide side, string id, int count)
    {
        try
        {
            var chance = _rules.Chance(side, id, count);
            return chance < 0 ? -1 : Math.Min(chance, 100);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private void AddCard(List<MenuTextLine> lines, ContainerSide side, ContainerStack stack)
    {
        lines.Add(new MenuTextLine(stack.Name, LineTone.Heading));
        lines.Add(new MenuTextLine(ContainerText.Count(stack.Count, stack.Weight, stack.Value)));
        var perWeight = stack.Weight > 0 ? stack.Value / stack.Weight : stack.Value;
        lines.Add(new MenuTextLine(ContainerText.ValuePerWeight(perWeight)));
        var chance = _rules.Mode == ContainerMode.Pickpocket ? Chance(side, stack.Id, _picking ? ClampQuantity() : 1) : -1;
        if (chance >= 0)
            lines.Add(new MenuTextLine(ContainerText.Chance(chance, side == ContainerSide.Container), LineTone.Bad));
        if (!_picking) return;
        var count = ClampQuantity();
        lines.Add(new MenuTextLine(ContainerText.Quantity(count, stack.Count), LineTone.Heading));
        lines.Add(new MenuTextLine(ContainerText.Total(stack.Weight, stack.Value, count)));
    }
}
