using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class OverlayWatchTests
{
    [Fact]
    public void Each_change_of_the_overlay_state_is_reported_once_in_both_directions()
    {
        var watch = new OverlayWatch();

        Assert.False(watch.Changed(false));
        Assert.True(watch.Changed(true));
        Assert.False(watch.Changed(true));
        Assert.True(watch.Changed(false));
        Assert.False(watch.Changed(false));
    }

    [Fact]
    public void A_direction_held_when_the_overlay_opens_does_not_repeat_after_the_reset_the_change_triggers()
    {
        var watch = new OverlayWatch();
        var driver = new MenuInputDriver();
        driver.Handle(MenuInputEvent.FromKey(MenuKey.Down, true));
        if (watch.Changed(true)) driver.Reset();

        Assert.Empty(driver.Advance(5.0));
    }
}
