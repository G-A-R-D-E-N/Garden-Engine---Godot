using System;
using System.Collections.Generic;
using System.Numerics;
using Radicle.Interop;
using Vector3 = System.Numerics.Vector3;

namespace OpenCommonwealth.Services.Papyrus.Events;

// Requires: Translation is in FO4 units and the frame is the NIF frame unless ToGodot was applied; the rotation unit quaternion rotates with q v q^-1.
public readonly record struct RootDelta(Vector3 Translation, Quaternion Rotation)
{
    public static readonly RootDelta None = new(Vector3.Zero, Quaternion.Identity);

    // Invariant: the NIF to Godot change of axes is (x, y, z) to (x, z, -y), a proper rotation, so a quaternion maps the same way and w stays.
    public RootDelta ToGodot(float metersPerUnit) =>
        new(new Vector3(Translation.X, Translation.Z, -Translation.Y) * metersPerUnit, new Quaternion(Rotation.X, Rotation.Z, -Rotation.Y, Rotation.W));
}

public readonly record struct NifRoot(string Name, Vector3 Translation, Quaternion Rotation)
{
    // Requires: the node rotation is the row-major 3x3 NIF matrix, mapped to the row-vector matrix the way NifNode.LocalMatrix does.
    public static NifRoot From(NifNode node)
    {
        var r = node.Rotation;
        var matrix = new Matrix4x4(r[0], r[3], r[6], 0, r[1], r[4], r[7], 0, r[2], r[5], r[8], 0, 0, 0, 0, 1);
        return new NifRoot(node.Name, node.Translation, Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix)));
    }
}

// Requires: sampled once at clip read time because the NIF handle is gone afterwards; each sample is the root pose relative to the root's rest pose.
public sealed class RootPoseTrack
{
    public const int SamplesPerSecond = 30;
    public const int MaxSamples = 1024;

    private readonly RootDelta[] _samples;
    private readonly float _seconds;

    private RootPoseTrack(RootDelta[] samples, float seconds)
    {
        _samples = samples;
        _seconds = seconds;
    }

    public int Samples => _samples.Length;

    // Constraint: a NIF quaternion component order of (w, x, y, z) is the nif.xml order and is UNVERIFIED against the Radicle output.
    public static Quaternion FromNif(Vector4 raw)
    {
        var q = new Quaternion(raw.Y, raw.Z, raw.W, raw.X);
        return q.LengthSquared() > 0 && float.IsFinite(q.LengthSquared()) ? Quaternion.Normalize(q) : Quaternion.Identity;
    }

    // Invariant: the delta is P(t) * P(rest)^-1, so the rest pose gives identity and the baked closed mesh needs no correction.
    public static RootDelta Delta(NifRoot rest, Vector3 translation, Quaternion rotation)
    {
        var turn = Quaternion.Normalize(rotation * Quaternion.Conjugate(rest.Rotation));
        return new RootDelta(translation - Vector3.Transform(rest.Translation, turn), turn);
    }

    public static RootPoseTrack? From(NifAnimSequenceInfo sequence, NifRoot rest, Func<float, IReadOnlyList<NifAnimPoseInfo>> evaluate, float seconds)
    {
        var count = Math.Clamp((int)MathF.Ceiling(seconds * SamplesPerSecond) + 1, 2, MaxSamples);
        var samples = new RootDelta[count];
        for (var i = 0; i < count; i++)
        {
            var time = sequence.StartTime + (sequence.StopTime - sequence.StartTime) * i / (count - 1);
            NifAnimPoseInfo? pose = null;
            foreach (var candidate in evaluate(time))
                if (string.Equals(candidate.NodeName, rest.Name, StringComparison.OrdinalIgnoreCase)) pose = candidate;
            if (pose is null || !float.IsFinite(pose.Translation.X + pose.Translation.Y + pose.Translation.Z)) return null;
            samples[i] = Delta(rest, pose.Translation, FromNif(pose.Rotation));
        }
        return new RootPoseTrack(samples, seconds);
    }

    // Invariant: seconds outside the clip clamp to its ends; between samples the translation lerps and the rotation slerps.
    public RootDelta At(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return _samples[0];
        if (seconds >= _seconds) return _samples[^1];
        var position = seconds / _seconds * (_samples.Length - 1);
        var low = (int)Math.Floor(position);
        var blend = (float)(position - low);
        var a = _samples[low];
        var b = _samples[Math.Min(low + 1, _samples.Length - 1)];
        return new RootDelta(Vector3.Lerp(a.Translation, b.Translation, blend), Quaternion.Slerp(a.Rotation, b.Rotation, blend));
    }
}
