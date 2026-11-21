using System;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Interaction;

public static class LockpickResults
{
    // Constraint: the menu can finish after the container node was freed, so the caller's open action must tolerate that.
    public static Action<bool> ForContainer(ContainerLedger ledger, string refKey, Action open, Action? picked = null) => unlocked =>
    {
        if (!unlocked) return;
        ledger.MarkUnlocked(refKey);
        picked?.Invoke();
        open();
    };
}
