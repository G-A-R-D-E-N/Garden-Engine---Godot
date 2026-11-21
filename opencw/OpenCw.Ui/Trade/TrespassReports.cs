using System;
using System.Linq;
using OpenCommonwealth.Services.Interaction;

namespace OpenCommonwealth.Services.Ui.Trade;

public static class TrespassReports
{
    // Requires: the caller names the use it reports; only the latest ledger event can suppress a repeat (a witnessed trespass on this reference, or an unwitnessed one the owner's faction does not see now), so any other recorded crime ends the episode; a failing owner check throws and the host catches it.
    public static CrimeEvent? Report(CrimeLedger ledger, ActivationTarget target, Func<ActivationTarget, bool> allows)
    {
        if (string.IsNullOrWhiteSpace(target.Owner) || target.OwnerNoCrime || string.IsNullOrWhiteSpace(target.BaseKey)) return null;
        ledger.ResolvePending();
        var prior = ledger.Events.LastOrDefault() is { Kind: CrimeKind.Trespass } last && string.Equals(last.Ref, target.RefKey, StringComparison.OrdinalIgnoreCase) ? last : null;
        if (prior is { Witnessed: true }) return null;
        if (allows(target)) return null;
        if (prior is not null && !ledger.Seen(CrimeKind.Trespass, ledger.FactionOf(target.Owner))) return null;
        return ledger.Report(CrimeKind.Trespass, target.Owner, target.BaseKey, 1, target.RefKey, witnessed: false);
    }
}
