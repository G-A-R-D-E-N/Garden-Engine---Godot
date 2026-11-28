using System;

namespace OpenCommonwealth.Services.Interaction;

public static class PlayerIdentity
{
    public const uint PlayerRefId = 0x14;
    public static readonly string PlayerRefKey = $"{PlayerRefId:X6}:Fallout4.esm";
    public const string BaseEditorId = "Player";

    // Constraint: the reference half is fixed and never looked up; null when the base record is missing.
    public static (string Ref, string Base)? Resolve(Func<string, string, string?> keyOf) =>
        keyOf(BaseEditorId, "NPC_") is { Length: > 0 } basis ? (PlayerRefKey, basis) : null;

    // Constraint: true only while a load order exists and the session has not been attempted yet.
    public static bool NeedsResolve(bool hasLoadOrder, int attemptedSession, int session) =>
        hasLoadOrder && attemptedSession != session;
}
