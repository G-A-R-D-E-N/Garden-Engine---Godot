using System;
using System.Numerics;

namespace OpenCommonwealth.Services.Interaction;

public enum FollowAction { Idle, GoTo, Stop }

public enum FollowGait { Walk, Run }

// Invariant: values are the vanilla FollowerState settings (Follow 1, Wait 2); GoHome (4) is not modelled.
public enum FollowerState { Follow = 1, Wait = 2 }

public static class FollowDistances
{
    public const float Near = 150f;
    public const float Medium = 300f;
    public const float Far = 600f;
}

// Invariant: StopRatio is in (0, 1) and RunRatio is above 1; both scale the start distance.
public sealed record FollowTunables(float StopRatio = 0.5f, float RunRatio = 2f, float RepathSeconds = 0.5f, float MoveUnits = 69.99125f)
{
    public static FollowTunables Default { get; } = new();
}

public readonly record struct FollowMemory(bool HasGoal, Vector3 Goal, Vector3 PlayerAtIssue, float IssuedAt)
{
    public static FollowMemory None => default;
}

public readonly record struct FollowStep(FollowAction Action, FollowGait Gait, FollowMemory Memory);

public static class FollowPlanner
{
    public const float ArrivalInset = 0.9f;

    public static FollowStep Step(Vector3 self, Vector3 player, FollowerState state, float startUnits, float now, FollowMemory memory, FollowTunables tunables)
    {
        if (!float.IsFinite(startUnits) || startUnits <= 0f) throw new ArgumentOutOfRangeException(nameof(startUnits));
        if (!(tunables.StopRatio > 0f && tunables.StopRatio < 1f)) throw new ArgumentOutOfRangeException(nameof(tunables));
        if (!(tunables.RunRatio > 1f)) throw new ArgumentOutOfRangeException(nameof(tunables));
        if (state == FollowerState.Wait) return new FollowStep(FollowAction.Stop, FollowGait.Walk, FollowMemory.None);
        float distance = Horizontal(self - player);
        if (!float.IsFinite(distance)) return new FollowStep(FollowAction.Stop, FollowGait.Walk, FollowMemory.None);
        float stopUnits = startUnits * tunables.StopRatio;
        FollowGait gait = distance > startUnits * tunables.RunRatio ? FollowGait.Run : FollowGait.Walk;
        if (memory.HasGoal)
        {
            if (distance <= stopUnits) return new FollowStep(FollowAction.Stop, FollowGait.Walk, FollowMemory.None);
            if (now >= memory.IssuedAt && (now - memory.IssuedAt < tunables.RepathSeconds || Horizontal(player - memory.PlayerAtIssue) <= tunables.MoveUnits))
                return new FollowStep(FollowAction.GoTo, gait, memory);
        }
        else if (distance <= startUnits)
        {
            return new FollowStep(FollowAction.Idle, FollowGait.Walk, FollowMemory.None);
        }
        var away = new Vector3(self.X - player.X, 0f, self.Z - player.Z);
        var goal = player + away * (stopUnits * ArrivalInset / distance);
        return new FollowStep(FollowAction.GoTo, gait, new FollowMemory(true, goal, player, now));
    }

    private static float Horizontal(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
