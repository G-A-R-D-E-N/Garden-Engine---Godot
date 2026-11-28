using OpenCommonwealth;
using Xunit;

namespace OpenCw.Ui.Tests;

public class RetryLedgerTests
{
    private const string Skeleton = "meshes/actors/character/characterassets/skeleton.hkx";
    private const string RobotSkeleton = "meshes/actors/robot/characterassets/skeleton.hkx";
    private const string Idle = "meshes/actors/character/animations/idle.hkx";
    private const string Walk = "meshes/actors/character/animations/walk.hkx";
    private const string Fallout4 = @"C:\Games\Fallout 4";

    private static RetryLedger.RetryBatch Claimed(RetryLedger ledger, params string[] pending) =>
        Assert.IsType<RetryLedger.RetryBatch>(ledger.TryClaim(Skeleton, pending, () => Fallout4));

    [Fact]
    public void TheFirstRetryClaimsEveryPendingEntryWithTheInstallRoot()
    {
        var ledger = new RetryLedger(() => 0);
        var batch = Claimed(ledger, Idle, Walk);
        Assert.Equal(Fallout4, batch.InstallRoot);
        Assert.Equal([Idle, Walk], batch.Entries);
    }

    [Fact]
    public void ASecondRetryIsRefusedInsideTheCooldown()
    {
        double now = 0;
        var ledger = new RetryLedger(() => now);
        Claimed(ledger, Idle);
        now += RetryLedger.CooldownSeconds - 0.5;
        Assert.Null(ledger.TryClaim(Skeleton, [Idle], () => Fallout4));
    }

    [Fact]
    public void TheSameEntryIsClaimedAgainOnceTheCooldownElapses()
    {
        double now = 0;
        var ledger = new RetryLedger(() => now);
        Claimed(ledger, Idle);
        now += RetryLedger.CooldownSeconds;
        Assert.Equal([Idle], Claimed(ledger, Idle).Entries);
    }

    [Fact]
    public void AFailingEntryBacksOffOneCooldownPerAttemptAndStopsAtTheCap()
    {
        double now = 0;
        var ledger = new RetryLedger(() => now);
        for (int attempt = 1; attempt <= RetryLedger.MaxAttempts; attempt++)
        {
            Assert.Equal([Idle], Claimed(ledger, Idle).Entries);
            now += RetryLedger.CooldownSeconds - 1;
            Assert.Null(ledger.TryClaim(Skeleton, [Idle], () => Fallout4));
            now += 1;
        }
        Assert.Null(ledger.TryClaim(Skeleton, [Idle], () => Fallout4));
        now += RetryLedger.CooldownSeconds * RetryLedger.MaxAttempts;
        Assert.Null(ledger.TryClaim(Skeleton, [Idle], () => Fallout4));
    }

    [Fact]
    public void NoKnownInstallSpendsNoAttempt()
    {
        double now = 0;
        var ledger = new RetryLedger(() => now);
        Assert.Null(ledger.TryClaim(Skeleton, [Idle], () => null));
        Assert.True(ledger.HasUnclaimed(Skeleton, [Idle]));
        now += 0.5;
        Assert.Equal([Idle], Claimed(ledger, Idle).Entries);
        Assert.False(ledger.HasUnclaimed(Skeleton, [Idle]));
    }

    [Fact]
    public void AConvertedEntryLeavesTheRetrySetAndTheOthersStillWait()
    {
        double now = 0;
        var ledger = new RetryLedger(() => now);
        Claimed(ledger, Idle, Walk);
        now += RetryLedger.CooldownSeconds;
        Assert.Null(ledger.TryClaim(Skeleton, [], () => Fallout4));
        Assert.Equal([Walk], Claimed(ledger, Walk).Entries);
    }

    [Fact]
    public void ACappedEntryIsRetriedAgainOnlyByAFreshLedger()
    {
        double now = 0;
        var capped = new RetryLedger(() => now);
        for (int attempt = 0; attempt < RetryLedger.MaxAttempts; attempt++)
        {
            Claimed(capped, Idle);
            now += RetryLedger.CooldownSeconds;
        }
        Assert.Null(capped.TryClaim(Skeleton, [Idle], () => Fallout4));
        Assert.Equal([Idle], Claimed(new RetryLedger(() => now), Idle).Entries);
    }

    [Fact]
    public void SkeletonsAreCountedApart()
    {
        var ledger = new RetryLedger(() => 0);
        Assert.Single(ledger.Claim(Skeleton, [Idle]));
        Assert.Single(ledger.Claim(RobotSkeleton, [Idle]));
        Assert.Empty(ledger.Claim(Skeleton.ToUpperInvariant(), [Idle]));
    }
}
