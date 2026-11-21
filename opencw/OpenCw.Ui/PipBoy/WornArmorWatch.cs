using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.PipBoy;

// Requires: main thread only; each armor id is reported once for the session, however often it is worn again.
public sealed class WornArmorWatch
{
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> NewlyWorn(IEnumerable<EquippedArmor> worn) =>
        worn.Select(piece => piece.Id).Where(id => _reported.Add(id)).ToArray();
}
