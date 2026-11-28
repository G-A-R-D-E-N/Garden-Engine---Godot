using System;
using System.Collections.Generic;
using System.Linq;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Papyrus.Events;

public static class NifAnimationClips
{
    // Requires: textKeys reads the NiTextKeyExtraData block of a sequence; a Radicle error there keeps the clip with no keys.
    public static AnimationClip? From(IEnumerable<NifAnimSequenceInfo> sequences, Func<int, IReadOnlyList<NifAnimTextKeyInfo>> textKeys, string animation,
        NifRoot? root = null, Func<int, float, IReadOnlyList<NifAnimPoseInfo>>? poses = null)
    {
        foreach (var sequence in sequences)
        {
            if (!string.Equals(sequence.Name, animation, StringComparison.OrdinalIgnoreCase)) continue;
            var seconds = (sequence.StopTime - sequence.StartTime) / sequence.Frequency;
            if (!float.IsFinite(seconds) || seconds <= 0) return null;
            var (track, blocked) = Pose(sequence, seconds, root, poses);
            return new AnimationClip(sequence.Name, seconds, Keys(sequence, seconds, textKeys), track, blocked);
        }
        return null;
    }

    // Invariant: a pose track exists only when every controlled node is the NIF root, the one node the baked merged mesh can follow; any other clip is blocked with a reason and its pose is never guessed.
    private static (RootPoseTrack? Track, string? Blocked) Pose(NifAnimSequenceInfo sequence, float seconds, NifRoot? root, Func<int, float, IReadOnlyList<NifAnimPoseInfo>>? poses)
    {
        if (root is not { } rest || poses is null || sequence.Controlled.Length == 0) return (null, null);
        var others = sequence.Controlled.Count(entry => !string.Equals(entry.NodeName, rest.Name, StringComparison.OrdinalIgnoreCase));
        if (others > 0) return (null, $"it animates {others} node(s) besides the root, which the merged mesh cannot address");
        try { return RootPoseTrack.From(sequence, rest, time => poses(sequence.Block, time), seconds) is { } track ? (track, null) : (null, "the root pose could not be read"); }
        catch (RadicleException) { return (null, "the root pose could not be read"); }
    }

    private static IReadOnlyList<AnimationKey> Keys(NifAnimSequenceInfo sequence, float seconds, Func<int, IReadOnlyList<NifAnimTextKeyInfo>> textKeys)
    {
        if (sequence.TextKeysRef < 0) return [];
        IReadOnlyList<NifAnimTextKeyInfo> raw;
        try { raw = textKeys(sequence.TextKeysRef); }
        catch (RadicleException) { return []; }
        return raw
            .Select(key => new AnimationKey((key.Time - sequence.StartTime) / sequence.Frequency, key.Text))
            .Where(key => float.IsFinite(key.Time) && key.Time >= 0 && key.Time <= seconds)
            .OrderBy(key => key.Time)
            .ToList();
    }
}
