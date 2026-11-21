using System;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PlayerLimbsFillTests
{
    [Fact]
    public void The_displayed_copy_shows_every_unreported_limb_as_full_and_the_store_stays_empty()
    {
        var limbs = new PlayerLimbs();

        var model = new PlayerStatusModel(new LivePlayerStatus(limbs: limbs, unreportedLimbsFull: true));

        Assert.Equal(Enum.GetValues<LimbKind>().Length, model.Condition.Count);
        Assert.Empty(limbs.All);
        Assert.Empty(PlayerVitalsSave.Sections(null, limbs));
    }

    [Fact]
    public void The_displayed_copy_keeps_a_reported_limb()
    {
        var limbs = new PlayerLimbs();
        limbs.Set(LimbKind.LeftLeg, 0, true);
        limbs.Set(LimbKind.Head, 0.4);
        var source = new LivePlayerStatus(limbs: limbs, unreportedLimbsFull: true);

        Assert.Equal(new LimbCondition(0, true), source.Limb(LimbKind.LeftLeg));
        Assert.Equal(new LimbCondition(0.4, false), source.Limb(LimbKind.Head));
        Assert.Equal(new LimbCondition(1, false), source.Limb(LimbKind.RightArm));
        Assert.Equal(2, limbs.All.Count);
    }

    [Fact]
    public void Without_the_fill_an_unreported_limb_stays_unknown()
    {
        var source = new LivePlayerStatus(limbs: new PlayerLimbs());

        Assert.Null(source.Limb(LimbKind.Head));
    }

    [Fact]
    public void Without_a_store_there_is_no_limb_even_with_the_fill()
    {
        Assert.Null(new LivePlayerStatus(unreportedLimbsFull: true).Limb(LimbKind.Head));
    }
}
