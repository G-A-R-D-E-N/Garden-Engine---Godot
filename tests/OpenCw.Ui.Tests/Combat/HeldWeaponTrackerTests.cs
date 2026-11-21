using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class HeldWeaponTrackerTests
{
    [Fact]
    public void DrawingAnEquippedWeaponShowsItAndSheathingHidesIt()
    {
        var tracker = new HeldWeaponTracker();
        Assert.False(tracker.Update("000001:Test.esm", false));
        Assert.True(tracker.Update("000001:Test.esm", true));
        Assert.Equal("000001:Test.esm", tracker.Shown);
        Assert.False(tracker.Update("000001:test.esm", true));
        Assert.True(tracker.Update("000001:Test.esm", false));
        Assert.Null(tracker.Shown);
    }

    [Fact]
    public void SwappingTheDrawnWeaponChangesTheVisualAndUnequippingClearsIt()
    {
        var tracker = new HeldWeaponTracker();
        tracker.Update("000001:Test.esm", true);
        Assert.True(tracker.Update("000002:Test.esm", true));
        Assert.Equal("000002:Test.esm", tracker.Shown);
        Assert.True(tracker.Update(null, true));
        Assert.Null(tracker.Shown);
        Assert.False(tracker.Update("", true));
    }

    [Fact]
    public void AResetShowsAnAlreadyDrawnWeaponAgain()
    {
        var tracker = new HeldWeaponTracker();
        tracker.Update("000001:Test.esm", true);
        tracker.Reset();
        Assert.True(tracker.Update("000001:Test.esm", true));
    }
}
