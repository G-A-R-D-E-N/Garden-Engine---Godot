using System;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Trade;

public static class ActorLoot
{
    // Constraint: a frozen ledger entry wins over the base record, except that death stacks not yet added to it still count; an unreadable or partial actor counts as having items so the prompt never claims Empty on a guess.
    public static bool HasItems(IActorRecords records, ContainerLedger ledger, string refKey, string baseKey)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(ledger);
        var held = ledger.Find(refKey);
        if (held is { Stacks.Count: > 0 }) return true;
        if (held is not null && ledger.HasDeathLoot(refKey)) return false;
        if (records.Read(refKey, baseKey) is not { Complete: true } setup) return true;
        if (held is null && setup.Stacks.Any(stack => stack.Count > 0)) return true;
        return !ledger.HasDeathLoot(refKey) && setup.DeathStacks is { } death && death.Any(stack => stack.Count > 0);
    }
}
