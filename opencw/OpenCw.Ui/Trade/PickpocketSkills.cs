using System;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Trade;

public static class PickpocketSkills
{
    public const int MaxTemplateHops = 8;

    // Requires: stored answers the NPC_'s own base (null when absent, 0 when stored as 0); statsOwner answers the node itself when it owns Stats, its template owner when templated, null when blocked.
    public static float? Resolve(Func<FormKey, float?> stored, Func<FormKey, FormKey?> statsOwner, FormKey npc)
    {
        var current = npc;
        for (var hop = 0; ; hop++)
        {
            var own = stored(current);
            if (own > 0) return own;
            if (statsOwner(current) is not { } next) return null;
            if (next.Equals(current)) return own;
            if (hop == MaxTemplateHops) return null;
            current = next;
        }
    }
}
