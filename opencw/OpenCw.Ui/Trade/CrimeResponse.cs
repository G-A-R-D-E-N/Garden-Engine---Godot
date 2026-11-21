using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Trade;

public sealed record CrimeResponse(CrimeKind Kind, string Owner, IReadOnlyList<string> Actors);

public static class CrimeResponseRules
{
    // Invariant: UNVERIFIED default, see docs/code-notes/GardenEngine.md.
    public static IReadOnlyList<string> Actors(CrimeEvent raw, string? faction, bool witnessed)
    {
        if (raw.Kind == CrimeKind.Pickpocket) return raw.Attempt && !string.IsNullOrWhiteSpace(raw.Ref) ? [raw.Ref] : [];
        if (!witnessed || raw.SeenBy is not { Count: > 0 } seen) return [];
        return seen.Where(s => faction is null || string.Equals(s.Faction, faction, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Ref).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
