namespace OpenCommonwealth.Services.Interaction;

// Invariant: a roster companion and the player are allies in either order; every other pair is left to the engine relation.
public static class CompanionRelations
{
    public static bool AlliedWithPlayer(bool observerCompanion, bool observerPlayer, bool targetCompanion, bool targetPlayer) =>
        (observerCompanion && targetPlayer) || (targetCompanion && observerPlayer);
}
