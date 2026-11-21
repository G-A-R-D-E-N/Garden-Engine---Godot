using System;

namespace OpenCommonwealth.Services.Interaction;

// Invariant: a menu choice runs only while the target is still the actor the menu opened for, still routes to Talk, and is within reach of the player.
public static class CompanionChoice
{
    public const float ReachMeters = 4f;

    public static bool StillValid(string openedRefKey, string currentRefKey, NpcRoute route, float distanceMeters) =>
        openedRefKey.Length > 0 && string.Equals(openedRefKey, currentRefKey, StringComparison.Ordinal)
        && route == NpcRoute.Talk && float.IsFinite(distanceMeters) && distanceMeters <= ReachMeters;
}
