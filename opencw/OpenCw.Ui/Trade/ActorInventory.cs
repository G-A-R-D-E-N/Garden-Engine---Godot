using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Trade;

public sealed record PickpocketContext(
    PickpocketSettings Settings,
    float ThiefAgility,
    float TargetPerception,
    IItemRecords Items,
    PickpocketTarget Target,
    bool PlayerSneaking,
    Func<int>? Roll = null,
    Func<float, float>? Perk = null,
    CrimeLedger? Crimes = null);

// Constraint: DeathStacks are what a kill adds to the carried stacks (worn outfit pieces and the death item), null for a living actor; Open adds them to the ledger row once.
public sealed record ActorSetup(string Name, bool Alive, IReadOnlyList<InventoryStack> Stacks, bool Complete = true, IReadOnlyList<InventoryStack>? DeathStacks = null);

public interface IActorRecords
{
    ActorSetup? Read(string refKey, string baseKey);
}

public sealed class ActorInventory : IBarterLedger, IContainerInventory
{
    public const string NotEnoughText = "Not enough.";
    public const string MissingText = "That actor could not be read.";
    public const string PartialText = "That actor's items could not be fully read.";
    public const string NoStockText = "The vendor's stock could not be read.";
    public const string AliveText = "Not dead.";
    public const string CapsLineText = "Caps are not an item.";
    public const string NoInvestText = "Not available.";
    public const string ShortCapsText = "Not enough caps.";
    public const string VendorShortText = "The vendor is short of caps.";
    public const string PlaceText = "You cannot place items in a container while pickpocketing.";
    public const string CaughtText = "You've been caught pickpocketing.";
    public const string AlreadyCaughtSuffix = " has already caught you.";
    public const string NotOfferedText = "Not available.";

    private readonly string _name;
    private readonly bool _alive;
    private readonly PlayerItems _items;
    private readonly PlayerItems _player;
    private readonly string _capsId;
    private readonly Func<BarterSide, double> _priceFactor;
    private readonly PickpocketContext? _pick;
    private readonly ContainerLedger? _ledger;
    private readonly string _refKey;
    private readonly InventoryRevision _revision;

    private ActorInventory(string name, bool alive, PlayerItems items, PlayerItems player, string capsId, Func<BarterSide, double> priceFactor, double carryLimit,
        PickpocketContext? pick = null, ContainerLedger? ledger = null, string refKey = "")
    {
        _pick = pick;
        _ledger = ledger;
        _refKey = refKey;
        _name = name;
        _alive = alive;
        _items = items;
        _player = player;
        _revision = new InventoryRevision(player, items);
        _capsId = capsId;
        _priceFactor = priceFactor;
        CarryLimit = carryLimit;
    }

    public string VendorName => _name;

    public string Title => _name;

    public ContainerMode Mode => _pick is null ? ContainerMode.Loot : ContainerMode.Pickpocket;

    public bool WasCaught { get; private set; }

    public double CarryLimit { get; }

    public int Revision => _revision.Value;

    public int PlayerCaps => _player.CountOf(_capsId);

    public int VendorCaps => _items.CountOf(_capsId);

    public IReadOnlyList<InventoryStack> PlayerStacks => _player.Stacks;

    public IReadOnlyList<InventoryStack> ContainerStacks => _items.Stacks;

    IReadOnlyList<InventoryStack> IBarterLedger.PlayerStacks => _player.Stacks.Where(NotCaps).ToArray();

    IReadOnlyList<InventoryStack> IBarterLedger.VendorStacks => _items.Stacks.Where(NotCaps).ToArray();

    public bool CanInvest => false;

    public int InvestAmount => 0;

    public static ActorInventory? Open(
        string refKey, string baseKey, IActorRecords records, ContainerLedger ledger, PlayerItems player,
        string capsId, Func<BarterSide, double> priceFactor, out string refusal, double carryLimit = 0)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(priceFactor);
        refusal = string.Empty;
        if (records.Read(refKey, baseKey) is not { } setup)
        {
            refusal = MissingText;
            return null;
        }
        var held = ledger.Find(refKey);
        if (held is null)
        {
            if (!setup.Complete)
            {
                refusal = PartialText;
                return null;
            }
            held = ledger.Freeze(refKey, setup.Stacks);
        }
        if (setup is { Complete: true, DeathStacks: { } death } && !ledger.HasDeathLoot(refKey))
        {
            foreach (var stack in death) held.Add(stack.Id, stack.Count);
            ledger.MarkDeathLoot(refKey);
        }
        return new ActorInventory(setup.Name, setup.Alive, held, player, capsId, priceFactor, carryLimit);
    }

    // Requires: the base-record stacks of a living actor stand in for its carried items; equipped items are not offered.
    public static ActorInventory? OpenPickpocket(
        string refKey, string baseKey, IActorRecords records, ContainerLedger ledger, PlayerItems player,
        string capsId, Func<BarterSide, double> priceFactor, PickpocketContext pick, out string refusal, double carryLimit = 0)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(priceFactor);
        ArgumentNullException.ThrowIfNull(pick);
        refusal = string.Empty;
        var offer = PickpocketRules.Offer(pick.Target, pick.PlayerSneaking);
        if (offer == PickpocketOffer.NotOffered)
        {
            refusal = NotOfferedText;
            return null;
        }
        if (records.Read(refKey, baseKey) is not { } setup)
        {
            refusal = MissingText;
            return null;
        }
        if (offer == PickpocketOffer.AlreadyCaught)
        {
            refusal = setup.Name + AlreadyCaughtSuffix;
            return null;
        }
        var held = ledger.Find(refKey);
        if (held is null)
        {
            if (!setup.Complete)
            {
                refusal = PartialText;
                return null;
            }
            held = ledger.Freeze(refKey, setup.Stacks);
        }
        return new ActorInventory(setup.Name, true, held, player, capsId, priceFactor, carryLimit, pick, ledger, refKey);
    }

    // Constraint: stock and caps are the chest's, keyed by chestKey.
    public static ActorInventory? OpenVendor(
        string npcRefKey, string npcBaseKey, string chestKey, string chestBaseKey, IActorRecords actors, IContainerRecords chests,
        ContainerLedger ledger, PlayerItems player, string capsId, Func<BarterSide, double> priceFactor, out string refusal)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(chests);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(priceFactor);
        refusal = string.Empty;
        if (actors.Read(npcRefKey, npcBaseKey) is not { } actor)
        {
            refusal = MissingText;
            return null;
        }
        var held = ledger.Find(chestKey);
        if (held is null)
        {
            if (chests.Read(chestKey, chestBaseKey) is not { } chest)
            {
                refusal = NoStockText;
                return null;
            }
            held = ledger.Freeze(chestKey, chest.Stacks);
        }
        return new ActorInventory(actor.Name, true, held, player, capsId, priceFactor, 0);
    }

    public void Add(string id, int count)
    {
        if (count <= 0) return;
        var mark = _revision.Mark();
        _items.Add(id, count);
        _revision.Bump(mark);
    }

    public bool Remove(string id, int count)
    {
        var mark = _revision.Mark();
        if (!_items.Remove(id, count)) return false;
        _revision.Bump(mark);
        return true;
    }

    public double PriceFactor(BarterSide side) => _priceFactor(side);

    string IBarterLedger.Refusal(BarterSide side, string itemId) =>
        string.Equals(itemId, _capsId, StringComparison.OrdinalIgnoreCase) ? CapsLineText : string.Empty;

    public ActionResult Commit(IReadOnlyList<BarterLine> basket, int playerNet)
    {
        var need = new Dictionary<(BarterSide Side, string Id), int>(new LineKeyComparer());
        foreach (var line in basket)
        {
            if (line.Count <= 0) return new ActionResult(false, NotEnoughText);
            if (string.Equals(line.ItemId, _capsId, StringComparison.OrdinalIgnoreCase)) return new ActionResult(false, CapsLineText);
            var key = (line.Side, line.ItemId);
            need[key] = need.GetValueOrDefault(key) + line.Count;
        }
        var mark = _revision.Mark();
        foreach (var ((side, id), count) in need)
            if ((side == BarterSide.Player ? _player : _items).CountOf(id) < count)
                return new ActionResult(false, NotEnoughText);
        if (playerNet < 0 && PlayerCaps < -(long)playerNet) return new ActionResult(false, ShortCapsText);
        if (playerNet > 0 && VendorCaps < playerNet) return new ActionResult(false, VendorShortText);
        foreach (var ((side, id), count) in need)
        {
            var (source, target) = side == BarterSide.Player ? (_player, _items) : (_items, _player);
            source.MoveTo(target, id, count);
        }
        if (playerNet > 0) { _items.Remove(_capsId, playerNet); _player.Add(_capsId, playerNet); }
        else if (playerNet < 0) { _player.Remove(_capsId, -playerNet); _items.Add(_capsId, -playerNet); }
        _revision.Bump(mark);
        return new ActionResult(true);
    }

    public ActionResult Invest() => new(false, NoInvestText);

    string IContainerInventory.Refusal(ContainerSide from, string itemId, int count)
    {
        if (_pick is not null)
            return from == ContainerSide.Player ? PlaceText : count <= 0 || _items.CountOf(itemId) < count ? NotEnoughText : string.Empty;
        if (_alive) return AliveText;
        var source = from == ContainerSide.Container ? _items : _player;
        return count <= 0 || source.CountOf(itemId) < count ? NotEnoughText : string.Empty;
    }

    public int Chance(ContainerSide from, string itemId) => Chance(from, itemId, 1);

    public int Chance(ContainerSide from, string itemId, int count)
    {
        if (_pick is not { } pick || from == ContainerSide.Player) return 0;
        var item = pick.Items.Find(itemId);
        return PickpocketFormula.Chance(
            pick.Settings, pick.ThiefAgility, pick.TargetPerception,
            PickpocketFormula.StolenValue(item?.Value ?? 0, count), PickpocketFormula.StolenWeight(item?.Weight ?? 0, count),
            false, pick.Perk);
    }

    public ContainerTransferResult Transfer(ContainerSide from, string itemId, int count)
    {
        if (_pick is { } pick)
        {
            var refusal = ((IContainerInventory)this).Refusal(from, itemId, count);
            if (refusal.Length > 0) return new ContainerTransferResult(TransferStatus.Refused, refusal);
            if (!PickpocketRules.KnockedOut(pick.Target)
                && !PickpocketFormula.Succeeds(Chance(from, itemId, count), pick.Roll is { } roll ? roll() : DefaultRoll()))
            {
                WasCaught = true;
                _ledger?.MarkCaught(_refKey);
                if (_refKey.Length > 0) pick.Crimes?.Report(CrimeKind.Pickpocket, _refKey, itemId, count, _refKey, witnessed: true);
                return new ContainerTransferResult(TransferStatus.Caught, CaughtText);
            }
        }
        else if (_alive) return new ContainerTransferResult(TransferStatus.Refused, AliveText);
        var (source, target) = from == ContainerSide.Container ? (_items, _player) : (_player, _items);
        var mark = _revision.Mark();
        if (!source.MoveTo(target, itemId, count)) return new ContainerTransferResult(TransferStatus.Refused, NotEnoughText);
        _revision.Bump(mark);
        return new ContainerTransferResult(TransferStatus.Done);
    }

    private static int DefaultRoll() => Random.Shared.Next(0, PickpocketFormula.RollCount);

    private bool NotCaps(InventoryStack stack) => !string.Equals(stack.Id, _capsId, StringComparison.OrdinalIgnoreCase);

    private sealed class LineKeyComparer : IEqualityComparer<(BarterSide Side, string Id)>
    {
        public bool Equals((BarterSide Side, string Id) a, (BarterSide Side, string Id) b) =>
            a.Side == b.Side && string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((BarterSide Side, string Id) key) =>
            HashCode.Combine(key.Side, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Id));
    }
}
