using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class WeaponRestoreTests
{
    const string Gun = "000F01:Fallout4.esm";
    const string Ammo = "AMMO1";
    const string Scope = "000AAA:Fallout4.esm";
    static readonly WeaponInfo Pistol = new(9, Ammo, 6, 20, 0, 3000, 1, 0, 0.5, 2, 0);

    sealed class Lookup
    {
        public WeaponInfo? Info { get; set; }

        public WeaponInfo? Of(string key) => Info;
    }

    sealed class Mods : IObjectModRecords
    {
        public bool Fail { get; set; }

        public IReadOnlyList<ObjectModProperty>? Properties(string key)
        {
            if (Fail) return null;
            return key == Scope ? new[] { new ObjectModProperty(0, 2, WeaponProperty.AmmoCapacity, 10) } : Array.Empty<ObjectModProperty>();
        }
    }

    // Regression (finding 513): a record that cannot be read at load must not erase the saved clip from the next save.
    [Fact]
    public void ASavedWeaponWhoseRecordCannotBeReadIsKeptCapturedAndRestoredOnceItReads()
    {
        var items = new PlayerItems();
        items.Add(Ammo, 20);
        items.Add(Gun, 1);
        var c = new WeaponController(items);
        var lookup = new Lookup();
        var notes = new List<string>();
        c.Notice += notes.Add;
        var save = new WeaponSave(Gun, 4, true, ItemState.Plain);

        Assert.Equal(WeaponRestore.Deferred, c.Restore(save, lookup.Of));
        Assert.Null(c.WeaponKey);
        Assert.Equal(save, c.Capture());
        Assert.Single(notes);

        c.Update(0.1);
        Assert.Equal(save, c.Capture());
        lookup.Info = Pistol;
        c.Update(1.0);
        Assert.Null(c.WeaponKey);
        c.Update(1.5);

        Assert.Equal(Gun, c.WeaponKey);
        Assert.Equal(4, c.Clip);
        Assert.True(c.IsDrawn);
        Assert.Equal(20, c.Reserve);
        Assert.Equal(new WeaponSave(Gun, 4, true, ItemState.Plain), c.Capture());
    }

    [Fact]
    public void ASavedWeaponNoLongerCarriedReturnsItsLoadedRoundsToTheReserve()
    {
        var items = new PlayerItems();
        items.Add(Ammo, 20);
        var c = new WeaponController(items);

        Assert.Equal(WeaponRestore.Dropped, c.Restore(new WeaponSave(Gun, 4, false, ItemState.Plain), _ => Pistol));

        Assert.Null(c.Capture());
        Assert.Equal(24, items.CountOf(Ammo));
    }

    [Fact]
    public void NothingSavedRestoresAsNothingAndClearsAPendingRestore()
    {
        var items = new PlayerItems();
        items.Add(Gun, 1);
        var c = new WeaponController(items);
        c.Restore(new WeaponSave(Gun, 4, false, ItemState.Plain), _ => null);
        Assert.NotNull(c.Capture());

        Assert.Equal(WeaponRestore.None, c.Restore(null, _ => null));
        Assert.Null(c.Capture());
    }

    [Fact]
    public void EquippingWaitsForAPendingRestoreAndThenKeepsItsClipOrRefundsIt()
    {
        var items = new PlayerItems();
        items.Add(Ammo, 20);
        items.Add(Gun, 1);
        var lookup = new Lookup();
        var c = new WeaponController(items);
        c.Restore(new WeaponSave(Gun, 4, false, ItemState.Plain), lookup.Of);

        Assert.False(c.Equip("OTHER", Pistol));
        Assert.Null(c.WeaponKey);

        lookup.Info = Pistol;
        Assert.True(c.Equip(Gun, Pistol));
        Assert.Equal(4, c.Clip);

        var other = new WeaponController(items);
        var again = new Lookup();
        other.Restore(new WeaponSave(Gun, 4, false, ItemState.Plain), again.Of);
        again.Info = Pistol;
        Assert.True(other.Equip("OTHER", Pistol));
        Assert.Equal("OTHER", other.WeaponKey);
        Assert.Equal(24, items.CountOf(Ammo));
    }

    // Regression (finding 514): an unreadable installed mod must not clamp the restored clip to the smaller fallback capacity.
    [Fact]
    public void ARestoredClipSurvivesAModThatIsUnreadableAtLoadAndTheStatsFillInOnRecovery()
    {
        var items = new PlayerItems();
        var extended = new ItemState([Scope], 1.0, 0);
        items.Add(Gun, extended, 1);
        items.Add(Ammo, 5);
        var mods = new Mods { Fail = true };
        var c = new WeaponController(items, mods: mods);
        var save = new WeaponSave(Gun, 14, false, extended);

        Assert.Equal(WeaponRestore.Restored, c.Restore(save, _ => Pistol));
        Assert.Equal(6, c.Weapon!.Capacity);
        Assert.Equal(14, c.Clip);
        Assert.Equal(save, c.Capture());

        mods.Fail = false;
        c.Update(0.1);

        Assert.Equal(16, c.Weapon.Capacity);
        Assert.Equal(14, c.Clip);
        Assert.Equal(5, items.CountOf(Ammo));
    }

    [Fact]
    public void AReadableModSetStillClampsTheClipToTheModdedCapacityAtOnce()
    {
        var items = new PlayerItems();
        items.Add(Gun, ItemState.Plain, 1);
        var c = new WeaponController(items, mods: new Mods());
        c.Equip(Gun, Pistol, clip: 9);
        Assert.Equal(6, c.Clip);
        c.Equip(Gun, Pistol, clip: 9, item: new ItemState([Scope], 1.0, 0));
        Assert.Equal(9, c.Clip);
    }

    [Fact]
    public void AMissingWeaponRecordStopsBlockingEquipsOnceTheWeaponIsGoneOrAfterEnoughMisses()
    {
        var items = new PlayerItems();
        var c = new WeaponController(items);
        var lookup = new Lookup();
        Assert.Equal(WeaponRestore.Dropped, c.Restore(new WeaponSave(Gun, 4, true, ItemState.Plain), lookup.Of));
        Assert.True(c.Equip(Gun, Pistol));

        items.Add(Gun, 1);
        var d = new WeaponController(items);
        Assert.Equal(WeaponRestore.Deferred, d.Restore(new WeaponSave(Gun, 4, true, ItemState.Plain), lookup.Of));
        var last = WeaponRestore.Deferred;
        for (var i = 0; i < 40 && last == WeaponRestore.Deferred; i++) last = d.ResolveDeferred();
        Assert.Equal(WeaponRestore.Dropped, last);
        Assert.True(d.Equip(Gun, Pistol));
    }
}
