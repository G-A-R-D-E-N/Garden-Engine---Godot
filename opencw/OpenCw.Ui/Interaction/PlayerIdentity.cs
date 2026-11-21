using System;

namespace OpenCommonwealth.Services.Interaction;

public static class PlayerIdentity
{
    public const string RefEditorId = "PlayerRef";
    public const string BaseEditorId = "Player";

    // Constraint: both keys come from the loaded records by editor id; null when either record is missing.
    public static (string Ref, string Base)? Resolve(Func<string, string, string?> keyOf) =>
        keyOf(RefEditorId, "REFR") is { Length: > 0 } reference && keyOf(BaseEditorId, "NPC_") is { Length: > 0 } basis
            ? (reference, basis)
            : null;

    // Constraint: true only while a load order exists and the session has not been attempted yet.
    public static bool NeedsResolve(bool hasLoadOrder, int attemptedSession, int session) =>
        hasLoadOrder && attemptedSession != session;
}
