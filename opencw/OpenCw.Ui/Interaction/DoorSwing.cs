using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Interaction;

public enum DoorSwingPhase { Closed, Opening, Open, Closing }

public enum DoorSwingStyle { Hinge, Slide }

public sealed record DoorSwingSequence(string Name, float Start, float Stop, float Frequency);

// Constraint: UNVERIFIED tuning, see opencw-interact-doors.md; the fallback numbers have no engine source.
public sealed record DoorSwingPlan(float OpenSeconds, float CloseSeconds, DoorSwingStyle Style, bool FromSequence)
{
    public const byte SlidingFlag = 0x10;
    public const float FallbackSeconds = 1f;
    public const float FallbackHingeRadians = MathF.PI / 2;
    public const float FallbackSlideMeters = 1f;

    public static DoorSwingPlan From(IReadOnlyList<DoorSwingSequence> sequences, byte doorFlags)
    {
        var style = (doorFlags & SlidingFlag) != 0 ? DoorSwingStyle.Slide : DoorSwingStyle.Hinge;
        var open = Seconds(sequences, "Open");
        if (open is not { } opening) return new(FallbackSeconds, FallbackSeconds, style, false);
        return new(opening, Seconds(sequences, "Close") ?? opening, style, true);
    }

    private static float? Seconds(IReadOnlyList<DoorSwingSequence> sequences, string name)
    {
        foreach (var sequence in sequences)
        {
            if (!string.Equals(sequence.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            var seconds = (sequence.Stop - sequence.Start) / sequence.Frequency;
            if (float.IsFinite(seconds) && seconds > 0) return seconds;
        }
        return null;
    }
}

// Requires: main thread only. Progress runs from 0 (closed) to 1 (open); Command keeps it, so a command mid-swing reverses from there.
public sealed class DoorSwing(DoorSwingPlan plan, bool open = false)
{
    private bool _target = open;

    public float Progress { get; private set; } = open ? 1f : 0f;

    public DoorSwingPhase Phase => Progress switch
    {
        <= 0f when !_target => DoorSwingPhase.Closed,
        >= 1f when _target => DoorSwingPhase.Open,
        _ => _target ? DoorSwingPhase.Opening : DoorSwingPhase.Closing,
    };

    public bool Moving => Phase is DoorSwingPhase.Opening or DoorSwingPhase.Closing;

    public void Command(bool open) => _target = open;

    public void Snap(bool open)
    {
        _target = open;
        Progress = open ? 1f : 0f;
    }

    public bool Advance(float seconds)
    {
        if (!(seconds > 0) || !Moving) return Moving;
        var step = seconds / (_target ? plan.OpenSeconds : plan.CloseSeconds);
        Progress = _target ? Math.Min(1f, Progress + step) : Math.Max(0f, Progress - step);
        return Moving;
    }
}
