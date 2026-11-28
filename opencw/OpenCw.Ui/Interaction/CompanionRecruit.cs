namespace OpenCommonwealth.Services.Interaction;

// Invariant: an actor is recruited only when its marker or its NPC base record carries the companion script, it is not disallowed and it is not already the companion; otherwise the roster is unchanged.
public static class CompanionRecruit
{
    public static bool CanAsk(bool possibleCompanion, CompanionRoster roster, string refKey) =>
        possibleCompanion && refKey.Length > 0 && !roster.IsDisallowed(refKey) && !roster.IsCompanion(refKey);

    public static bool Ask(bool possibleCompanion, CompanionRoster roster, string refKey, string baseKey) =>
        CanAsk(possibleCompanion, roster, refKey) && roster.Recruit(refKey, baseKey);
}
