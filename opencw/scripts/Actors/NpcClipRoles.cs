using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth;

// Constraint: no Godot or Radicle types here so the mapping is unit-testable without the engine.
public sealed record NpcBehaviorSource(string Entry, string AnimationRoot, string Directory, bool Mood, bool Supplement, string[] Roles);

public static class NpcClipRoles
{
    public const string HumanRoot = "meshes/actors/character";
    public const string RobotRoot = "meshes/actors/robot";
    public const string RobotSkeletonEntry = "meshes/actors/robot/characterassets/skeleton.hkx";
    public const string MoodFolder = "animations/mt/neutral";
    public const string TurnLeftRole = "turn_left_90";
    public const string TurnRightRole = "turn_right_90";

    private static readonly NpcBehaviorSource[] Human =
    [
        new(HumanRoot + "/behaviors/locomotion_8wayblend.hkx", HumanRoot, "animations/1hm", false, false, ["walk", "run"]),
        new(HumanRoot + "/behaviors/mtbehavior.hkx", HumanRoot, "animations/", true, false, [TurnLeftRole, TurnRightRole]),
    ];

    private static readonly NpcBehaviorSource[] Robot =
    [
        new(RobotRoot + "/behaviors/handybehavior.hkx", RobotRoot, "", false, true, ["walk", "run", TurnLeftRole, TurnRightRole]),
    ];

    public static IReadOnlyList<NpcBehaviorSource> BehaviorsFor(string skeletonEntry, string humanSkeleton) =>
        skeletonEntry.Equals(humanSkeleton, StringComparison.OrdinalIgnoreCase) ? Human
        : skeletonEntry.Equals(RobotSkeletonEntry, StringComparison.OrdinalIgnoreCase) ? Robot
        : [];

    // Constraint: only an entry directly inside the animations folder is a mood-relative name; a deeper entry already names its folder.
    public static string? MoodEntry(string entry, bool female)
    {
        string path = entry.Replace('\\', '/').ToLowerInvariant();
        string prefix = HumanRoot + "/animations/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.IndexOf('/', prefix.Length) >= 0 || path.Length == prefix.Length) return null;
        return HumanRoot + "/" + MoodFolder + (female ? "/female/" : "/") + path[prefix.Length..];
    }

    // Constraint: Radicle's table has a key for turnleft90 but not for the robot's bare turnleft/turnright, so those two names are mapped here.
    public static string? RobotRole(string animation)
    {
        string name = Key(animation);
        return name switch
        {
            "turnleft" => TurnLeftRole,
            "turnright" => TurnRightRole,
            _ => null,
        };
    }

    public static IEnumerable<(string Role, string Entry)> SupplementRoles(IEnumerable<string> animations, string animationRoot)
    {
        var seen = new HashSet<string>();
        foreach (var animation in animations)
        {
            if (RobotRole(animation) is not { } role || !seen.Add(role)) continue;
            if (Entry(animation, animationRoot) is { } entry) yield return (role, entry);
        }
    }

    public static string? Entry(string animation, string animationRoot)
    {
        string path = animation.Replace('\\', '/').ToLowerInvariant().Trim('/');
        int dot = path.LastIndexOf('.');
        if (dot > path.LastIndexOf('/')) path = path[..dot];
        if (path.Length == 0 || path.Contains(':') || path.Split('/').Any(s => s is ".." or ".")) return null;
        return animationRoot.TrimEnd('/') + "/" + path + ".hkx";
    }

    private static string Key(string animation)
    {
        string name = animation.Replace('\\', '/').ToLowerInvariant();
        name = name[(name.LastIndexOf('/') + 1)..];
        int dot = name.LastIndexOf('.');
        return dot >= 0 ? name[..dot] : name;
    }
}
