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
    public static bool Sees(WitnessCandidate candidate, CrimeKind kind) =>
        candidate.Alive && !candidate.Asleep && candidate.LineOfSight && candidate.Distance <= candidate.SightRange
        && !string.IsNullOrWhiteSpace(candidate.CrimeFaction) && !(kind == CrimeKind.Theft && candidate.FactionIgnoresStealing);

    public static bool AnySees(IEnumerable<WitnessCandidate> candidates, CrimeKind kind)
    {
        foreach (var candidate in candidates)
            if (Sees(candidate, kind)) return true;
        return false;
    }
}
