using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class RadicleObjectModsTests
{
    private const string Damage = "000B01:Fallout4.esm";
    private const string Mag = "000B03:Fallout4.esm";

    private static uint F(float v) => BitConverter.SingleToUInt32Bits(v);

    private static OmodPropertyEntry Entry(byte type, byte fn, ushort prop, uint v1, uint v2 = 0, FormKey? form = null) =>
        new(type, fn, prop, v1, v2, 0f, form);

    private static WeaponInfo Rifle() => new(9, "AMMO1", 30, 20, 100, 3000, 1, 0, 0.5, 2, 0, AmmoDamage: 5);

    private static Func<FormKey, OmodPropertiesRecord?> Source(List<FormKey> asked) => key =>
    {
        asked.Add(key);
        return key.LocalId switch
        {
            0xB01 => new(key, [Entry(1, 1, WeaponProperty.AttackDamage, F(0.25f)), Entry(1, 2, 30, F(0.15f))]),
            0xB03 => new(key, [Entry(0, 2, WeaponProperty.AmmoCapacity, 10), Entry(4, 0, WeaponProperty.AmmoCapacity, 0x1234, 0, new FormKey("Fallout4.esm", 0x1234))]),
            0xB04 => throw new RadicleException(default, "truncated DATA"),
            _ => null,
        };
    };

    [Fact]
    public void RowsKeepTheRawSlotsAndAKeyIsPassedThrough()
    {
        var asked = new List<FormKey>();
        var rows = new RadicleObjectMods(Source(asked)).Properties("000b01:fallout4.esm")!;
        Assert.Equal(new FormKey("fallout4.esm", 0xB01), Assert.Single(asked));
        Assert.Equal(new ObjectModProperty(1, 1, WeaponProperty.AttackDamage, F(0.25f)), rows[0]);
        Assert.Equal(new ObjectModProperty(1, 2, 30, F(0.15f)), rows[1]);
        Assert.Equal(0.25f, rows[0].Float1);
    }

    [Fact]
    public void UnreadableKeysAndNonWinnersReadAsNoRowsAndOnlyAFailedReadIsNull()
    {
        var asked = new List<FormKey>();
        var mods = new RadicleObjectMods(Source(asked));
        Assert.Empty(mods.Properties("not a key")!);
        Assert.Empty(asked);
        Assert.Empty(mods.Properties("000B99:Fallout4.esm")!);
        Assert.Null(mods.Properties("000B04:Fallout4.esm"));
    }

    [Fact]
    public void FormValueRowsReachTheMathAsUnmappedAndAreIgnored()
    {
        var rows = new RadicleObjectMods(Source([])).Properties(Mag)!;
        var info = ObjectModMath.Apply(Rifle(), rows);
        Assert.Equal(40, info.Capacity);
    }

    [Fact]
    public void AModdedWeaponUsesTheStatsReadThroughTheRecords()
    {
        var store = new PlayerItems();
        store.Add("AMMO1", 50);
        var c = new WeaponController(store, mods: new RadicleObjectMods(Source([])));
        Assert.True(c.Equip("W", Rifle(), clip: 100, item: new ItemState([Mag, Damage, "000B99:Fallout4.esm"])));
        Assert.Equal(25, c.Weapon!.BaseDamage);
        Assert.Equal(40, c.Weapon.Capacity);
        Assert.Equal(40, c.Clip);
    }

    // Regression (finding 514): a mod whose read failed must not clamp the clip to the smaller fallback capacity while the read can still recover.
    [Fact]
    public void AModThatFailsToReadKeepsTheClipUntilTheRetriesRunOut()
    {
        var store = new PlayerItems();
        store.Add("AMMO1", 50);
        var notes = new List<string>();
        var c = new WeaponController(store, mods: new RadicleObjectMods(Source([])));
        c.Notice += notes.Add;
        Assert.True(c.Equip("W", Rifle(), clip: 100, item: new ItemState([Mag, "000B04:Fallout4.esm"])));
        Assert.Equal(40, c.Weapon!.Capacity);
        Assert.Equal(100, c.Clip);
        for (var i = 0; i < WeaponController.MaxModRetries - 1; i++) c.Update(WeaponController.RetrySeconds + 0.1);
        Assert.Equal(100, c.Clip);
        Assert.Empty(notes);
        c.Update(WeaponController.RetrySeconds + 0.1);
        Assert.Equal(40, c.Clip);
        Assert.Equal(110, store.CountOf("AMMO1"));
        Assert.Single(notes);
        c.Update(WeaponController.RetrySeconds + 0.1);
        Assert.Single(notes);
    }

    [Fact]
    public void ADetachedModRestoresTheStatsOnTheNextSync()
    {
        var store = new PlayerItems();
        store.Add("W", new ItemState([Damage]), 1);
        store.Add("AMMO1", 50);
        var c = new WeaponController(store, mods: new RadicleObjectMods(Source([])));
        c.Equip("W", Rifle(), clip: 5, item: new ItemState([Damage]));
        Assert.Equal(25, c.Weapon!.BaseDamage);
        store.Transform("W", new ItemState([Damage]), ItemState.Plain);
        c.SyncHeld();
        Assert.Equal(ItemState.Plain, c.Item);
        Assert.Equal(20, c.Weapon!.BaseDamage);
        Assert.Equal(5, c.Clip);
    }

    [Fact]
    public void AMagazineModDetachedClampsTheClip()
    {
        var store = new PlayerItems();
        store.Add("W", new ItemState([Mag]), 1);
        var c = new WeaponController(store, mods: new RadicleObjectMods(Source([])));
        c.Equip("W", Rifle(), clip: 40, item: new ItemState([Mag]));
        Assert.Equal(40, c.Clip);
        store.Transform("W", new ItemState([Mag]), ItemState.Plain);
        c.SyncHeld();
        Assert.Equal(30, c.Weapon!.Capacity);
        Assert.Equal(30, c.Clip);
    }
}
