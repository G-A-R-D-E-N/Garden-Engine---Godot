using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Flow;
using Xunit;

namespace OpenCw.Ui.Tests;

public class UiInputRouterExhaustiveTests
{
    static IEnumerable<UiInputContext> AllContexts() =>
        Enumerable.Range(0, 128).Select(bits => new UiInputContext(
            (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0, (bits & 32) != 0, (bits & 64) != 0));

    [Fact]
    public void The_context_space_has_128_distinct_combinations()
    {
        Assert.Equal(128, AllContexts().Distinct().Count());
    }

    [Fact]
    public void Headless_routes_nothing_for_any_action_in_any_context()
    {
        foreach (var context in AllContexts().Where(c => c.Headless))
            foreach (var action in new[] { UiInputAction.Pause, UiInputAction.Gallery, UiInputAction.PipBoy })
                Assert.Equal(UiRoute.None, UiInputRouter.Route(action, context));
    }

    [Fact]
    public void Pause_and_gallery_open_exactly_when_nothing_else_owns_input_and_never_touch_the_pipboy()
    {
        foreach (var context in AllContexts().Where(c => !c.Headless))
        {
            Assert.Equal(context.Occupied ? UiRoute.None : UiRoute.OpenPause, UiInputRouter.Route(UiInputAction.Pause, context));
            Assert.Equal(context.Occupied ? UiRoute.None : UiRoute.OpenGallery, UiInputRouter.Route(UiInputAction.Gallery, context));
        }
    }

    [Fact]
    public void The_pipboy_action_only_ever_opens_or_closes_the_pipboy()
    {
        foreach (var context in AllContexts())
            Assert.Contains(UiInputRouter.Route(UiInputAction.PipBoy, context), new[] { UiRoute.None, UiRoute.OpenPipBoy, UiRoute.ClosePipBoy });
    }

    [Fact]
    public void The_pipboy_opens_only_for_a_registered_player_with_nothing_else_occupying_input()
    {
        var opens = AllContexts().Where(c => UiInputRouter.Route(UiInputAction.PipBoy, c) == UiRoute.OpenPipBoy).ToArray();

        Assert.Single(opens);
        Assert.Equal(new UiInputContext(false, true, false, false, false, false, false), opens[0]);
    }

    [Fact]
    public void The_pipboy_closes_in_exactly_the_non_headless_contexts_where_it_is_on_top_and_no_overlay_covers_it()
    {
        var closes = AllContexts().Where(c => UiInputRouter.Route(UiInputAction.PipBoy, c) == UiRoute.ClosePipBoy).ToArray();

        Assert.Equal(16, closes.Length);
        Assert.All(closes, c =>
        {
            Assert.False(c.Headless);
            Assert.True(c.PipBoyOnTop);
            Assert.False(c.ModalOverlayOpen);
        });
    }
}
