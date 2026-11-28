using OpenCommonwealth.Services.Ui.Flow;
using Xunit;

namespace OpenCw.Ui.Tests;

public class UiInputRouterTests
{
    static UiInputContext Idle() => new(false, true, false, false, false, false, false);

    static UiRoute Route(UiInputAction action, UiInputContext context) => UiInputRouter.Route(action, context);

    [Fact]
    public void Idle_game_opens_what_each_action_names()
    {
        Assert.Equal(UiRoute.OpenPause, Route(UiInputAction.Pause, Idle()));
        Assert.Equal(UiRoute.OpenGallery, Route(UiInputAction.Gallery, Idle()));
        Assert.Equal(UiRoute.OpenPipBoy, Route(UiInputAction.PipBoy, Idle()));
    }

    [Theory]
    [InlineData(UiInputAction.Pause)]
    [InlineData(UiInputAction.Gallery)]
    [InlineData(UiInputAction.PipBoy)]
    public void Headless_routes_nothing_even_when_idle(UiInputAction action)
    {
        Assert.Equal(UiRoute.None, Route(action, Idle() with { Headless = true }));
    }

    [Fact]
    public void Headless_does_not_close_an_open_pipboy()
    {
        Assert.Equal(UiRoute.None, Route(UiInputAction.PipBoy, Idle() with { Headless = true, MenuOpen = true, PipBoyOnTop = true }));
    }

    [Fact]
    public void Pipboy_needs_a_registered_player()
    {
        Assert.Equal(UiRoute.None, Route(UiInputAction.PipBoy, Idle() with { PlayerRegistered = false }));
    }

    [Fact]
    public void Pause_and_gallery_do_not_need_a_registered_player()
    {
        var context = Idle() with { PlayerRegistered = false };

        Assert.Equal(UiRoute.OpenPause, Route(UiInputAction.Pause, context));
        Assert.Equal(UiRoute.OpenGallery, Route(UiInputAction.Gallery, context));
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void Nothing_opens_while_a_menu_overlay_loading_screen_or_block_owns_input(bool menu, bool overlay, bool loading, bool blocked)
    {
        var context = Idle() with { MenuOpen = menu, ModalOverlayOpen = overlay, LoadingVisible = loading, Blocked = blocked };

        Assert.Equal(UiRoute.None, Route(UiInputAction.Pause, context));
        Assert.Equal(UiRoute.None, Route(UiInputAction.Gallery, context));
        Assert.Equal(UiRoute.None, Route(UiInputAction.PipBoy, context));
    }

    [Fact]
    public void Pipboy_key_closes_an_open_pipboy()
    {
        var context = Idle() with { MenuOpen = true, PipBoyOnTop = true };

        Assert.Equal(UiRoute.ClosePipBoy, Route(UiInputAction.PipBoy, context));
    }

    [Fact]
    public void Pipboy_key_closes_even_after_the_player_is_gone_or_loading_shows()
    {
        var context = Idle() with { MenuOpen = true, PipBoyOnTop = true, PlayerRegistered = false, LoadingVisible = true, Blocked = true };

        Assert.Equal(UiRoute.ClosePipBoy, Route(UiInputAction.PipBoy, context));
    }

    [Fact]
    public void Pipboy_key_leaves_an_overlay_above_the_pipboy_alone()
    {
        var context = Idle() with { MenuOpen = true, PipBoyOnTop = true, ModalOverlayOpen = true };

        Assert.Equal(UiRoute.None, Route(UiInputAction.PipBoy, context));
    }

    [Fact]
    public void Pause_and_gallery_never_open_over_the_pipboy_so_escape_stays_with_the_menu_back_action()
    {
        var context = Idle() with { MenuOpen = true, PipBoyOnTop = true };

        Assert.Equal(UiRoute.None, Route(UiInputAction.Pause, context));
        Assert.Equal(UiRoute.None, Route(UiInputAction.Gallery, context));
    }

    [Fact]
    public void Occupied_is_true_for_any_single_reason_and_false_when_idle()
    {
        Assert.False(Idle().Occupied);
        Assert.True((Idle() with { MenuOpen = true }).Occupied);
        Assert.True((Idle() with { ModalOverlayOpen = true }).Occupied);
        Assert.True((Idle() with { LoadingVisible = true }).Occupied);
        Assert.True((Idle() with { Blocked = true }).Occupied);
        Assert.True((Idle() with { PipBoyOnTop = true }).Occupied);
    }
}
