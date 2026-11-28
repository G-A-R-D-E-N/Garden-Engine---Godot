using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Radicle.Interop;
using LockKind = Radicle.Interop.Native.LockKind;
using RadicleKey = Radicle.Interop.FormKey;

namespace OpenCommonwealth.Services.Navigation;

public static class RadicleNavWalk
{
    private static readonly ConditionalWeakTable<RadicleEnvironment, ConcurrentDictionary<RadicleKey, bool>> LockedDoors = new();

    // Constraint: a door starts locked when its record has lock data above level None; an unreadable door counts as locked and is not cached.
    private static bool StartsLocked(RadicleEnvironment env, RadicleKey door)
    {
        var cache = LockedDoors.GetOrCreateValue(env);
        if (cache.TryGetValue(door, out var known)) return known;
        try
        {
            var locked = env.Winner(door) is not { } record || env.Lock(record) is { HasLockData: true, Kind: not LockKind.None };
            cache[door] = locked;
            return locked;
        }
        catch (RadicleException) { return true; }
    }

    public static NavWalkResult Read(RadicleEnvironment env, RadicleKey cell)
    {
        var meshes = new List<NavmeshData>();
        var skipped = new List<string>();
        foreach (var child in env.CellChildren(cell))
        {
            var info = env.Info(child.Record);
            if (info.Signature != "NAVM") continue;
            try
            {
                if (env.NavmeshWalk(child.Record, NavmeshBuilder.LinkReach) is { } view) meshes.Add(NavWalkImport.ToData(Plain(info.FormKey.ToString(), view)));
            }
            catch (RadicleException ex) { skipped.Add($"{info.FormKey}: {ex.Message}"); }
        }
        return new NavWalkResult(meshes, skipped);
    }

    public static uint LargeForbiddenClass(RadicleEnvironment env) => NavWalkImport.CachedForbiddenClass(env, () =>
    {
        uint mask = 0;
        foreach (var race in env.WinnersOf("RACE"))
        {
            RadicleKey? key = null;
            try { key = env.Info(race).FormKey; mask |= env.WalkAgentProfile(key).ForbiddenClass; }
            catch (RadicleException ex) { Godot.GD.PushWarning($"[Nav] race {key?.ToString() ?? "unknown"} forbidden class not read: {ex.Message}"); }
        }
        return mask;
    });

    public static NavProfile Profile(RadicleEnvironment env, RadicleKey? race)
    {
        var profile = env.WalkAgentProfile(race);
        return NavWalkImport.Profile(new PlainNavProfile(profile.Radius, profile.Height, profile.Large));
    }

    public static DoorVerdict DoorVerdictOf(string doorFormKey, ActorIdentity? actor)
    {
        if (!GameLoader.TryParseKey(doorFormKey, out var door)) return DoorVerdict.Unknown;
        RadicleKey? npc = actor != null && GameLoader.TryParseKey(actor.NpcKey, out var key) ? key : null;
        GameLoader.EnvLifetime.EnterReadLock();
        try
        {
            if (GameLoader.ActiveRecords is not { } records || !LoadSession.IsCurrent(records.Environment)) return DoorVerdict.Unknown;
            var env = records.Environment;
            return Verdict(env.DoorPassable(npc, door, StartsLocked(env, door), false));
        }
        finally { GameLoader.EnvLifetime.ExitReadLock(); }
    }

    private static PlainNavView Plain(string key, NavmeshWalkView view) =>
        new(key, view.MetresPerUnit, view.Vertices.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray(), view.Triangles, view.TriangleClass,
            view.Links.Select(l => new PlainNavLink(l.SourceTriangle, l.SourceEdge, l.TargetNavmesh?.ToString() ?? "", l.TargetTriangle, l.TargetEdge,
                [l.Start.X, l.Start.Y, l.Start.Z], [l.End.X, l.End.Y, l.End.Z], Verdict(l.Verdict))).ToArray(),
            view.Doors.Where(d => d.Door != null).Select(d => new PlainNavDoor(d.Door!.Value.ToString(), d.Triangles)).ToArray());

    private static DoorVerdict Verdict(NavVerdict verdict) => verdict switch
    {
        NavVerdict.Passable => DoorVerdict.Passable,
        NavVerdict.NeedsOpening => DoorVerdict.NeedsOpening,
        NavVerdict.Blocked => DoorVerdict.Blocked,
        _ => DoorVerdict.Unknown,
    };
}
