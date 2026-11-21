using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class ObjectModTests
{
    private const string Plain = "000A01:Fallout4.esm";
    private const string Damage = "000B01:Fallout4.esm";
    private const string Auto = "000B02:Fallout4.esm";
    private const string Mag = "000B03:Fallout4.esm";

    private static uint F(float v) => BitConverter.SingleToUInt32Bits(v);

    private static ObjectModProperty Row(byte type, byte fn, ushort prop, uint v1) => new(type, fn, prop, v1);

    private static WeaponInfo Rifle() => new(9, "AMMO1", 30, 20, 100, 3000, 1, 0, 0.5, 2, 0, AmmoDamage: 5);

    private sealed class Mods(Dictionary<string, ObjectModProperty[]> rows) : IObjectModRecords
    {
        public IReadOnlyList<ObjectModProperty>? Properties(string key) => rows.GetValueOrDefault(key);
    }

    private static readonly Dictionary<string, ObjectModProperty[]> Rows = new(StringComparer.OrdinalIgnoreCase)
    {
        [Damage] = [Row(1, 1, WeaponProperty.AttackDamage, F(0.25f))],
        [Auto] = [Row(2, 0, WeaponProperty.IsAutomatic, 1), Row(1, 1, WeaponProperty.AttackDamage, F(-0.5f)), Row(1, 1, 30, F(0.2f))],
        [Mag] = [Row(0, 2, WeaponProperty.AmmoCapacity, 10), Row(1, 1, WeaponProperty.MaxRange, F(0.5f))],
    };

    [Fact]
    public void MulAddScalesTheBaseByOnePlusValueAndIgnoresOtherProperties()
    {
        var info = ObjectModMath.Apply(Rifle(), Rows[Damage]);
        Assert.Equal(25, info.BaseDamage);
        Assert.Equal(Rifle() with { BaseDamage = 25 }, info);
        Assert.Equal(10, ObjectModMath.Apply(Rifle(), Rows[Auto]).BaseDamage);
    }

    [Fact]
    public void IntAddSetAndBoolSetApply()
    {
        var info = ObjectModMath.Apply(Rifle(), Rows[Mag]);
        Assert.Equal(40, info.Capacity);
        Assert.Equal(4500, info.MaxRange);
        var auto = ObjectModMath.Apply(Rifle(), Rows[Auto]);
        Assert.True(auto.Automatic);
        Assert.False(Rifle().Automatic);
        Assert.False(ObjectModMath.Apply(auto, [Row(2, 0, WeaponProperty.IsAutomatic, 0)]).Automatic);
        Assert.True(ObjectModMath.Apply(Rifle(), [Row(2, 2, WeaponProperty.IsAutomatic, 1)]).Automatic);
        Assert.False(ObjectModMath.Apply(auto, [Row(2, 1, WeaponProperty.IsAutomatic, 0)]).Automatic);
        Assert.Equal(7, ObjectModMath.Apply(Rifle(), [Row(0, 0, WeaponProperty.AmmoCapacity, 7)]).Capacity);
        Assert.Equal(25, ObjectModMath.Apply(Rifle(), [Row(0, 2, WeaponProperty.AmmoCapacity, unchecked((uint)-5))]).Capacity);
        Assert.Equal(int.MaxValue, ObjectModMath.Apply(Rifle(), [Row(1, 0, WeaponProperty.AmmoCapacity, F(3e9f))]).Capacity);
    }

    [Fact]
    public void ResultsClampAtZeroAndUnsupportedRowsAreSkipped()
    {
        Assert.Equal(0, ObjectModMath.Apply(Rifle(), [Row(1, 1, WeaponProperty.AttackDamage, F(-2f))]).BaseDamage);
        Assert.Equal(0, ObjectModMath.Apply(Rifle(), [Row(1, 0, WeaponProperty.AttackDamage, F(float.NaN))]).BaseDamage);
        Assert.Equal(Rifle(), ObjectModMath.Apply(Rifle(), [Row(0, 1, WeaponProperty.AttackDamage, 5)]));
        Assert.Equal(Rifle(), ObjectModMath.Apply(Rifle(), [Row(3, 0, WeaponProperty.AttackDamage, 5)]));
        Assert.Equal(Rifle(), ObjectModMath.Apply(Rifle(), [Row(1, 9, WeaponProperty.AttackDamage, F(5))]));
        Assert.Equal(Rifle(), ObjectModMath.Apply(Rifle(), [Row(1, 0, WeaponProperty.IsAutomatic, 1)]));
    }

    [Fact]
    public void RowsApplyInOrder()
    {
        var info = ObjectModMath.Apply(Rifle(), [Row(1, 2, WeaponProperty.AttackDamage, F(10f)), Row(1, 1, WeaponProperty.AttackDamage, F(0.5f))]);
        Assert.Equal(45, info.BaseDamage);
        var rev = ObjectModMath.Apply(Rifle(), [Row(1, 1, WeaponProperty.AttackDamage, F(0.5f)), Row(1, 2, WeaponProperty.AttackDamage, F(10f))]);
        Assert.Equal(40, rev.BaseDamage);
    }

    [Fact]
    public void ControllerReportsTheModdedStatsAndTheyReachTheAttack()
    {
        var store = new PlayerItems();
        store.Add("AMMO1", 50);
        var c = new WeaponController(store, mods: new Mods(Rows));
        var hits = new List<WeaponAttack>();
        c.Attacked += hits.Add;
        Assert.True(c.Equip("W", Rifle(), clip: 100, item: new ItemState([Mag, Damage])));
        Assert.Equal(40, c.Weapon!.Capacity);
        Assert.Equal(40, c.Clip);
        Assert.Equal(30, Rifle().Capacity);
        c.Draw();
        Assert.Equal(FireResult.Fired, c.Fire());
        Assert.Equal(25, hits[0].Damage);
        Assert.Equal(30, hits[0].TotalDamage);
        Assert.Equal(4500, hits[0].MaxRange);
        Assert.Equal(39, c.Clip);
    }

    [Fact]
    public void PlainItemsUnknownModsAndNoRecordsLeaveTheStatsAlone()
    {
        var c = new WeaponController(new PlayerItems(), mods: new Mods(Rows));
        c.Equip("W", Rifle());
        Assert.Equal(Rifle(), c.Weapon);
        c.Equip("W", Rifle(), item: new ItemState([Plain]));
        Assert.Equal(Rifle(), c.Weapon);
        var bare = new WeaponController(new PlayerItems());
        bare.Equip("W", Rifle(), item: new ItemState([Damage]));
        Assert.Equal(Rifle(), bare.Weapon);
        c.Equip("W", Rifle(), item: new ItemState([Damage]));
        c.Equip("W", Rifle());
        Assert.Equal(20, c.Weapon!.BaseDamage);
    }
}
