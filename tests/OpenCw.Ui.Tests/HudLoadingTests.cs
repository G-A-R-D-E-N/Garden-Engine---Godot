using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudLoadingTests
{
    private static readonly string[] Tips = ["a", "b", "c"];

    [Fact]
    public void BeginShowsTheScreenAtZeroProgress()
    {
        var model = new LoadingModel();
        Assert.False(model.IsVisible);
        Assert.Equal(LoadingPhase.Idle, model.Phase);

        model.Begin("  Loading world ");
        Assert.True(model.IsVisible);
        Assert.Equal("Loading world", model.Title);
        Assert.Equal(0.0, model.Progress, 6);
    }

    [Fact]
    public void ProgressOnlyMovesForwardAndIsClamped()
    {
        var model = new LoadingModel();
        model.Begin("x");
        model.SetProgress(0.4);
        model.SetProgress(0.2);
        Assert.Equal(0.4, model.Progress, 6);
        model.SetProgress(double.NaN);
        Assert.Equal(0.4, model.Progress, 6);
        model.SetProgress(7);
        Assert.Equal(1.0, model.Progress, 6);
        Assert.True(model.IsVisible);
    }

    [Fact]
    public void CompletingEarlyWaitsForTheMinimumDisplayTime()
    {
        var model = new LoadingModel(minDisplaySeconds: 2);
        int finished = 0;
        model.Begin("x", onFinished: () => finished++);
        model.Complete();
        Assert.True(model.IsVisible);
        Assert.True(model.WorkComplete);
        Assert.Equal(1.0, model.Progress, 6);

        model.Tick(1);
        Assert.True(model.IsVisible);
        Assert.Equal(0, finished);

        model.Tick(1);
        Assert.False(model.IsVisible);
        Assert.Equal(LoadingPhase.Finished, model.Phase);
        Assert.Equal(1, finished);

        model.Tick(10);
        Assert.Equal(1, finished);
    }

    [Fact]
    public void CompletingAfterTheMinimumFinishesImmediately()
    {
        var model = new LoadingModel(minDisplaySeconds: 1);
        int finished = 0;
        model.Begin("x", onFinished: () => finished++);
        model.Tick(3);
        Assert.True(model.IsVisible);
        model.Complete();
        Assert.Equal(LoadingPhase.Finished, model.Phase);
        Assert.Equal(1, finished);
    }

    [Fact]
    public void CancelHidesImmediatelyAndSuppressesTheCallback()
    {
        var model = new LoadingModel(minDisplaySeconds: 0);
        int finished = 0;
        model.Begin("x", onFinished: () => finished++);
        Assert.True(model.Cancel());
        Assert.Equal(LoadingPhase.Cancelled, model.Phase);
        Assert.False(model.IsVisible);

        model.Complete();
        model.SetProgress(0.9);
        model.Tick(10);
        Assert.Equal(LoadingPhase.Cancelled, model.Phase);
        Assert.Equal(0.0, model.Progress, 6);
        Assert.Equal(0, finished);
        Assert.False(model.Cancel());
    }

    [Fact]
    public void CancelAfterFinishChangesNothing()
    {
        var model = new LoadingModel(minDisplaySeconds: 0);
        model.Begin("x");
        model.Complete();
        Assert.Equal(LoadingPhase.Finished, model.Phase);
        Assert.False(model.Cancel());
        Assert.Equal(LoadingPhase.Finished, model.Phase);
    }

    [Fact]
    public void CancelWhileIdleIsHarmless()
    {
        var model = new LoadingModel();
        Assert.False(model.Cancel());
        model.Complete();
        model.Tick(1);
        Assert.Equal(LoadingPhase.Idle, model.Phase);
    }

    [Fact]
    public void BeginAgainRestartsAndDropsTheOldCallback()
    {
        var model = new LoadingModel(minDisplaySeconds: 0);
        int first = 0;
        int second = 0;
        model.Begin("one", onFinished: () => first++);
        model.SetProgress(0.8);
        model.Begin("two", onFinished: () => second++);
        Assert.Equal(0.0, model.Progress, 6);
        Assert.Equal("two", model.Title);
        model.Complete();
        Assert.Equal(0, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public void TipsRotateOnATimerAndWrap()
    {
        var model = new LoadingModel(tipSeconds: 4);
        model.Begin("x", Tips, firstTip: 1);
        Assert.Equal("b", model.Tip);

        model.Tick(3.5);
        Assert.Equal("b", model.Tip);
        model.Tick(0.5);
        Assert.Equal("c", model.Tip);

        model.Tick(8);
        Assert.Equal("b", model.Tip);
    }

    [Fact]
    public void NegativeFirstTipCountsFromTheEnd()
    {
        var model = new LoadingModel();
        model.Begin("x", Tips, firstTip: -1);
        Assert.Equal("c", model.Tip);
    }

    [Fact]
    public void BlankTipsAreIgnoredAndNoTipsMeansEmptyText()
    {
        var model = new LoadingModel();
        model.Begin("x", ["", "  ", " keep "]);
        Assert.Equal("keep", model.Tip);
        model.NextTip();
        Assert.Equal("keep", model.Tip);

        model.Begin("x");
        Assert.Empty(model.Tip);
        model.Tick(100);
        Assert.Empty(model.Tip);
    }

    [Fact]
    public void InfiniteStepDoesNotBreakTipRotation()
    {
        var model = new LoadingModel(tipSeconds: 4);
        model.Begin("x", Tips);
        model.NextTip();
        model.Tick(double.PositiveInfinity);
        model.Tick(double.NaN);
        Assert.Equal("b", model.Tip);
        Assert.Equal(0.0, model.Elapsed, 6);
        Assert.True(model.IsVisible);
    }

    [Fact]
    public void NextTipAdvancesManually()
    {
        var model = new LoadingModel();
        model.Begin("x", Tips);
        model.NextTip();
        Assert.Equal("b", model.Tip);
        model.NextTip();
        model.NextTip();
        Assert.Equal("a", model.Tip);
    }
}
