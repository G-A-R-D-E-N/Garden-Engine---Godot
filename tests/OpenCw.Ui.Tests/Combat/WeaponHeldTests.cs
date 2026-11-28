using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class WeaponHeldTests
{
    const string Gun = "000F01:Fallout4.esm";
    const string Scope = "000AAA:Fallout4.esm";
    static readonly WeaponInfo Pistol = new(9, "AMMO1", 6, 20, 0, 3000, 1, 0, 0.5, 2, 0);

    [Fact]
    public void AWeaponMovedToAContainerIsNoLongerEquipped()
    {
        var items = new PlayerItems();
        items.Add(Gun, 1);
        var controller = new WeaponController(items);
        controller.Equip(Gun, Pistol, 3);
        controller.Draw();
        var chest = new PlayerItems();
        items.MoveTo(chest, Gun, 1);

        controller.SyncHeld();

        Assert.Null(controller.WeaponKey);
        Assert.Equal(WeaponState.None, controller.State);
    }

    [Fact]
    public void AModAppliedToTheEquippedWeaponRefreshesItsState()
    {
        var items = new PlayerItems();
        items.Add(Gun, 1);
        var controller = new WeaponController(items);
        controller.Equip(Gun, Pistol, 3);
        var modded = new ItemState([Scope], 1.0, 0);
        items.Transform(Gun, ItemState.Plain, modded);

        controller.SyncHeld();

        Assert.Equal(Gun, controller.WeaponKey);
        Assert.Equal(modded, controller.Item);
    }

    [Fact]
    public void ARestoredWeaponThePlayerNoLongerHoldsIsNotEquipped()
    {
        var controller = new WeaponController(new PlayerItems());
        controller.Restore(new WeaponSave(Gun, 3, true, ItemState.Plain), _ => Pistol);
        Assert.Null(controller.WeaponKey);
    }
}
