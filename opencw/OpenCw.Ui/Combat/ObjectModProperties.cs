using System;
using System.Collections.Generic;
using OmodPropertyEntry = Radicle.Interop.OmodPropertyEntry;
using OmodValueType = Radicle.Interop.OmodValueType;

namespace OpenCommonwealth.Services.Combat;

public interface IObjectModRecords
{
    IReadOnlyList<OmodPropertyEntry>? Properties(string modKey);
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

    public static WeaponInfo Apply(WeaponInfo info, IEnumerable<OmodPropertyEntry> rows)
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
                WeaponProperty.IsAutomatic when row.Kind == OmodValueType.Bool => info with { Flags = Flag(info.Flags, AutomaticFlag, row) },
                _ => info,
            };
        }
        return info;
    }

    // Constraint: MUL+ADD is base * (1 + value1); value2 is not applied (see code notes).
    private static double Number(double current, OmodPropertyEntry row)
    {
        double value;
        switch (row.Kind)
        {
            case OmodValueType.Int:
                value = row.IntValue!.Value;
                if (row.FunctionType == 1) return current;
                break;
            case OmodValueType.Float:
                value = row.FloatValue!.Value;
                if (row.FunctionType == 1) return Clean(current * (1 + value));
                break;
            default:
                return current;
        }
        return row.FunctionType switch { 0 => Clean(value), 2 => Clean(current + value), _ => current };
    }

    private static int ToInt(double v) => (int)Math.Min(int.MaxValue, Math.Round(v, MidpointRounding.AwayFromZero));

    private static double Clean(double v) => double.IsFinite(v) ? Math.Max(0, v) : 0;

    private static uint Flag(uint flags, uint bit, OmodPropertyEntry row)
    {
        var on = row.BoolValue == true;
        var now = (flags & bit) != 0;
        var next = row.FunctionType switch { 0 => on, 1 => now && on, 2 => now || on, _ => now };
        return next ? flags | bit : flags & ~bit;
    }
}
