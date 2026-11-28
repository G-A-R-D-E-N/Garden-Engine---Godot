using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCommonwealth.Services.Papyrus.Ui;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class UiBoxSchedulerTests
{
    private sealed class Probe
    {
        public List<string> Presented { get; } = new();

        public int Closes { get; private set; }

        public bool Busy { get; set; }

        public UiBoxScheduler Scheduler { get; }

        public Probe() => Scheduler = new UiBoxScheduler(Present, () => Closes++);

        private bool Present(UiBoxRequest request)
        {
            if (Busy) return false;
            Presented.Add(request.Text);
            return true;
        }
    }

    private static UiBoxRequest Request(string text, List<int>? answers = null, CancellationToken cancelled = default)
    {
        Action<int>? answer = null;
        if (answers is not null) answer = answers.Add;
        return new UiBoxRequest(text, answer, cancelled);
    }

    [Fact]
    public void BoxesShowOneAtATimeInArrivalOrder()
    {
        var probe = new Probe();
        var first = Request("a");
        var second = Request("b");
        var third = Request("c");
        foreach (var request in new[] { first, second, third }) Assert.True(probe.Scheduler.Enqueue(request));

        probe.Scheduler.Pump();
        probe.Scheduler.Pump();
        Assert.Equal(new[] { "a" }, probe.Presented);
        Assert.Equal(2, probe.Scheduler.Queued);

        probe.Scheduler.Answered(first, 0);
        probe.Scheduler.Pump();
        Assert.Equal(new[] { "a", "b" }, probe.Presented);

        probe.Scheduler.Answered(second, 0);
        probe.Scheduler.Pump();
        Assert.Equal(new[] { "a", "b", "c" }, probe.Presented);
        Assert.Same(third, probe.Scheduler.Active);
        Assert.Equal(0, probe.Scheduler.Queued);
    }

    [Fact]
    public void ABusyControlKeepsTheRequestAtTheFrontUntilItIsFree()
    {
        var probe = new Probe { Busy = true };
        probe.Scheduler.Enqueue(Request("a"));
        probe.Scheduler.Enqueue(Request("b"));

        probe.Scheduler.Pump();
        probe.Scheduler.Pump();
        Assert.Empty(probe.Presented);
        Assert.Null(probe.Scheduler.Active);
        Assert.Equal(2, probe.Scheduler.Queued);

        probe.Busy = false;
        probe.Scheduler.Pump();

        Assert.Equal(new[] { "a" }, probe.Presented);
        Assert.Equal(1, probe.Scheduler.Queued);
    }

    [Fact]
    public void AnAnswerReachesItsRequestOnceAndAStaleAnswerIsIgnored()
    {
        var probe = new Probe();
        var answers = new List<int>();
        var request = Request("a", answers);
        probe.Scheduler.Enqueue(request);
        probe.Scheduler.Pump();

        probe.Scheduler.Answered(request, 3);
        probe.Scheduler.Answered(request, 4);
        probe.Scheduler.Answered(Request("other", answers), 5);

        Assert.Equal(new[] { 3 }, answers);
    }

    [Fact]
    public void ACloseWithoutAnAnswerDismissesTheRequestOnTheNextPump()
    {
        var probe = new Probe();
        var answers = new List<int>();
        var request = Request("a", answers);
        probe.Scheduler.Enqueue(request);
        probe.Scheduler.Enqueue(Request("b"));
        probe.Scheduler.Pump();

        probe.Scheduler.NoteClosed();
        Assert.Empty(answers);
        probe.Scheduler.Pump();

        Assert.Equal(new[] { UiServices.Dismissed }, answers);
        Assert.Equal(new[] { "a", "b" }, probe.Presented);
        probe.Scheduler.Answered(request, 2);
        Assert.Equal(new[] { UiServices.Dismissed }, answers);
    }

    [Fact]
    public void ACloseThatCarriesAnAnswerInTheSameCallStackIsNotADismissal()
    {
        var probe = new Probe();
        var answers = new List<int>();
        var request = Request("a", answers);
        probe.Scheduler.Enqueue(request);
        probe.Scheduler.Pump();

        probe.Scheduler.NoteClosed();
        probe.Scheduler.Answered(request, 2);
        probe.Scheduler.Pump();

        Assert.Equal(new[] { 2 }, answers);
    }

    [Fact]
    public void ACloseWithNothingOpenIsIgnored()
    {
        var probe = new Probe();
        probe.Scheduler.NoteClosed();
        var answers = new List<int>();
        probe.Scheduler.Enqueue(Request("a", answers));

        probe.Scheduler.Pump();
        probe.Scheduler.Pump();

        Assert.Empty(answers);
        Assert.Equal(new[] { "a" }, probe.Presented);
    }

    [Fact]
    public void ACancelledQueuedRequestIsSkippedAndNeverAnswered()
    {
        var probe = new Probe();
        using var source = new CancellationTokenSource();
        var answers = new List<int>();
        var first = Request("a");
        probe.Scheduler.Enqueue(first);
        probe.Scheduler.Enqueue(Request("b", answers, source.Token));
        probe.Scheduler.Enqueue(Request("c"));
        probe.Scheduler.Pump();

        source.Cancel();
        probe.Scheduler.Answered(first, 0);
        probe.Scheduler.Pump();

        Assert.Equal(new[] { "a", "c" }, probe.Presented);
        Assert.Empty(answers);
        Assert.Equal(0, probe.Closes);
    }

    [Fact]
    public void ACancelledOpenRequestClosesItsBoxAndTheNextOneShowsAtOnce()
    {
        var probe = new Probe();
        using var source = new CancellationTokenSource();
        var answers = new List<int>();
        var request = Request("a", answers, source.Token);
        probe.Scheduler.Enqueue(request);
        probe.Scheduler.Enqueue(Request("b"));
        probe.Scheduler.Pump();

        source.Cancel();
        probe.Scheduler.Pump();
        probe.Scheduler.Answered(request, 1);

        Assert.Equal(1, probe.Closes);
        Assert.Equal(new[] { "a", "b" }, probe.Presented);
        Assert.Empty(answers);
    }

    [Fact]
    public void AnAnswerGivenAfterCancellationIsDroppedWithoutClosingTwice()
    {
        var probe = new Probe();
        using var source = new CancellationTokenSource();
        var answers = new List<int>();
        var request = Request("a", answers, source.Token);
        probe.Scheduler.Enqueue(request);
        probe.Scheduler.Pump();

        source.Cancel();
        probe.Scheduler.Answered(request, 1);
        probe.Scheduler.Pump();

        Assert.Empty(answers);
        Assert.Equal(0, probe.Closes);
        Assert.Null(probe.Scheduler.Active);
    }

    [Fact]
    public void ClearClosesTheOpenBoxDropsTheQueueAnswersNothingAndRefusesNewRequests()
    {
        var probe = new Probe();
        var answers = new List<int>();
        var first = Request("a", answers);
        probe.Scheduler.Enqueue(first);
        probe.Scheduler.Enqueue(Request("b", answers));
        probe.Scheduler.Pump();

        probe.Scheduler.Clear();
        probe.Scheduler.Answered(first, 1);
        probe.Scheduler.Pump();

        Assert.Equal(1, probe.Closes);
        Assert.Equal(new[] { "a" }, probe.Presented);
        Assert.Empty(answers);
        Assert.Equal(0, probe.Scheduler.Queued);
        Assert.False(probe.Scheduler.Enqueue(Request("c")));
    }

    [Fact]
    public void ARequestThePresenterRejectsWithAnExceptionIsDismissedNotRetried()
    {
        var answers = new List<int>();
        var calls = 0;
        var scheduler = new UiBoxScheduler(_ =>
        {
            calls++;
            throw new InvalidOperationException("no control");
        }, () => { });
        scheduler.Enqueue(Request("a", answers));

        Assert.Throws<InvalidOperationException>(scheduler.Pump);
        scheduler.Pump();

        Assert.Equal(1, calls);
        Assert.Equal(new[] { UiServices.Dismissed }, answers);
        Assert.Equal(0, scheduler.Queued);
    }

    [Fact]
    public void RequestsFromManyThreadsAreAllAnsweredExactlyOnce()
    {
        var probe = new Probe();
        var answered = new List<int>();
        var gate = new object();
        Parallel.For(0, UiBoxScheduler.MaxQueued, index =>
        {
            var request = new UiBoxRequest($"box{index}", choice =>
            {
                lock (gate) answered.Add(choice);
            });
            Assert.True(probe.Scheduler.Enqueue(request));
        });
        Assert.Equal(UiBoxScheduler.MaxQueued, probe.Scheduler.Queued);

        for (var guard = 0; guard < 1000 && (probe.Scheduler.Queued > 0 || probe.Scheduler.Active is not null); ++guard)
        {
            probe.Scheduler.Pump();
            if (probe.Scheduler.Active is { } active) probe.Scheduler.Answered(active, probe.Presented.Count);
        }

        Assert.Equal(UiBoxScheduler.MaxQueued, probe.Presented.Count);
        Assert.Equal(UiBoxScheduler.MaxQueued, probe.Presented.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, UiBoxScheduler.MaxQueued), answered.OrderBy(value => value));
    }

    [Fact]
    public void TheQueueIsBoundedAndTheRefusalsAreCounted()
    {
        var probe = new Probe();
        for (var index = 0; index < UiBoxScheduler.MaxQueued; ++index)
            Assert.True(probe.Scheduler.Enqueue(Request($"box{index}")));

        Assert.False(probe.Scheduler.Enqueue(Request("over")));
        Assert.False(probe.Scheduler.Enqueue(Request("over again")));

        Assert.Equal(UiBoxScheduler.MaxQueued, probe.Scheduler.Queued);
        Assert.Equal(2, probe.Scheduler.Dropped);

        probe.Scheduler.Pump();

        Assert.Equal(UiBoxScheduler.MaxQueued - 1, probe.Scheduler.Queued);
        Assert.True(probe.Scheduler.Enqueue(Request("room again")));
        Assert.Equal(2, probe.Scheduler.Dropped);
        Assert.Equal(64, UiBoxScheduler.MaxQueued);
    }

    [Fact]
    public void ABusyControlCannotLetTheQueueGrowPastTheBound()
    {
        var probe = new Probe { Busy = true };
        var accepted = 0;
        for (var index = 0; index < 200; ++index)
        {
            probe.Scheduler.Pump();
            if (probe.Scheduler.Enqueue(Request($"box{index}"))) accepted++;
        }

        Assert.Equal(UiBoxScheduler.MaxQueued, accepted);
        Assert.Equal(200 - UiBoxScheduler.MaxQueued, probe.Scheduler.Dropped);
        Assert.Equal(UiBoxScheduler.MaxQueued, probe.Scheduler.Queued);
    }
}
