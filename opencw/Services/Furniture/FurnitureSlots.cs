using Radicle.Interop;

namespace OpenCommonwealth.Services;

public sealed record FurnitureSlot(int Index, double X, double Y, double Z, double Heading, FurnitureMarkerSides Sides, FormKey? Keyword);

public readonly record struct SeatPose(double X, double Y, double Z, double Yaw);

public static class FurnitureSlots
{
    private static readonly int InteractionPoints = System.Numerics.BitOperations.PopCount((uint)FurnitureMarkerFlag.InteractionPointMask);
    private const FurnitureMarkerSides SideBits = FurnitureMarkerSides.Front | FurnitureMarkerSides.Rear | FurnitureMarkerSides.Right | FurnitureMarkerSides.Left | FurnitureMarkerSides.Other;
    private const ushort DisabledSideBits = (ushort)(FurnitureEntryTypes.Front | FurnitureEntryTypes.Behind | FurnitureEntryTypes.Right | FurnitureEntryTypes.Left);
    public const double MaxSeatDistance = 3.0;

    // Invariant: returns the first index with the smallest squared distance under MaxSeatDistance squared, else -1.
    public static int Nearest(IReadOnlyList<double> distancesSquared, Func<int, bool>? free = null)
    {
        var best = -1;
        var nearest = MaxSeatDistance * MaxSeatDistance;
        for (var i = 0; i < distancesSquared.Count; i++)
        {
            if (distancesSquared[i] >= nearest || free != null && !free(i)) continue;
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
            var sides = row.Sides & SideBits;
            foreach (var marker in disabledPoints)
                if (marker.Index == i && marker.DisabledEntryPoints is { } disabled)
                    sides &= ~(FurnitureMarkerSides)(disabled & DisabledSideBits);
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
