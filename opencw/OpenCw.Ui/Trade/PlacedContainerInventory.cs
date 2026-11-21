using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Trade;

public sealed record ContainerSetup(string Title, bool Owned, bool Locked, IReadOnlyList<InventoryStack> Stacks, string? Owner = null);

public interface IContainerRecords
{
    ContainerSetup? Read(string refKey, string baseKey);
}

public sealed class PlacedContainerInventory : IContainerInventory
{
    public const string LockedText = "Locked";
    public const string MissingText = "That container could not be read.";
    public const string NotEnoughText = "Not enough.";

    private readonly string _title;
    private readonly bool _owned;
    private readonly PlayerItems _contents;
    private readonly PlayerItems _player;
    private readonly string _refKey;
    private readonly string? _owner;
    private readonly CrimeLedger? _crimes;

    private PlacedContainerInventory(string title, bool owned, PlayerItems contents, PlayerItems player, double carryLimit, string refKey, string? owner, CrimeLedger? crimes)
    {
        _title = title;
        _owned = owned;
        _refKey = refKey;
        _owner = owner;
        _crimes = crimes;
        _contents = contents;
        _player = player;
        CarryLimit = carryLimit;
    }

    public ContainerMode Mode => _owned ? ContainerMode.Steal : ContainerMode.Loot;

    public string Title => _title;

    public double CarryLimit { get; }

    public int Revision { get; private set; }

    public IReadOnlyList<InventoryStack> ContainerStacks => _contents.Stacks;

    public IReadOnlyList<InventoryStack> PlayerStacks => _player.Stacks;

    public static PlacedContainerInventory? Open(
        string refKey, string baseKey, IContainerRecords records, ContainerLedger ledger, PlayerItems player,
        out string refusal, double carryLimit = 0, CrimeLedger? crimes = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(player);
        refusal = string.Empty;
        var held = ledger.Find(refKey);
        var setup = records.Read(refKey, baseKey);
        if (setup is null)
        {
            refusal = MissingText;
            return null;
        }
        if (setup.Locked && !ledger.IsUnlocked(refKey))
        {
            refusal = LockedText;
            return null;
        }
        return new PlacedContainerInventory(setup.Title, setup.Owned, held ?? ledger.Freeze(refKey, setup.Stacks), player, carryLimit, refKey, setup.Owner, crimes);
    }

    public string Refusal(ContainerSide from, string itemId, int count)
    {
        var source = from == ContainerSide.Container ? _contents : _player;
        return count <= 0 || source.CountOf(itemId) < count ? NotEnoughText : string.Empty;
    }

    public int Chance(ContainerSide from, string itemId) => 0;

    public ContainerTransferResult Transfer(ContainerSide from, string itemId, int count)
    {
        var (source, target) = from == ContainerSide.Container ? (_contents, _player) : (_player, _contents);
        var moved = new List<(ItemState State, int Count)>();
        var taking = from == ContainerSide.Container;
        Func<ItemState, ItemState>? map = null;
        Func<ItemState, int>? order = null;
        if (taking)
        {
            map = state => state.Deposited ? state.WithDeposited(false) : _owned && !string.IsNullOrWhiteSpace(_owner) ? state.WithStolenFrom(_owner) : state;
            order = state => state.Deposited ? 0 : state.IsPlain ? 1 : 2;
        }
        else if (_owned) map = state => state.WithDeposited(true);
        if (!source.MoveTo(target, itemId, count, map, moved, order)) return new ContainerTransferResult(TransferStatus.Refused, NotEnoughText);
        var stolen = moved.Where(step => !step.State.Deposited).Sum(step => step.Count);
        if (_owned && taking && stolen > 0 && !string.IsNullOrWhiteSpace(_owner) && !string.IsNullOrWhiteSpace(itemId)) _crimes?.Report(CrimeKind.Theft, _owner, itemId, stolen, _refKey, witnessed: false);
        Revision++;
        return new ContainerTransferResult(TransferStatus.Done);
    }
}
