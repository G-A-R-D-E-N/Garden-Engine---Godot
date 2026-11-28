using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth;

// Requires: main thread; a reference with no ledger row is frozen from baseStacks, and has no inventory when that answers null.
public sealed class LedgerInventoryAccess(ContainerLedger ledger, PlayerItems player, Func<string, bool> isPlayer, Func<string, IReadOnlyList<InventoryStack>?>? baseStacks = null) : IReferenceInventoryAccess
{
    private PlayerItems? Of(string refKey) =>
        isPlayer(refKey) ? player : ledger.Find(refKey) ?? (baseStacks?.Invoke(refKey) is { } stacks ? ledger.Freeze(refKey, stacks) : null);

    public bool TryAdd(string refKey, string itemKey, int count)
    {
        if (count <= 0 || Of(refKey) is not { } items) return false;
        items.Add(itemKey, count);
        return true;
    }

    public int Count(string refKey, string? itemKey) =>
        Of(refKey) is not { } items ? 0 : itemKey is null ? items.Entries.Sum(entry => entry.Count) : items.CountOf(itemKey);

    public int Move(string refKey, string? toRefKey, string? itemKey, int count)
    {
        if (Of(refKey) is not { } source) return 0;
        PlayerItems? target = null;
        if (toRefKey is not null && ((target = Of(toRefKey)) is null || ReferenceEquals(target, source))) return 0;
        if (itemKey is not null)
        {
            var moved = Math.Min(count, source.CountOf(itemKey));
            return moved > 0 && source.MoveTo(target, itemKey, moved) ? moved : 0;
        }
        var all = source.Entries.ToArray();
        foreach (var entry in all) target?.Add(entry.Id, entry.State, entry.Count);
        source.Clear();
        return all.Sum(entry => entry.Count);
    }
}
