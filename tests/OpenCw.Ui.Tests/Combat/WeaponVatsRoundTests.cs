using System.Collections.Generic;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class WeaponVatsRoundTests
{
    private static WeaponInfo Gun() => new(9, "AMMO1", 6, 20, 0, 3000, 1, 0, 0.5, 2, 0, AmmoDamage: 5);

    private static WeaponController Held(int clip, bool draw = true)
    {
        var store = new PlayerItems();
        store.Add("AMMO1", 20);
        var c = new WeaponController(store);
        Assert.True(c.Equip("W1", Gun(), clip));
        if (draw)
            c.Draw();
        return c;
    }

    // Invariant: catches the VATS round seam taking a round from a holstered weapon.
    [Fact]
    public void ASheathedWeaponGivesNoRound()
    {
        var c = Held(3, draw: false);

        Assert.False(c.TryConsumeRound());
        Assert.Equal(3, c.Clip);
    }

    // Invariant: catches the VATS round seam taking a round while a reload is running, with rounds left in the clip.
    [Fact]
    public void AReloadingWeaponGivesNoRoundUntilTheReloadEnds()
    {
        var c = Held(2);
        Assert.True(c.Reload());

        Assert.False(c.TryConsumeRound());
        Assert.Equal(2, c.Clip);

        c.Update(2);

        Assert.True(c.TryConsumeRound());
        Assert.Equal(5, c.Clip);
    }

    // Invariant: catches the VATS round seam taking a round inside the cooldown of an ordinary shot.
    [Fact]
    public void AnAttackingWeaponGivesNoRoundUntilTheCooldownEnds()
    {
        var c = Held(3);
        Assert.Equal(FireResult.Fired, c.Fire());

        Assert.False(c.TryConsumeRound());
        Assert.Equal(2, c.Clip);

        c.Update(0.5);

        Assert.True(c.TryConsumeRound());
        Assert.Equal(1, c.Clip);
    }
}
