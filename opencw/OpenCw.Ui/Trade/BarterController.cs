using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Trade;

public sealed record BarterRow(BarterSide Side, string Id, string Name, int Available, int Staged, int Price);

public sealed class BarterController : ListDetailController<BarterRow>
{
    public const string TradedText = "Traded.";
    public const string ChangedText = "Inventory changed.";
    public const string CheckAgainText = "Check the basket again.";
    public const string NothingStagedText = "Nothing staged.";
    public const string NothingLeftText = "Nothing left to stage.";
    public const string NotAvailableText = "Not available.";
    public const string FailedText = "That didn't work.";
    public const string NoPrice = "--";

    private enum Confirming
    {
        None,
        Discard,
        Invest,
    }

    private readonly IBarterRules _rules;
    private readonly Dictionary<(BarterSide Side, string Id), int> _basket = new();
    private BarterSide _side = BarterSide.Player;
    private Confirming _confirming;
    private BarterQuote? _quote;
    private int _revision;
    private bool _needsAck;
    private bool _committing;

    public BarterController(IBarterRules rules)
        : base(new PlainListModel<BarterRow>(row => row.Id, TextOf))
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
        _revision = rules.Revision;
        Rebuild(null);
        Rows.Accepted += _ => OnRowAccepted();
    }

    public bool IsConfirming => _confirming != Confirming.None;

    public BarterQuote? Quote => _quote;

    public IReadOnlyList<BarterLine> Basket => BasketLines();

    public override string Title => _rules.VendorName;

    public override string Header =>
        "Your caps " + _rules.PlayerCaps.ToString(CultureInfo.InvariantCulture)
        + " | Vendor caps " + _rules.VendorCaps.ToString(CultureInfo.InvariantCulture);

    public override IReadOnlyList<MenuHint> Hints
    {
        get
        {
            if (IsConfirming)
                return new[] { new MenuHint(MenuInputAction.Accept, "Confirm"), new MenuHint(MenuInputAction.Back, "Cancel") };
            var hints = new List<MenuHint>
            {
                new(MenuInputAction.Accept, _side == BarterSide.Player ? "Sell" : "Buy"),
                new(MenuInputAction.TabPrev, "Take back"),
                new(MenuInputAction.Compare, "Trade"),
                new(MenuInputAction.Favourite, "Reset"),
                new(MenuInputAction.Search, "Invest"),
                new(MenuInputAction.Back, "Exit"),
            };
            return hints;
        }
    }

    public override IReadOnlyList<MenuTextLine> DetailLines
    {
        get
        {
            if (_confirming == Confirming.Discard)
                return new[] { new MenuTextLine("Drop the staged items and leave?", LineTone.Heading) };
            if (_confirming == Confirming.Invest)
                return new[]
                {
                    new MenuTextLine("Invest " + _rules.InvestAmount.ToString(CultureInfo.InvariantCulture) + " caps in this vendor?",
                        LineTone.Heading),
                };
            var lines = new List<MenuTextLine>
            {
                new(ListHeader, LineTone.Heading),
            };
            if (Rows.Selected is { } row)
                lines.Add(new MenuTextLine(row.Name + ": " + PriceText(row.Price) + " each"));
            if (_basket.Count == 0) return lines;
            lines.Add(new MenuTextLine("Basket", LineTone.Heading));
            foreach (var line in BasketLines())
                lines.Add(new MenuTextLine((line.Side == BarterSide.Player ? "Sell " : "Buy ") + NameOf(line) + " x "
                    + line.Count.ToString(CultureInfo.InvariantCulture)));
            if (_quote is { } quote)
            {
                lines.Add(quote.PlayerNet >= 0
                    ? new MenuTextLine("You receive " + quote.PlayerNet.ToString(CultureInfo.InvariantCulture) + " caps")
                    : new MenuTextLine("You pay " + (-(long)quote.PlayerNet).ToString(CultureInfo.InvariantCulture) + " caps"));
                if (!quote.CanCommit)
                    lines.Add(new MenuTextLine(quote.Refusal.Length > 0 ? quote.Refusal : NotAvailableText, LineTone.Bad));
            }
            return lines;
        }
    }

    public static string PriceText(int price) => price < 0 ? NoPrice : price.ToString(CultureInfo.InvariantCulture) + " caps";

    public override bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        if (_committing) return true;
        var fresh = _needsAck;
        _needsAck = false;
        if (IsConfirming) return HandleConfirm(action);
        switch (action)
        {
            case MenuInputAction.Left:
                return SwitchSide(BarterSide.Player);
            case MenuInputAction.Right:
                return SwitchSide(BarterSide.Vendor);
            case MenuInputAction.Accept:
                Stage();
                return true;
            case MenuInputAction.TabPrev:
                Unstage();
                return true;
            case MenuInputAction.Compare:
                Commit(fresh);
                return true;
            case MenuInputAction.Favourite:
                Reset();
                return true;
            case MenuInputAction.Search:
                AskInvest();
                return true;
            case MenuInputAction.Back:
                if (_basket.Count == 0) Finish(false);
                else Ask(Confirming.Discard);
                return true;
            default:
                return Rows.Handle(action);
        }
    }

    public override void Tick(double deltaSeconds)
    {
        base.Tick(deltaSeconds);
        if (IsFinished || _committing || _rules.Revision == _revision) return;
        _revision = _rules.Revision;
        var pruned = Prune();
        Rebuild(Rows.Selected?.Id);
        if (!pruned) return;
        _needsAck = true;
        Say(ChangedText);
    }

    private static string TextOf(BarterRow row)
    {
        var text = row.Name + " x " + row.Available.ToString(CultureInfo.InvariantCulture);
        if (row.Staged > 0) text += " (" + row.Staged.ToString(CultureInfo.InvariantCulture) + " staged)";
        return text + " - " + PriceText(row.Price);
    }

    private IReadOnlyList<BarterStack> StacksOf(BarterSide side) =>
        side == BarterSide.Player ? _rules.PlayerStacks : _rules.VendorStacks;

    private string NameOf(BarterLine line) =>
        StacksOf(line.Side).FirstOrDefault(stack => stack.Id == line.ItemId)?.Name ?? line.ItemId;

    private IReadOnlyList<BarterLine> BasketLines() =>
        _basket.OrderBy(pair => pair.Key.Side).ThenBy(pair => pair.Key.Id, StringComparer.Ordinal)
            .Select(pair => new BarterLine(pair.Key.Side, pair.Key.Id, pair.Value)).ToList();

    private string ListHeader => _side == BarterSide.Player ? _rules.PlayerListHeader : _rules.VendorListHeader;

    private int StagedOf(BarterSide side, string id) => _basket.TryGetValue((side, id), out var count) ? count : 0;

    private void Rebuild(string? selectKey)
    {
        var rows = StacksOf(_side)
            .Where(stack => !string.IsNullOrWhiteSpace(stack.Id) && stack.Count > 0)
            .Select(stack => new BarterRow(_side, stack.Id, stack.Name, Math.Max(0, stack.Count - StagedOf(_side, stack.Id)),
                StagedOf(_side, stack.Id), _rules.UnitPrice(_side, stack.Id)))
            .ToList();
        Rows.ShowRows(rows, selectKey);
        Rows.Summary = ListHeader;
        _quote = _basket.Count == 0 ? null : _rules.Quote(BasketLines());
    }

    private bool Prune()
    {
        var changed = false;
        foreach (var pair in _basket.ToList())
        {
            var stack = StacksOf(pair.Key.Side).FirstOrDefault(s => s.Id == pair.Key.Id);
            if (stack is null || stack.Count <= 0)
            {
                _basket.Remove(pair.Key);
                changed = true;
            }
            else if (pair.Value > stack.Count)
            {
                _basket[pair.Key] = stack.Count;
                changed = true;
            }
        }
        return changed;
    }

    private bool SwitchSide(BarterSide side)
    {
        if (_side == side) return true;
        _side = side;
        Sound("menu.tab");
        Rebuild(null);
        return true;
    }

    private void OnRowAccepted()
    {
        if (!IsFinished && !IsConfirming && !_committing) Stage();
    }

    private void Blocked(string text)
    {
        Say(text);
        Sound("menu.blocked");
    }

    private void Stage()
    {
        if (Rows.Selected is not { } row) return;
        var refusal = _rules.Refusal(row.Side, row.Id);
        if (refusal.Length > 0)
        {
            Blocked(refusal);
            return;
        }
        if (row.Available <= 0)
        {
            Blocked(NothingLeftText);
            return;
        }
        _basket[(row.Side, row.Id)] = row.Staged + 1;
        Sound("menu.forward");
        Rebuild(row.Id);
    }

    private void Unstage()
    {
        if (Rows.Selected is not { } row) return;
        if (row.Staged <= 0)
        {
            Blocked(NothingStagedText);
            return;
        }
        if (row.Staged == 1) _basket.Remove((row.Side, row.Id));
        else _basket[(row.Side, row.Id)] = row.Staged - 1;
        Sound("menu.back");
        Rebuild(row.Id);
    }

    private void Reset()
    {
        if (_basket.Count == 0)
        {
            Blocked(NothingStagedText);
            return;
        }
        _basket.Clear();
        Sound("menu.back");
        Rebuild(Rows.Selected?.Id);
    }

    private void Commit(bool fresh)
    {
        if (_basket.Count == 0)
        {
            Blocked(NothingStagedText);
            return;
        }
        if (fresh)
        {
            Blocked(CheckAgainText);
            return;
        }
        if (_rules.Revision != _revision)
        {
            _revision = _rules.Revision;
            Prune();
            Rebuild(Rows.Selected?.Id);
            _needsAck = true;
            Blocked(ChangedText);
            return;
        }
        var quote = _rules.Quote(BasketLines());
        _quote = quote;
        if (!quote.CanCommit)
        {
            Blocked(quote.Refusal.Length > 0 ? quote.Refusal : NotAvailableText);
            return;
        }
        _committing = true;
        ActionResult result;
        try
        {
            result = _rules.Commit(BasketLines());
        }
        finally
        {
            _committing = false;
        }
        if (IsFinished) return;
        if (!result.Ok)
        {
            Blocked(result.Reason.Length > 0 ? result.Reason : FailedText);
            return;
        }
        _basket.Clear();
        _revision = _rules.Revision;
        Rebuild(Rows.Selected?.Id);
        Say(TradedText);
        Sound("trade.commit");
    }

    private void AskInvest()
    {
        if (!_rules.CanInvest || _basket.Count > 0)
        {
            Blocked(NotAvailableText);
            return;
        }
        Ask(Confirming.Invest);
    }

    private void Ask(Confirming kind)
    {
        _confirming = kind;
        Raise();
    }

    private bool HandleConfirm(MenuInputAction action)
    {
        if (action == MenuInputAction.Back)
        {
            _confirming = Confirming.None;
            Raise();
        }
        else if (action == MenuInputAction.Accept)
        {
            var kind = _confirming;
            _confirming = Confirming.None;
            if (kind == Confirming.Discard)
            {
                _basket.Clear();
                Finish(false);
                return true;
            }
            var result = _rules.Invest();
            _revision = _rules.Revision;
            Rebuild(Rows.Selected?.Id);
            if (result.Ok) Say("Invested.");
            else Blocked(result.Reason.Length > 0 ? result.Reason : FailedText);
        }
        return true;
    }
}
