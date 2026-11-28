using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public static class PlayerPerkRows
{
    // Constraint: the key stands in for the name until RecordPlayerStatusSource resolves the PERK record.
    public static IReadOnlyList<OwnedPerk> FromRanks(IReadOnlyDictionary<string, int> ranks) =>
        ranks.Where(pair => pair.Value > 0)
             .OrderBy(pair => pair.Key, StringComparer.Ordinal)
             .Select(pair => new OwnedPerk(pair.Key, pair.Key, pair.Value, []))
             .ToArray();

    // Invariant: a perk whose record gave no name is dropped, so a row never shows a raw form key; whether a named perk is player-visible is UNVERIFIED.
    public static IReadOnlyList<OwnedPerk> Named(IEnumerable<OwnedPerk> perks) =>
        perks.Where(perk => !string.IsNullOrWhiteSpace(perk.Name) && !string.Equals(perk.Name, perk.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
}
