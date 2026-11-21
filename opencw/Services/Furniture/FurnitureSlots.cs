using Radicle.Interop;

namespace OpenCommonwealth.Services;

[Flags]
public enum FurnitureSides { None = 0, Front = 1, Rear = 2, Right = 4, Left = 8, Other = 16 }

public sealed record FurnitureSlot(int Index, double X, double Y, double Z, double Heading, FurnitureSides Sides, FormKey? Keyword);

public readonly record struct SeatPose(double X, double Y, double Z, double Yaw);

public static class FurnitureSlots
{
    public const uint SleepFlag = 0x8000_0000;
    private const int InteractionPoints = 22;
    private const byte SideBits = 0x1F;
    public const double MaxSeatDistance = 3.0;

    // Invariant: returns the first index with the smallest squared distance under MaxSeatDistance squared, else -1.
    public static int Nearest(IReadOnlyList<double> distancesSquared)
    {
        var best = -1;
        var nearest = MaxSeatDistance * MaxSeatDistance;
        for (var i = 0; i < distancesSquared.Count; i++)
        {
            if (distancesSquared[i] >= nearest) continue;
            nearest = distancesSquared[i];
            best = i;
        }
        return best;
    }

    // Requires: markerParams row i pairs with MNAM bit i and with the ENAM marker index i; bits 22..31 of MNAM are flags, not points.
    public static IReadOnlyList<FurnitureSlot> From(uint? markerFlags, IReadOnlyList<FurnitureMarkerParam> markerParams, IReadOnlyList<FurnitureMarkerSlot> disabledPoints)
    {
        var slots = new List<FurnitureSlot>();
        for (var i = 0; i < markerParams.Count; i++)
        {
            if (markerFlags is { } flags && (i >= InteractionPoints || (flags >> i & 1) == 0)) continue;
            var row = markerParams[i];
            var sides = (FurnitureSides)(row.EntryTypes & SideBits);
            foreach (var marker in disabledPoints)
                if (marker.Index == i && marker.DisabledEntryPoints is { } disabled)
                    sides &= ~(FurnitureSides)(disabled & 0xF);
            slots.Add(new FurnitureSlot(i, row.Offset.X, row.Offset.Y, row.Offset.Z, row.RotationZ, sides, row.Keyword));
        }
        return slots;
    }

    // Invariant: Godot yaw is minus the FO4 heading, matching Fo4Rotation.
    public static SeatPose Local(FurnitureSlot slot)
    {
        var k = WorldScale.Fo4ToGodot;
        return new SeatPose(slot.X * k, slot.Z * k, -slot.Y * k, -slot.Heading);
    }
}
