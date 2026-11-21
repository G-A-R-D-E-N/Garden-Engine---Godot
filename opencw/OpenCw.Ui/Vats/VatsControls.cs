using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Vats;

// Invariant: must match the bindings in VatsMenu.HandleKey, HandlePad and HandleAxis; every label VatsMenuModel.HintLabels returns needs a row.
public static class VatsControls
{
    private static readonly IReadOnlyDictionary<string, (string Keyboard, string Controller)> Rows = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        ["SELECT"] = ("Q / Click", "RT"),
        ["ACCEPT"] = ("Enter / E", "A"),
        ["RETURN"] = ("Esc / Tab", "B"),
        ["BODY PART"] = ("W / S", "D-Pad Up/Down"),
        ["CYCLE TARGETS"] = ("A / D", "LB / RB"),
        ["CRITICAL"] = ("Space", "X"),
        ["ABORT"] = ("Esc / Tab", "B"),
    };

    public static IEnumerable<string> Labels => Rows.Keys;

    public static string For(string label, bool controller) =>
        Rows.TryGetValue(label, out var row) ? (controller ? row.Controller : row.Keyboard) : string.Empty;
}
