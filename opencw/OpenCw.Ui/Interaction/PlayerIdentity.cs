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

    public const double RetrySeconds = 5;

    // Constraint: needs a load order and an unresolved session; a failed attempt in this session is retried only after RetrySeconds.
    public static bool NeedsResolve(bool hasLoadOrder, int resolvedSession, int session, int attemptedSession = -1, double secondsSinceAttempt = 0) =>
        hasLoadOrder && resolvedSession != session && (attemptedSession != session || secondsSinceAttempt >= RetrySeconds);
}
