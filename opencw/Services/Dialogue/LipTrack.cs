using System;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Dialogue;

public sealed class LipTrack(LipAnimation lip)
{
    public LipAnimation Lip { get; } = lip;

    public int SlotCount => Lip.SlotCount;

    // TODO: verify frame = t * 30 + |PrerollFrames| against the engine, see CodeNotes LipTrack.
    public double FramePosition(double audioSeconds) => audioSeconds * LipAnimation.FramesPerSecond + Math.Abs(Lip.PrerollFrames);

    public void Sample(double audioSeconds, Span<float> slots)
    {
        if (slots.Length < SlotCount) throw new ArgumentException("slots is shorter than the slot count", nameof(slots));
        slots = slots[..SlotCount];
        slots.Clear();
        double position = FramePosition(audioSeconds);
        if (Lip.FrameCount == 0 || double.IsNaN(position) || position < 0 || position > Lip.FrameCount - 1) return;
        int frame = Math.Min((int)position, Lip.FrameCount - 1);
        int next = Math.Min(frame + 1, Lip.FrameCount - 1);
        float blend = (float)(position - frame);
        var weights = Lip.Weights.AsSpan();
        var a = weights.Slice(frame * SlotCount, SlotCount);
        var b = weights.Slice(next * SlotCount, SlotCount);
        for (int i = 0; i < slots.Length; i++)
            slots[i] = a[i] + (b[i] - a[i]) * blend;
    }

    public float JawOpen(double audioSeconds)
    {
        Span<float> slots = SlotCount <= 128 ? stackalloc float[SlotCount] : new float[SlotCount];
        Sample(audioSeconds, slots);
        return MouthShape.Openness(slots, Lip.PhonemeCount);
    }
}

public static class MouthShape
{
    public const int VisemeCount = 43;
    public const int RestSlot = 40;

    // Constraint: no LIP slot name table exists; RestSlot is the slot measured as the silence pose, not a name.
    public static float Openness(ReadOnlySpan<float> slots, int phonemeCount)
    {
        float sum = 0;
        int rest = phonemeCount == VisemeCount ? RestSlot : -1;
        for (int i = 0; i < Math.Min(phonemeCount, slots.Length); i++)
            if (i != rest && slots[i] > 0) sum += slots[i];
        return Math.Clamp(sum, 0f, 1f);
    }
}
