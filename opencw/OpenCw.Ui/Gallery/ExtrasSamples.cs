using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;

namespace OpenCommonwealth.Services.Ui.Gallery;

public sealed class SampleContainer : IContainerRules
{
    public const string HeavyId = "item_d";
    public const string WatchedId = "item_b";

    private readonly List<ContainerStack> _container = new()
    {
        new ContainerStack("item_a", "Item A", 8, 0.5, 12),
        new ContainerStack(WatchedId, "Item B", 3, 2, 40),
        new ContainerStack("item_c", "Item C", 20, 0.1, 1, true),
        new ContainerStack(HeavyId, "Item D", 1, 30, 90),
    };

    private readonly List<ContainerStack> _player = new()
    {
        new ContainerStack("item_e", "Item E", 2, 1.5, 15),
        new ContainerStack("item_f", "Item F", 12, 0.2, 3),
    };

    private int _revision;

    public SampleContainer(ContainerMode mode = ContainerMode.Loot)
    {
        Mode = mode;
    }

    public ContainerMode Mode { get; }

    public string Title => Mode == ContainerMode.Pickpocket ? "Sample person" : "Sample container";

    public string PlayerListHeader => "YOUR ITEMS";

    public double CarryLimit => 40;

    public int Revision => _revision;

    public IReadOnlyList<ContainerStack> ContainerStacks => _container;

    public IReadOnlyList<ContainerStack> PlayerStacks => _player;

    public string Refusal(ContainerSide from, string itemId, int count) =>
        from == ContainerSide.Container && itemId == HeavyId ? "That is too heavy to lift." : string.Empty;

    public int Chance(ContainerSide from, string itemId) => itemId == WatchedId ? 35 : 80;

    public ContainerTransferResult Transfer(ContainerSide from, string itemId, int count)
    {
        if (Mode == ContainerMode.Pickpocket && itemId == WatchedId)
            return new ContainerTransferResult(TransferStatus.Caught);
        var source = from == ContainerSide.Container ? _container : _player;
        var target = from == ContainerSide.Container ? _player : _container;
        var index = source.FindIndex(stack => stack.Id == itemId);
        if (index < 0 || source[index].Count < count) return new ContainerTransferResult(TransferStatus.Refused, "That is gone.");
        var stack = source[index];
        if (stack.Count == count) source.RemoveAt(index);
        else source[index] = stack with { Count = stack.Count - count };
        var existing = target.FindIndex(candidate => candidate.Id == itemId);
        if (existing < 0) target.Add(stack with { Count = count });
        else target[existing] = target[existing] with { Count = target[existing].Count + count };
        _revision++;
        return new ContainerTransferResult(TransferStatus.Done);
    }
}

public sealed class SampleBenchRename : IWorkbenchRename
{
    private readonly Dictionary<string, string> _names = new() { ["target_a"] = "Item X" };

    public int MaxNameLength => 24;

    public bool CanRename(string targetId) => _names.ContainsKey(targetId);

    public string CurrentName(string targetId) => _names.TryGetValue(targetId, out var name) ? name : string.Empty;

    public string? Validate(string name) => name.Any(char.IsControl) ? "Names cannot hold control characters." : null;

    public ActionResult Rename(string targetId, string name)
    {
        if (!_names.ContainsKey(targetId)) return new ActionResult(false, "That item is gone.");
        _names[targetId] = name;
        return new ActionResult(true);
    }
}
