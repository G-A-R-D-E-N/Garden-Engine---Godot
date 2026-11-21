using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenCommonwealth.Services.Interaction;

public enum PendingStep { Drop, Wait, Retarget, Place }

public static class CompanionFollowRules
{
    public static float StartUnits(CompanionDistance distance) => distance switch
    {
        CompanionDistance.Near => FollowDistances.Near,
        CompanionDistance.Medium => FollowDistances.Medium,
        CompanionDistance.Far => FollowDistances.Far,
        _ => throw new ArgumentOutOfRangeException(nameof(distance)),
    };

    public static bool OwnsRoute(int mine, int current) => mine > 0 && mine == current;

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

    // Invariant: a living Wait companion that is still held by the carry holder moves into the cell it waits in, so the holder cleanup cannot free it.
    public static bool AdoptsIntoCell(CompanionEntry? entry, string refKey) =>
        refKey.Length > 0 && entry is { State: CompanionState.Wait } && entry.RefKey == refKey;

    // Invariant: a marker is skipped only while a live puppet with its reference is in the tree, so a companion freed with its cell respawns from the marker.
    public static bool SkipsMarker(string markerRefKey, bool puppetPresent) => puppetPresent && markerRefKey.Length > 0;

    // Invariant: a pending arrival is dropped only when its puppet is gone or unmarked; a recorded player that went away is replaced by the current one, never abandoned with the marker set.
    public static PendingStep PlanPending(bool puppetLive, bool marked, bool arrivalKnown, bool arrivalLive, float seconds, float timeoutSeconds)
    {
        if (!puppetLive || !marked) return PendingStep.Drop;
        if (!arrivalKnown) return seconds >= timeoutSeconds ? PendingStep.Retarget : PendingStep.Wait;
        return arrivalLive ? PendingStep.Place : PendingStep.Retarget;
    }

    // Invariant: the latch names the current entry only; a live puppet with its reference clears it, a dead one sets it and an absent one keeps it.
    public static string? DeathLatch(string? latched, string refKey, bool puppetPresent, bool alive) =>
        puppetPresent ? (alive ? null : refKey) : latched == refKey ? latched : null;

    // Invariant: after a load the latch comes from the saved life of the restored companion, never from the replaced world.
    public static string? LatchAfterLoad(CompanionEntry? entry, bool? storedDead) => entry is not null && storedDead == true ? entry.RefKey : null;

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

    // Invariant: the first candidate that resolves and is clear wins, in candidate order; null when none is.
    public static Vector3? FirstClear(IEnumerable<Vector3> candidates, Func<Vector3, Vector3?> resolve, Func<Vector3, bool> isClear)
    {
        foreach (var candidate in candidates)
            if (resolve(candidate) is { } point && isClear(point)) return point;
        return null;
    }
}
