using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Trade;

public sealed record WitnessCandidate(
    string Ref, bool Alive, bool Asleep, double Distance, double SightRange, bool LineOfSight, string? CrimeFaction, bool FactionIgnoresStealing);

public interface IWitnessSource
{
    IReadOnlyList<WitnessCandidate> Candidates();
}

public static class WitnessRules
{
    // Invariant: UNVERIFIED rule; the thresholds arrive with the candidate (Distance, SightRange, LineOfSight), none is chosen here, and a witness with no crime faction has nobody to report to.
    public static bool Sees(WitnessCandidate candidate, CrimeKind kind, string? faction = null) =>
        candidate.Alive && !candidate.Asleep && candidate.LineOfSight && candidate.Distance <= candidate.SightRange
        && !string.IsNullOrWhiteSpace(candidate.CrimeFaction) && !(kind == CrimeKind.Theft && candidate.FactionIgnoresStealing)
        && (faction is null || string.Equals(candidate.CrimeFaction, faction, System.StringComparison.OrdinalIgnoreCase));

    // Invariant: a faction narrows the witnesses to that crime faction; null accepts any faction.
    public static bool AnySees(IEnumerable<WitnessCandidate> candidates, CrimeKind kind, string? faction = null)
    {
        foreach (var candidate in candidates)
            if (Sees(candidate, kind, faction)) return true;
        return false;
    }
}
