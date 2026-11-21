using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ToastQueueTests
{
    static ToastOptions Exact(int maxVisible = 3, double minHold = 2, double maxHold = 8, double fadeIn = 0.25, double fadeOut = 0.5) => new()
    {
        MaxVisible = maxVisible,
        FadeIn = fadeIn,
        FadeOut = fadeOut,
        HoldBase = 0,
        HoldPerChar = 0,
        MinHold = minHold,
        MaxHold = maxHold,
    };

    [Theory]
    [InlineData(ToastKind.Error, 5)]
    [InlineData(ToastKind.Warning, 4)]
    [InlineData(ToastKind.Quest, 3)]
    [InlineData(ToastKind.Discovery, 2)]
    [InlineData(ToastKind.Success, 1)]
    [InlineData(ToastKind.Info, 0)]
    [InlineData((ToastKind)99, 0)]
    public void Each_kind_has_its_own_priority_rank(ToastKind kind, int expected)
    {
        Assert.Equal(expected, ToastQueue.PriorityOf(kind));
    }

    [Fact]
    public void The_display_text_only_shows_a_count_once_a_toast_has_merged()
    {
        Assert.Equal("Saved", new ToastView(1, "Saved", ToastKind.Info, 1, 1, 1).DisplayText);
        Assert.Equal("Saved (x3)", new ToastView(1, "Saved", ToastKind.Info, 3, 1, 1).DisplayText);
    }

    [Fact]
    public void Text_is_trimmed_and_toasts_of_a_different_kind_do_not_merge()
    {
        var queue = new ToastQueue(Exact());

        int first = queue.Enqueue("  hi  ", ToastKind.Info);
        int same = queue.Enqueue("hi", ToastKind.Info);
        int other = queue.Enqueue("hi", ToastKind.Warning);

        Assert.Equal(first, same);
        Assert.NotEqual(first, other);
        Assert.Equal(2, queue.Visible.Count);
        Assert.Equal("hi", queue.Visible[0].Text);
        Assert.Equal(2, queue.Visible[0].Count);
        Assert.Equal(1, queue.Visible[1].Count);
    }

    [Fact]
    public void A_duplicate_of_a_waiting_toast_merges_into_it_and_shows_the_count_when_it_is_promoted()
    {
        var queue = new ToastQueue(Exact(maxVisible: 1));
        queue.Enqueue("a", ToastKind.Info);
        int waiting = queue.Enqueue("b", ToastKind.Info);

        Assert.Equal(waiting, queue.Enqueue("b", ToastKind.Info));
        Assert.Equal(1, queue.PendingCount);

        queue.Tick(2.75);

        var shown = Assert.Single(queue.Visible);
        Assert.Equal(waiting, shown.Id);
        Assert.Equal(2, shown.Count);
        Assert.Equal("b (x2)", shown.DisplayText);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Merging_raises_the_waiting_toast_rank_so_it_pushes_out_a_lower_visible_one()
    {
        var queue = new ToastQueue(Exact(maxVisible: 1));
        queue.Enqueue("a", ToastKind.Info);
        queue.Enqueue("b", ToastKind.Info);
        Assert.Equal(0.0, queue.Visible[0].Alpha, 6);
        queue.Tick(0.125);
        Assert.Equal(0.5, queue.Visible[0].Alpha, 6);

        queue.Enqueue("b", ToastKind.Info, priority: 4);

        Assert.Equal("a", queue.Visible[0].Text);
        Assert.Equal(0.5, queue.Visible[0].Alpha, 6);
        queue.Tick(0.25);
        var shown = Assert.Single(queue.Visible);
        Assert.Equal("b", shown.Text);
        Assert.Equal(2, shown.Count);
    }

    [Fact]
    public void Dismissing_during_the_fade_in_continues_from_the_current_opacity()
    {
        var queue = new ToastQueue(Exact());
        int id = queue.Enqueue("a", ToastKind.Info);
        queue.Tick(0.125);
        Assert.Equal(0.5, queue.Visible[0].Alpha, 6);

        queue.Dismiss(id);

        Assert.Equal(0.5, queue.Visible[0].Alpha, 6);
        Assert.Equal(1.0, queue.Visible[0].Enter, 6);
        queue.Tick(0.125);
        Assert.Equal(0.25, queue.Visible[0].Alpha, 6);
        queue.Tick(0.125);
        Assert.Empty(queue.Visible);
    }

    [Fact]
    public void Dismissing_an_unknown_id_changes_nothing()
    {
        var queue = new ToastQueue(Exact());
        queue.Enqueue("a", ToastKind.Info);
        queue.Tick(0.25);

        queue.Dismiss(999);
        queue.Dismiss(-1);

        Assert.Single(queue.Visible);
        Assert.Equal(1.0, queue.Visible[0].Alpha, 6);
    }

    [Fact]
    public void With_no_fade_a_toast_is_opaque_at_once_and_a_dismissed_one_is_transparent_at_once()
    {
        var queue = new ToastQueue(Exact(maxVisible: 1, fadeIn: 0, fadeOut: 0));
        int id = queue.Enqueue("a", ToastKind.Info);

        Assert.Equal(1.0, queue.Visible[0].Alpha);
        Assert.Equal(1.0, queue.Visible[0].Enter);

        queue.Tick(0.5);
        Assert.Equal(1.0, queue.Visible[0].Alpha);

        queue.Dismiss(id);
        Assert.Equal(0.0, queue.Visible[0].Alpha);

        queue.Tick(0.125);
        Assert.Empty(queue.Visible);
    }

    [Fact]
    public void A_maximum_hold_below_the_minimum_still_holds_for_the_minimum()
    {
        var options = Exact(minHold: 3, maxHold: 1) with { HoldBase = 10 };
        var queue = new ToastQueue(options);
        queue.Enqueue("a", ToastKind.Info);

        queue.Tick(0.25);
        queue.Tick(2.75);
        Assert.Equal(1.0, queue.Visible[0].Alpha, 6);

        queue.Tick(0.25);
        Assert.Equal(1.0, queue.Visible[0].Alpha, 6);
        queue.Tick(0.25);
        Assert.Equal(0.5, queue.Visible[0].Alpha, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_explicit_hold_that_is_not_a_positive_finite_number_is_ignored(double hold)
    {
        var queue = new ToastQueue(Exact());
        queue.Enqueue("a", ToastKind.Info, holdSeconds: hold);

        queue.Tick(0.25);
        queue.Tick(1.5);

        Assert.Equal(1.0, queue.Visible[0].Alpha, 6);

        queue.Tick(0.5);
        queue.Tick(0.25);
        Assert.Equal(0.5, queue.Visible[0].Alpha, 6);
    }
}
