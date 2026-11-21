using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Trade;

// Requires: main thread only; one Attack per victim until it dies, and a Murder follows an Attack on that victim even though the attack made it hostile.
public sealed class ViolenceReports
{
    private readonly HashSet<string> _attacked = new(StringComparer.OrdinalIgnoreCase);

    public CrimeEvent? Attack(CrimeLedger ledger, string victimRef, string victimBase, bool victimHostile)
    {
        if (string.IsNullOrWhiteSpace(victimRef) || victimHostile || !_attacked.Add(victimRef)) return null;
        return ledger.Report(CrimeKind.Attack, victimRef, Item(victimRef, victimBase), 1, victimRef, witnessed: false);
    }

    public CrimeEvent? Murder(CrimeLedger ledger, string victimRef, string victimBase, bool victimHostile)
    {
        if (string.IsNullOrWhiteSpace(victimRef)) return null;
        var attacked = _attacked.Remove(victimRef);
        if (victimHostile && !attacked) return null;
        return ledger.Report(CrimeKind.Murder, victimRef, Item(victimRef, victimBase), 1, victimRef, witnessed: false);
    }

    public void Clear() => _attacked.Clear();

    private static string Item(string victimRef, string victimBase) => string.IsNullOrWhiteSpace(victimBase) ? victimRef : victimBase;
}
