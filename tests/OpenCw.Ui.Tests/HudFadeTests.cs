using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudFadeTests
{
    [Fact]
    public void FadeOutReachesBlackOnTimeAndFiresTheCallbackOnce()
    {
        var fade = new FadeController();
        int blackCalls = 0;
        fade.FadeOut(1, () => blackCalls++);

        fade.Tick(0.5);
        Assert.Equal(0.5, fade.Alpha, 6);
        Assert.Equal(0, blackCalls);
        Assert.False(fade.IsBlack);

        fade.Tick(0.5);
        Assert.Equal(1.0, fade.Alpha, 6);
        Assert.True(fade.IsBlack);
        Assert.Equal(1, blackCalls);

        fade.Tick(5);
        Assert.Equal(1, blackCalls);
        Assert.False(fade.IsBusy);
    }

    [Fact]
    public void BlinkRunsOutHoldInInOrder()
    {
        var fade = new FadeController();
        var events = new List<string>();
        fade.Blink(1, 0.5, 1, () => events.Add("black"), () => events.Add("clear"));

        fade.Tick(1);
        Assert.Equal(new[] { "black" }, events);
        Assert.True(fade.IsBlack);

        fade.Tick(0.25);
        Assert.True(fade.IsBlack);
        Assert.Single(events);

        fade.Tick(0.25);
        Assert.True(fade.IsBlack);

        fade.Tick(0.5);
        Assert.Equal(0.5, fade.Alpha, 6);
        Assert.Single(events);

        fade.Tick(0.5);
        Assert.Equal(new[] { "black", "clear" }, events);
        Assert.True(fade.IsClear);
        Assert.False(fade.IsBusy);
    }

    [Fact]
    public void OneBigStepRunsTheWholeQueueInOrder()
    {
        var fade = new FadeController();
        var events = new List<string>();
        fade.Blink(1, 1, 1, () => events.Add("black"), () => events.Add("clear"));
        fade.Tick(10);
        Assert.Equal(new[] { "black", "clear" }, events);
        Assert.True(fade.IsClear);
    }

    [Fact]
    public void QueuedFadesCarryLeftoverTimeIntoTheNextStep()
    {
        var fade = new FadeController();
        fade.FadeOut(1);
        fade.FadeIn(1);
        fade.Tick(1.5);
        Assert.Equal(0.5, fade.Alpha, 6);
    }

    [Fact]
    public void ACallbackMayQueueMoreWork()
    {
        var fade = new FadeController();
        fade.FadeOut(1, () => fade.FadeIn(1));
        fade.Tick(1.25);
        Assert.Equal(0.75, fade.Alpha, 6);
        fade.Tick(1);
        Assert.True(fade.IsClear);
    }

    [Fact]
    public void ACallbackMayCancelTheRest()
    {
        var fade = new FadeController();
        var cleared = false;
        fade.FadeOut(1, () => fade.Cancel());
        fade.FadeIn(1, () => cleared = true);
        fade.Tick(5);
        Assert.False(cleared);
        Assert.True(fade.IsBlack);
        Assert.False(fade.IsBusy);
    }

    [Fact]
    public void PartialFadesTakeProportionalTime()
    {
        var fade = new FadeController(0.5);
        fade.FadeIn(1);
        fade.Tick(0.25);
        Assert.Equal(0.25, fade.Alpha, 6);
        fade.Tick(0.25);
        Assert.True(fade.IsClear);
    }

    [Fact]
    public void CutJumpsAndDropsQueuedWorkWithoutCallbacks()
    {
        var fade = new FadeController();
        int calls = 0;
        fade.FadeOut(1, () => calls++);
        fade.Cut(true);
        Assert.True(fade.IsBlack);
        Assert.False(fade.IsBusy);
        fade.Tick(5);
        Assert.Equal(0, calls);

        fade.Cut(false);
        Assert.True(fade.IsClear);
    }

    [Fact]
    public void CancelKeepsTheCurrentAlpha()
    {
        var fade = new FadeController();
        fade.FadeOut(1);
        fade.Tick(0.25);
        fade.Cancel();
        fade.Tick(5);
        Assert.Equal(0.25, fade.Alpha, 6);
    }

    [Fact]
    public void ZeroLengthFadesCompleteOnTheNextTick()
    {
        var fade = new FadeController();
        int calls = 0;
        fade.FadeOut(0, () => calls++);
        fade.Tick(0);
        Assert.True(fade.IsBlack);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FadeOutWhenAlreadyBlackStillFiresTheCallback()
    {
        var fade = new FadeController(1);
        int calls = 0;
        fade.FadeOut(1, () => calls++);
        fade.Tick(0);
        Assert.Equal(1, calls);
        Assert.True(fade.IsBlack);
    }

    [Fact]
    public void HoldOnlyWaits()
    {
        var fade = new FadeController();
        int calls = 0;
        fade.Hold(1, () => calls++);
        fade.Tick(0.75);
        Assert.Equal(0, calls);
        Assert.True(fade.IsBusy);
        fade.Tick(0.25);
        Assert.Equal(1, calls);
        Assert.True(fade.IsClear);
    }

    [Fact]
    public void BadDurationsAndStepsAreTreatedAsZero()
    {
        var fade = new FadeController();
        fade.FadeOut(double.NaN);
        fade.Tick(double.NaN);
        Assert.True(fade.IsBlack);

        fade.FadeIn(-3);
        fade.Tick(-1);
        Assert.True(fade.IsClear);
    }
}
