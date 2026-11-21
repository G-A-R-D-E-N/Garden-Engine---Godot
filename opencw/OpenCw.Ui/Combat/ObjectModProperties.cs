using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Combat;

// Constraint: raw OMOD DATA property row; layout and enums in docs/code-notes/opencw-instance-items.md.
public sealed record ObjectModProperty(byte ValueType, byte Function, ushort Property, uint Value1, uint Value2 = 0)
{
    public const byte IntType = 0;
    public const byte FloatType = 1;
    public const byte BoolType = 2;

    public float Float1 => BitConverter.UInt32BitsToSingle(Value1);
}

public interface IObjectModRecords
{
    IReadOnlyList<ObjectModProperty>? Properties(string modKey);
}

public static class WeaponProperty
{
    public const ushort Reach = 1;
    public const ushort MaxRange = 3;
    public const ushort AmmoCapacity = 12;
    public const ushort IsAutomatic = 25;
    public const ushort AttackDamage = 28;
    public const ushort NumProjectiles = 51;
}

public static class ObjectModMath
{
    private const uint AutomaticFlag = 1u << 15;

    public static WeaponInfo Apply(WeaponInfo info, IEnumerable<ObjectModProperty> rows)
    {
        foreach (var row in rows)
        {
            info = row.Property switch
            {
                WeaponProperty.AttackDamage => info with { BaseDamage = Number(info.BaseDamage, row) },
                WeaponProperty.Reach => info with { Reach = Number(info.Reach, row) },
                WeaponProperty.MaxRange => info with { MaxRange = Number(info.MaxRange, row) },
                WeaponProperty.AmmoCapacity => info with { Capacity = ToInt(Number(info.Capacity, row)) },
                WeaponProperty.NumProjectiles => info with { ProjectileCount = ToInt(Number(info.ProjectileCount, row)) },
                WeaponProperty.IsAutomatic when row.ValueType == ObjectModProperty.BoolType => info with { Flags = Flag(info.Flags, AutomaticFlag, row) },
                _ => info,
            };
        }
        return info;
    }

    // Constraint: MUL+ADD is base * (1 + value1); value2 is not applied (see code notes).
    private static double Number(double current, ObjectModProperty row)
    {
        double value;
        switch (row.ValueType)
        {
            case ObjectModProperty.IntType:
                value = unchecked((int)row.Value1);
                if (row.Function == 1) return current;
                break;
            case ObjectModProperty.FloatType:
                value = row.Float1;
                if (row.Function == 1) return Clean(current * (1 + value));
                break;
            default:
                return current;
        }
        return row.Function switch { 0 => Clean(value), 2 => Clean(current + value), _ => current };
    }

    private static int ToInt(double v) => (int)Math.Min(int.MaxValue, Math.Round(v, MidpointRounding.AwayFromZero));

    private static double Clean(double v) => double.IsFinite(v) ? Math.Max(0, v) : 0;

    private static uint Flag(uint flags, uint bit, ObjectModProperty row)
    {
        var on = row.Value1 != 0;
        var now = (flags & bit) != 0;
        var next = row.Function switch { 0 => on, 1 => now && on, 2 => now || on, _ => now };
        return next ? flags | bit : flags & ~bit;
    }
}
