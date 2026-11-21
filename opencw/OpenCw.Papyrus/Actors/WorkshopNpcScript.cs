using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus.Actors;

public sealed record WorkshopNpcFacts(bool HasScript, bool Commandable);

public static class WorkshopNpcScript
{
    public const string ScriptName = "WorkshopNPCScript";
    public const string CommandableProperty = "bCommandable";

    // Requires: a script that is absent reads as no settler; a missing, non-bool or unparsable bCommandable reads as false (the script's own default).
    public static WorkshopNpcFacts Read(IReadOnlyList<ScriptAttachment> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        var script = scripts.FirstOrDefault(item => string.Equals(item.Name, ScriptName, StringComparison.OrdinalIgnoreCase));
        if (script is null) return new WorkshopNpcFacts(false, false);
        var flag = script.Values.FirstOrDefault(value => string.Equals(value.Name, CommandableProperty, StringComparison.OrdinalIgnoreCase));
        var commandable = flag is { Kind: ScriptValueKind.Bool, Value: { } text } && bool.TryParse(text, out var on) && on;
        return new WorkshopNpcFacts(true, commandable);
    }
}
