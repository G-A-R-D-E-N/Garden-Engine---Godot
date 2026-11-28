using System;
using System.Numerics;

namespace OpenCommonwealth.Services.Interaction;

public static class CompanionFollowRules
{
    public static float StartUnits(CompanionDistance distance) => distance switch
    {
        CompanionDistance.Near => FollowDistances.Near,
        CompanionDistance.Medium => FollowDistances.Medium,
        CompanionDistance.Far => FollowDistances.Far,
        _ => throw new ArgumentOutOfRangeException(nameof(distance)),
    };

    public static FollowerState StateOf(CompanionState state) => state switch
    {
        CompanionState.Follow => FollowerState.Follow,
        CompanionState.Wait => FollowerState.Wait,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static float PlaceUnits(CompanionDistance distance, FollowTunables tunables) =>
        StartUnits(distance) * tunables.StopRatio * FollowPlanner.ArrivalInset;

    // Invariant: only a living Follow companion is carried across a cell change; a Wait companion stays in its cell.
    public static bool Carries(CompanionEntry? entry, string refKey, bool alive) =>
        alive && refKey.Length > 0 && entry is { State: CompanionState.Follow } && entry.RefKey == refKey;

    // Invariant: a marker is skipped only while a live puppet with its reference is in the tree, so a companion freed with its cell respawns from the marker.
    public static bool SkipsMarker(string markerRefKey, bool puppetPresent) => puppetPresent && markerRefKey.Length > 0;

    public static bool NeedsRespawn(CompanionEntry? entry, bool puppetPresent, bool enabled) =>
        enabled && !puppetPresent && entry is { State: CompanionState.Follow };

    // Requires: yawRadians is a Godot Y rotation (forward is minus Z at yaw 0); meters is finite and above zero.
    public static Vector3[] Candidates(Vector3 player, float yawRadians, float meters)
    {
        if (!float.IsFinite(meters) || meters <= 0f) throw new ArgumentOutOfRangeException(nameof(meters));
        if (!float.IsFinite(yawRadians)) throw new ArgumentOutOfRangeException(nameof(yawRadians));
        var forward = new Vector3(-MathF.Sin(yawRadians), 0f, -MathF.Cos(yawRadians));
        var right = new Vector3(MathF.Cos(yawRadians), 0f, -MathF.Sin(yawRadians));
        return [player + right * meters, player - right * meters, player - forward * meters];
    }
}
