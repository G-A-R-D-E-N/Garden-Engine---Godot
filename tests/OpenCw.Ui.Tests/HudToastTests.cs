using System.Linq;
using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudToastTests
{
    private static ToastOptions Exact(int maxVisible = 3, int maxPending = 12) => new()
    {
        MaxVisible = maxVisible,
        MaxPending = maxPending,
        FadeIn = 0.25,
        FadeOut = 0.5,
        HoldBase = 0,
        HoldPerChar = 0,
        MinHold = 2,
        MaxHold = 8,
    };

    private static double Lifetime(string text, ToastKind kind, ToastOptions options)
    {
        var queue = new ToastQueue(options);
        queue.Show(text, kind);
        double elapsed = 0;
        while (queue.Visible.Count > 0 && elapsed < 100)
        {
            queue.Tick(0.125);
            elapsed += 0.125;
        }
        return elapsed;
    }

    [Fact]
    public void ShowIsVisibleImmediatelyAndFadesIn()
    {
        var queue = new ToastQueue(Exact());
        queue.Show("Saved");
        var toast = Assert.Single(queue.Visible);
        Assert.Equal("Saved", toast.Text);
        Assert.Equal(ToastKind.Info, toast.Kind);
        Assert.Equal(1, toast.Count);
        Assert.Equal(0.0, toast.Alpha, 6);

        queue.Tick(0.125);
        Assert.Equal(0.5, queue.Visible[0].Alpha, 6);
        Assert.Equal(0.5, queue.Visible[0].Enter, 6);
    }

    [Fact]
    public void LifecycleIsFadeInHoldFadeOutThenGone()
    {
        var queue = new ToastQueue(Exact());
        queue.Show("Saved");
        queue.Tick(0.25);
        Assert.Equal(1.0, queue.Visible[0].Alpha, 6);
        Assert.Equal(1.0, queue.Visible[0].Enter, 6);

        queue.Tick(2);
        Assert.Equal(1.0, queue.Visible[0].Alpha, 6);

        queue.Tick(0.25);
        Assert.Equal(0.5, queue.Visible[0].Alpha, 6);

        queue.Tick(0.25);
        Assert.Empty(queue.Visible);
    }

    [Fact]
    public void ALargeStepCarriesThroughEveryPhase()
    {
        var queue = new ToastQueue(Exact());
        queue.Show("Saved");
        queue.Tick(100);
        Assert.Empty(queue.Visible);
    }

    [Fact]
    public void DuplicatesMergeIntoOneToastWithACount()
    {
        var queue = new ToastQueue(Exact());
        queue.Show("Saved");
        queue.Show("Saved");
        queue.Show("Saved");
        var toast = Assert.Single(queue.Visible);
        Assert.Equal(3, toast.Count);
        Assert.Equal("Saved (x3)", toast.DisplayText);

        queue.Show("Saved", ToastKind.Warning);
        Assert.Equal(2, queue.Visible.Count);
    }

    [Fact]
    public void MergingRestartsTheHoldTime()
    {
        var queue = new ToastQueue(Exact());
        queue.Show("Saved");
        queue.Tick(0.25);
        queue.Tick(1.5);
        queue.Show("Saved");
        queue.Tick(1.5);
        Assert.Equal(1.0, Assert.Single(queue.Visible).Alpha, 6);
    }

    [Fact]
    public void MergingBringsAFadingToastBack()
    {
        var queue = new ToastQueue(Exact());
        queue.Show("Saved");
        queue.Tick(0.25);
        queue.Tick(2);
        queue.Tick(0.25);
        Assert.Equal(0.5, queue.Visible[0].Alpha, 6);

        queue.Show("Saved");
        Assert.Equal(1.0, queue.Visible[0].Alpha, 6);
    }

    [Fact]
    public void ExtraToastsWaitForASlotInOrder()
    {
        var queue = new ToastQueue(Exact(maxVisible: 3));
        foreach (var text in new[] { "a", "b", "c", "d", "e" }) queue.Show(text);
        Assert.Equal(new[] { "a", "b", "c" }, queue.Visible.Select(t => t.Text).ToArray());
        Assert.Equal(2, queue.PendingCount);

        queue.Tick(100);
        Assert.Equal(new[] { "d", "e" }, queue.Visible.Select(t => t.Text).ToArray());
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void HigherPriorityPendingToastsGoFirstAndEvictTheLowestVisible()
    {
        var queue = new ToastQueue(Exact(maxVisible: 1));
        queue.Show("info");
        queue.Show("a");
        queue.Show("boom", ToastKind.Error);
        Assert.Equal(2, queue.PendingCount);

        queue.Tick(0.01);
        Assert.Equal("boom", Assert.Single(queue.Visible).Text);
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public void EqualOrHigherPriorityVisibleToastsAreNotEvicted()
    {
        var queue = new ToastQueue(Exact(maxVisible: 1));
        queue.Show("first", ToastKind.Error);
        queue.Show("second", ToastKind.Error);
        queue.Tick(0.25);
        queue.Tick(0.75);
        var toast = Assert.Single(queue.Visible);
        Assert.Equal("first", toast.Text);
        Assert.Equal(1.0, toast.Alpha, 6);
    }

    [Fact]
    public void PendingOverflowDropsTheOldestLowestPriorityToast()
    {
        var queue = new ToastQueue(Exact(maxVisible: 1, maxPending: 2));
        foreach (var text in new[] { "a", "b", "c", "d" }) queue.Show(text);
        Assert.Equal(2, queue.PendingCount);

        queue.Tick(100);
        Assert.Equal("c", Assert.Single(queue.Visible).Text);
    }

    [Fact]
    public void ZeroPendingStillShowsToastsWhenThereIsRoom()
    {
        var queue = new ToastQueue(Exact(maxVisible: 2, maxPending: 0));
        queue.Show("a");
        queue.Show("b");
        queue.Show("c");
        Assert.Equal(2, queue.Visible.Count);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void BlankTextIsRejected()
    {
        var queue = new ToastQueue(Exact());
        Assert.Equal(-1, queue.Enqueue("   ", ToastKind.Info));
        queue.Show("");
        Assert.Empty(queue.Visible);
        Assert.Equal(0, queue.PendingCount);

        queue.Show("  padded  ");
        Assert.Equal("padded", queue.Visible[0].Text);
    }

    [Fact]
    public void HoldScalesWithLengthAndKind()
    {
        var options = new ToastOptions
        {
            FadeIn = 0,
            FadeOut = 0,
            HoldBase = 0,
            HoldPerChar = 0.25,
            MinHold = 1,
            MaxHold = 3,
        };
        Assert.Equal(1.0, Lifetime("abcd", ToastKind.Info, options), 6);
        Assert.Equal(2.0, Lifetime("abcdefgh", ToastKind.Info, options), 6);
        Assert.Equal(1.5, Lifetime("abcd", ToastKind.Warning, options), 6);
        Assert.Equal(3.0, Lifetime(new string('x', 40), ToastKind.Info, options), 6);
    }

    [Fact]
    public void ExplicitHoldOverridesTheEstimate()
    {
        var queue = new ToastQueue(Exact());
        queue.Enqueue("long wait", ToastKind.Info, holdSeconds: 6);
        queue.Tick(0.25);
        queue.Tick(5.5);
        Assert.Equal(1.0, Assert.Single(queue.Visible).Alpha, 6);
        queue.Tick(0.5);
        queue.Tick(0.5);
        Assert.Empty(queue.Visible);
    }

    [Fact]
    public void DismissFadesAVisibleToastAndDropsAPendingOne()
    {
        var queue = new ToastQueue(Exact(maxVisible: 1));
        int first = queue.Enqueue("first", ToastKind.Info);
        int second = queue.Enqueue("second", ToastKind.Info);
        queue.Dismiss(second);
        Assert.Equal(0, queue.PendingCount);

        queue.Tick(0.25);
        queue.Dismiss(first);
        queue.Tick(0.25);
        Assert.Equal(0.5, queue.Visible[0].Alpha, 6);
        queue.Tick(0.25);
        Assert.Empty(queue.Visible);
    }

    [Fact]
    public void ClearRemovesEverything()
    {
        var queue = new ToastQueue(Exact(maxVisible: 1));
        queue.Show("a");
        queue.Show("b");
        queue.Clear();
        Assert.Empty(queue.Visible);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void KindsRankErrorsAboveInfo()
    {
        Assert.True(ToastQueue.PriorityOf(ToastKind.Error) > ToastQueue.PriorityOf(ToastKind.Warning));
        Assert.True(ToastQueue.PriorityOf(ToastKind.Warning) > ToastQueue.PriorityOf(ToastKind.Quest));
        Assert.True(ToastQueue.PriorityOf(ToastKind.Quest) > ToastQueue.PriorityOf(ToastKind.Info));
    }

    [Fact]
    public void ConstructorRejectsBadOptions()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new ToastQueue(new ToastOptions { MaxVisible = 0 }));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new ToastQueue(new ToastOptions { MaxPending = -1 }));
    }
}
