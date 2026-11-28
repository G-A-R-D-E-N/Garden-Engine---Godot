using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Trade;

public sealed class RecordContainerRules : IContainerRules
{
    public const string MissingHeader = "";

    private readonly IContainerInventory _inventory;
    private readonly IItemRecords _items;
    private readonly Func<string?>? _playerListHeader;

    public RecordContainerRules(IContainerInventory inventory, IItemRecords items, Func<string?>? playerListHeader = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(items);
        _inventory = inventory;
        _items = items;
        _playerListHeader = playerListHeader;
    }

    public ContainerMode Mode => _inventory.Mode;

    public string Title => _inventory.Title;

    public string PlayerListHeader => _playerListHeader?.Invoke() is { Length: > 0 } header ? header : MissingHeader;

    public double CarryLimit => _inventory.CarryLimit;

    public int Revision => _inventory.Revision;

    public IReadOnlyList<ContainerStack> ContainerStacks => _inventory.ContainerStacks.Select(Describe).ToArray();

    public IReadOnlyList<ContainerStack> PlayerStacks => _inventory.PlayerStacks.Select(Describe).ToArray();

    public string Refusal(ContainerSide from, string itemId, int count) => _inventory.Refusal(from, itemId, count);

    public int Chance(ContainerSide from, string itemId) => _inventory.Chance(from, itemId);

    public int Chance(ContainerSide from, string itemId, int count) => _inventory.Chance(from, itemId, count);

    public ContainerTransferResult Transfer(ContainerSide from, string itemId, int count) => _inventory.Transfer(from, itemId, count);

    private ContainerStack Describe(InventoryStack stack) => _items.Find(stack.Id) is { } item
        ? new ContainerStack(stack.Id, item.Name, stack.Count, item.Weight ?? 0, item.Value ?? 0)
        : new ContainerStack(stack.Id, stack.Id, stack.Count);
}
