using System;
using System.Threading;
using OpenCommonwealth.Services.Dialogue;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class IndexLoaderTests
{
    sealed class Built;

    [Fact]
    public void AFailedBuildIsRetriedByTheNextStartUntilTheAttemptsRunOut()
    {
        var loader = new IndexLoader<Built>();
        int builds = 0;
        var reports = new System.Collections.Generic.List<(int Attempt, bool Retry)>();
        Built Fail() { builds++; throw new InvalidOperationException("no index"); }
        for (int i = 0; i < IndexLoader<Built>.MaxAttempts + 2; i++)
            loader.Start(Fail, (_, attempt, retry) => reports.Add((attempt, retry))).Wait();
        Assert.Equal(IndexLoader<Built>.MaxAttempts, builds);
        Assert.Equal([(1, true), (2, true), (3, false)], reports);
        Assert.Null(loader.Value);
        Assert.Contains("no index", loader.Failure);
        Assert.NotNull(loader.Current);
    }

    [Fact]
    public void AFailureThatFinishesBeforeStartReturnsStillLeavesTheLoaderRetryable()
    {
        for (int i = 0; i < 300; i++)
        {
            var loader = new IndexLoader<Built>();
            loader.Start(() => throw new InvalidOperationException("fast"));
            SpinWait.SpinUntil(() => loader.Failures == 1, 5000);
            Assert.Null(loader.Current);
        }
    }

    [Fact]
    public void ARetryThatSucceedsClearsTheFailure()
    {
        var loader = new IndexLoader<Built>();
        int builds = 0;
        Built Once() => builds++ == 0 ? throw new InvalidOperationException("first") : new Built();
        loader.Start(Once).Wait();
        Assert.Null(loader.Value);
        loader.Start(Once).Wait();
        Assert.NotNull(loader.Value);
        Assert.Null(loader.Failure);
    }

    [Fact]
    public void ABuildFromAReplacedEnvironmentIsDropped()
    {
        var loader = new IndexLoader<Built>();
        using var gate = new ManualResetEventSlim();
        var stale = loader.Start(() => { gate.Wait(); return new Built(); });
        loader.Reset();
        gate.Set();
        stale.Wait();
        Assert.Null(loader.Value);
        Assert.Null(loader.Current);
        Assert.Equal(0, loader.Failures);
    }

    [Fact]
    public void AStartWhileABuildRunsReturnsTheSameTask()
    {
        var loader = new IndexLoader<Built>();
        using var gate = new ManualResetEventSlim();
        var first = loader.Start(() => { gate.Wait(); return new Built(); });
        Assert.Same(first, loader.Start(() => throw new InvalidOperationException("second build")));
        gate.Set();
        first.Wait();
        Assert.NotNull(loader.Value);
    }

    [Fact]
    public void ABuildOnceRunsItsBuildOnceAndRethrowsTheFirstFailureForever()
    {
        var once = new BuildOnce();
        int runs = 0;
        once.Run(() => runs++);
        once.Run(() => runs++);
        Assert.Equal(1, runs);
        Assert.Null(once.Failure);

        var failing = new BuildOnce();
        var first = Assert.Throws<InvalidOperationException>(() => failing.Run(() => { runs++; throw new InvalidOperationException("boom"); }));
        Assert.Equal("boom", first.Message);
        var later = Assert.Throws<InvalidOperationException>(() => failing.Run(() => runs++));
        Assert.Contains("failed earlier", later.Message);
        Assert.Same(first, later.InnerException);
        Assert.Same(first, failing.Failure);
        Assert.Equal(2, runs);
    }
}
