using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus.Vm;

public static class RecordFragmentScripts
{
    public const string InfoPrefix = "TIF_";
    public const string TerminalPrefix = "TERM_";
    public const string InfoBegin = "Fragment_Begin";
    public const string InfoEnd = "Fragment_End";

    public static string TerminalFunction(int itemId) => $"Fragment_Terminal_{itemId:D2}";

    // Requires: exactly one cached script matches, because two plugins can reuse a low object id; an ambiguous or absent match reads as no script.
    public static string? Find(IEnumerable<string> scripts, string prefix, uint objectId)
    {
        var low = (objectId & 0xFFFFFFu).ToString("X6");
        var found = scripts.Where(name => name.Length >= prefix.Length + 9 && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && name[^9] == '_' && name[^6..].Equals(low, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return found.Count == 1 ? found[0] : null;
    }
}
