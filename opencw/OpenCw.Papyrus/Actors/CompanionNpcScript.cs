using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus.Actors;

public static class CompanionNpcScript
{
    public const string ScriptName = "CompanionActorScript";

    // Requires: the vanilla script is matched by name, case-insensitively, on the marker's attachments; an absent script is not a possible companion.
    public static bool IsPossibleCompanion(IReadOnlyList<ScriptAttachment> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        return scripts.Any(item => string.Equals(item.Name, ScriptName, StringComparison.OrdinalIgnoreCase));
    }

    // Requires: baseNames is read only when the marker has no companion script; it is the script names of the NPC base record.
    public static bool IsPossibleCompanion(IReadOnlyList<ScriptAttachment> scripts, Func<IReadOnlyList<string>> baseNames)
    {
        ArgumentNullException.ThrowIfNull(baseNames);
        return IsPossibleCompanion(scripts) || baseNames().Any(name => string.Equals(name, ScriptName, StringComparison.OrdinalIgnoreCase));
    }
}
