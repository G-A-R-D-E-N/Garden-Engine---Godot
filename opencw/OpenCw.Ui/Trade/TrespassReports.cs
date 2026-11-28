using System;
using System.Linq;
using OpenCommonwealth.Services.Interaction;

namespace OpenCommonwealth.Services.Ui.Trade;

public static class TrespassReports
{
    // Requires: the caller names the use it reports; a reference with a witnessed trespass, or with an unwitnessed one that the witness source does not see now, reports nothing; a failing owner check throws and the host catches it.
    public static CrimeEvent? Report(CrimeLedger ledger, ActivationTarget target, Func<ActivationTarget, bool> allows)
    {
        if (string.IsNullOrWhiteSpace(target.Owner) || target.OwnerNoCrime || string.IsNullOrWhiteSpace(target.BaseKey)) return null;
        var prior = ledger.Events.Where(crime => crime.Kind == CrimeKind.Trespass && string.Equals(crime.Ref, target.RefKey, StringComparison.OrdinalIgnoreCase)).ToList();
        if (prior.Any(crime => crime.Witnessed)) return null;
        if (allows(target)) return null;
        if (prior.Count > 0 && !ledger.Seen(CrimeKind.Trespass)) return null;
        return ledger.Report(CrimeKind.Trespass, target.Owner, target.BaseKey, 1, target.RefKey, witnessed: false);
    }
}
