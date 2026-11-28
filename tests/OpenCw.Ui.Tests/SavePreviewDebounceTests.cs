using OpenCommonwealth.Services.Ui.MainMenu;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SavePreviewDebounceTests
{
    [Fact]
    public void A_fresh_debounce_shows_nothing_and_has_nothing_pending()
    {
        var p = new SavePreviewDebounce();

        Assert.Equal(0.2, SavePreviewDebounce.DelaySeconds);
        Assert.Null(p.ShownId);
        Assert.False(p.Tick(5.0));
        Assert.False(p.Flush());
    }

    [Fact]
    public void The_preview_appears_exactly_at_the_delay_and_not_a_moment_before()
    {
        var early = new SavePreviewDebounce();
        early.Select("a");
        Assert.False(early.Tick(0.19999));
        Assert.Null(early.ShownId);

        var exact = new SavePreviewDebounce();
        exact.Select("a");
        Assert.False(exact.Tick(0.1));
        Assert.True(exact.Tick(0.1));
        Assert.Equal("a", exact.ShownId);

        var single = new SavePreviewDebounce();
        single.Select("a");
        Assert.True(single.Tick(5.0));
    }

    [Fact]
    public void Ticks_with_a_bad_delta_neither_show_the_preview_nor_count_towards_the_delay()
    {
        var p = new SavePreviewDebounce();
        p.Select("a");

        Assert.False(p.Tick(double.NaN));
        Assert.False(p.Tick(double.PositiveInfinity));
        Assert.False(p.Tick(double.NegativeInfinity));
        Assert.False(p.Tick(-1.0));
        Assert.False(p.Tick(0.0));
        Assert.Null(p.ShownId);

        Assert.False(p.Tick(0.1));
        Assert.True(p.Tick(0.1));
        Assert.Equal("a", p.ShownId);
    }

    [Fact]
    public void Selecting_another_id_or_the_same_pending_id_again_restarts_the_wait()
    {
        var other = new SavePreviewDebounce();
        other.Select("a");
        other.Tick(0.1);
        other.Select("b");
        Assert.False(other.Tick(0.1));
        Assert.True(other.Tick(0.1));
        Assert.Equal("b", other.ShownId);

        var same = new SavePreviewDebounce();
        same.Select("a");
        same.Tick(0.1);
        same.Select("a");
        Assert.False(same.Tick(0.1));
        Assert.True(same.Tick(0.1));
        Assert.Equal("a", same.ShownId);
    }

    [Fact]
    public void Selecting_nothing_while_a_preview_is_pending_drops_it()
    {
        var p = new SavePreviewDebounce();
        p.Select("a");

        p.Select(null);

        Assert.Null(p.ShownId);
        Assert.False(p.Tick(5.0));
        Assert.False(p.Flush());
    }

    [Fact]
    public void Moving_away_from_the_shown_preview_and_back_hides_it_until_the_delay_passes_again()
    {
        var p = new SavePreviewDebounce();
        p.Select("a");
        p.Flush();
        Assert.Equal("a", p.ShownId);

        p.Select("b");
        Assert.Null(p.ShownId);
        p.Select("a");

        Assert.Null(p.ShownId);
        Assert.True(p.Tick(0.2));
        Assert.Equal("a", p.ShownId);
    }

    [Fact]
    public void Once_shown_further_ticks_do_nothing_and_the_next_selection_waits_a_fresh_delay()
    {
        var p = new SavePreviewDebounce();
        p.Select("a");
        Assert.True(p.Tick(0.2));

        Assert.False(p.Tick(10.0));
        Assert.Equal("a", p.ShownId);

        p.Select("b");
        Assert.False(p.Tick(0.1));
        Assert.True(p.Tick(0.1));
        Assert.Equal("b", p.ShownId);
    }

    [Fact]
    public void Flush_shows_the_pending_id_without_waiting_and_a_later_tick_does_not_repeat_it()
    {
        var p = new SavePreviewDebounce();
        p.Select("a");

        Assert.True(p.Flush());
        Assert.Equal("a", p.ShownId);
        Assert.False(p.Tick(1.0));
    }
}
