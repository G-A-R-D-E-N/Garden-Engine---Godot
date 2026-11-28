using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenCommonwealth.Services.Ui.Settings;

public static class KeyBindingLegacy
{
    public static string? NormalizeKeyboard(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var upper = text.Trim().ToUpperInvariant();
        switch (upper)
        {
            case "NONE": return null;
            case "LEFT MOUSE": return "Mouse Left";
            case "RIGHT MOUSE": return "Mouse Right";
            case "MIDDLE MOUSE": return "Mouse Middle";
            case "MOUSE 8": return "Mouse 4";
            case "MOUSE 9": return "Mouse 5";
        }
        if (upper.StartsWith("MOUSE ", StringComparison.Ordinal)) return null;
        return upper.Length == 1 ? upper : char.ToUpperInvariant(upper[0]) + upper[1..].ToLowerInvariant();
    }

    private static bool IsNone(string? text) =>
        string.IsNullOrWhiteSpace(text) || string.Equals(text.Trim(), "NONE", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, string?> Normalize(IEnumerable<KeyValuePair<string, string>> raw)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (id, text) in raw)
        {
            var code = NormalizeKeyboard(text);
            if (code is null && !IsNone(text)) continue;
            result[id] = code;
        }
        return result;
    }
}
