using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class AttachRetryQueueTests
{
    [Fact]
    public void AReferenceThatAppearsLaterIsAttachedOnTheTickItIsPresent()
    {
        var queue = new AttachRetryQueue();
        var present = false;
        var attached = 0;
        queue.Enqueue("actor", () => { if (!present) return "not spawned yet"; attached++; return null; });

        Assert.Equal(0, queue.Tick(0.016));
        Assert.Equal(0, queue.Tick(0.016));
        Assert.Equal(1, queue.Pending);
        present = true;

        Assert.Equal(1, queue.Tick(0.016));
        Assert.Equal(1, attached);
        Assert.Equal(0, queue.Pending);
        Assert.Equal(0, queue.Tick(0.016));
        Assert.Equal(1, queue.Recovered);
        Assert.Equal(0, queue.Abandoned);
    }

    [Fact]
    public void AReferenceThatNeverAppearsIsAbandonedAfterTheBoundWithItsReason()
    {
        var queue = new AttachRetryQueue();
        var calls = 0;
        queue.Enqueue("ghost", () => { calls++; return "not spawned yet"; });

        for (var seconds = 0.0; seconds < AttachRetryQueue.MaxSeconds - 1; seconds += 0.5) queue.Tick(0.5);
        Assert.Equal(1, queue.Pending);
        Assert.Equal(0, queue.Abandoned);

        queue.Tick(AttachRetryQueue.MaxSeconds);

        Assert.True(calls > 0);
        Assert.Equal(0, queue.Pending);
        Assert.Equal(1, queue.Abandoned);
        Assert.Contains("ghost", queue.LastAbandonReason, StringComparison.Ordinal);
        Assert.Contains("not spawned yet", queue.LastAbandonReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ASlowSpawnSurvivesManyFastFrames()
    {
        var queue = new AttachRetryQueue();
        var present = false;
        queue.Enqueue("slow", () => present ? null : "not spawned yet");

        for (var frame = 0; frame < 600; ++frame) queue.Tick(1.0 / 240);
        present = true;

        Assert.Equal(1, queue.Tick(1.0 / 240));
        Assert.Equal(0, queue.Abandoned);
    }

    [Fact]
    public void AClearInsideAnAttemptDoesNotThrowAndTheClearedEntriesDoNotRun()
    {
        var queue = new AttachRetryQueue();
        var secondRan = false;
        queue.Enqueue("first", () => { queue.Clear(); return "later"; });
        queue.Enqueue("second", () => { secondRan = true; return null; });

        Assert.Equal(0, queue.Tick(0.016));

        Assert.False(secondRan);
        Assert.Equal(0, queue.Pending);
        Assert.Equal(0, queue.Abandoned);
    }

    [Fact]
    public void AnAttemptThatThrowsIsAbandonedByNameAndTheRestStillRun()
    {
        var queue = new AttachRetryQueue();
        var ran = false;
        queue.Enqueue("bad", () => throw new InvalidOperationException("boom"));
        queue.Enqueue("good", () => { ran = true; return null; });

        Assert.Equal(1, queue.Tick(0.016));

        Assert.True(ran);
        Assert.Equal(1, queue.Abandoned);
        Assert.Contains("bad: boom", queue.LastAbandonReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateKeyIsQueuedOnceAndAFullQueueRefusesByName()
    {
        var queue = new AttachRetryQueue();
        for (var i = 0; i < AttachRetryQueue.Capacity; ++i) Assert.True(queue.Enqueue("k" + i, () => "later"));
        Assert.True(queue.Enqueue("k0", () => "later"));
        Assert.Equal(AttachRetryQueue.Capacity, queue.Pending);

        Assert.False(queue.Enqueue("overflow", () => "later"));

        Assert.Equal(1, queue.Abandoned);
        Assert.Contains("overflow", queue.LastAbandonReason, StringComparison.Ordinal);
    }
}
