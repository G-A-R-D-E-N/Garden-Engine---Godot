using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Vats;

public static class VatsSoundMap
{
    public static readonly IReadOnlyDictionary<string, string> Cues = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["select"] = "UIVATSSelectPart",
    };

    public static string? EditorId(string cue) => Cues.GetValueOrDefault(cue);
}
