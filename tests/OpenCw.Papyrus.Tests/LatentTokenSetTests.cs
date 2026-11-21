using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class LatentTokenSetTests : HostFixture
{
    [Fact]
    public void SequentialTokensShareOneRunAndEveryTokenStaysRemembered()
    {
        var set = new LatentTokenSet();

        for (long token = 1; token <= 10_000; token++) Assert.True(set.Add(token));

        Assert.Equal(1, set.RunCount);
        for (long token = 1; token <= 10_000; token += 997) Assert.False(set.Add(token));
        Assert.True(set.Add(10_001));
        Assert.Equal(1, set.RunCount);
    }

    [Fact]
    public void TwoIncreasingAllocatorsKeepTwoRunsAndGapsMerge()
    {
        var set = new LatentTokenSet();
        var high = 1L << 40;

        for (var step = 1; step <= 500; step++)
        {
            Assert.True(set.Add(step));
            Assert.True(set.Add(high + step));
        }

        Assert.Equal(2, set.RunCount);
        Assert.False(set.Add(250));
        Assert.False(set.Add(high + 250));
        Assert.True(set.Add(700));
        Assert.True(set.Add(702));
        Assert.Equal(4, set.RunCount);
        Assert.True(set.Add(701));
        Assert.Equal(3, set.RunCount);
        Assert.False(set.Add(701));
    }

    [Fact]
    public void ExtremeAndNegativeTokensAreHandled()
    {
        var set = new LatentTokenSet();

        Assert.True(set.Add(long.MaxValue));
        Assert.True(set.Add(long.MinValue));
        Assert.True(set.Add(-1));
        Assert.True(set.Add(0));
        Assert.False(set.Add(long.MaxValue));
        Assert.False(set.Add(long.MinValue));
        Assert.Equal(3, set.RunCount);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ManyCompletedWaitsStillRefuseAStaleCompletionAndAReusedToken(HostKind kind)
    {
        var external = new ScriptSpec("External", "Scriptname External\nInt Function Answer() global native\n");
        var waiter = new ScriptSpec("Waiter",
            "Scriptname Waiter\n" +
            "Function Run()\n" +
            "  Int got = External.Answer()\n" +
            "EndFunction\n");
        var host = Track(TestHosts.Create(kind, TestHosts.Gate(), external, waiter));
        long next = 0;
        host.RegisterNative(new NativeId("External", "Answer"), _ => throw new NativeWait(next));
        var instance = host.Attach("Waiter");
        Assert.True(host.Initialize(instance).Completed);

        for (var round = 0; round < 200; round++)
        {
            next++;
            Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Run").Reason);
            Assert.True(host.CompleteLatent(next, PapyrusValue.FromInt(round)));
            host.RunStep(32);
        }

        Assert.Equal(0, host.WaitingCount);
        Assert.False(host.CompleteLatent(1, PapyrusValue.FromInt(1)));
        Assert.False(host.CompleteLatent(200, PapyrusValue.FromInt(1)));
        next = 7;
        Assert.Equal(VmStopReason.ModelError, host.RunEvent(instance, "Run").Reason);
        Assert.Equal(0, host.WaitingCount);
        next = 201;
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Run").Reason);
        Assert.True(host.CompleteLatent(201, PapyrusValue.FromInt(1)));
    }
}
