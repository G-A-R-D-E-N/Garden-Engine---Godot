using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CaughtRecoveryTests
{
    const string Npc = "000777:Fallout4.esm";

    [Fact]
    public void ACaughtFlagExpiresAfterTheRecoveryWindow()
    {
        var ledger = new ContainerLedger();
        ledger.MarkCaught(Npc, 100);

        Assert.True(ledger.IsCaught(Npc, 100 + ContainerLedger.CaughtRecoveryHours - 0.5));
        Assert.False(ledger.IsCaught(Npc, 100 + ContainerLedger.CaughtRecoveryHours));
        Assert.False(ledger.IsCaught(Npc));
        Assert.Empty(ledger.Caught);
    }

    [Fact]
    public void AFlagReadWithoutAUsableClockReadingNeverExpires()
    {
        var ledger = new ContainerLedger();
        ledger.MarkCaught(Npc);

        Assert.True(ledger.IsCaught(Npc));
        Assert.True(ledger.IsCaught(Npc, double.NaN));
        Assert.Single(ledger.Caught);
    }

    [Fact]
    public void AnOldFlagIsStampedAtItsFirstReadAndThenRecovers()
    {
        var ledger = new ContainerLedger();
        ledger.MarkCaught(Npc);

        Assert.True(ledger.IsCaught(Npc, 200));
        Assert.True(ledger.IsCaught(Npc, 210));
        Assert.False(ledger.IsCaught(Npc, 200 + ContainerLedger.CaughtRecoveryHours));
    }

    [Fact]
    public void ClearCaughtDropsTheFlagAndItsStamp()
    {
        var ledger = new ContainerLedger();
        ledger.MarkCaught(Npc, 10);

        ledger.ClearCaught(Npc);

        Assert.False(ledger.IsCaught(Npc));
        Assert.Empty(ledger.CaughtAt);
    }

    [Fact]
    public void TheCaughtTimeSurvivesASaveAndAnOldSaveStillLoads()
    {
        var ledger = new ContainerLedger();
        ledger.MarkCaught(Npc, 300);
        var saved = ContainerStateSave.Sections(ledger, new PlayerItems());

        var loaded = new ContainerLedger();
        ContainerStateSave.Apply(saved, loaded, new PlayerItems());

        Assert.True(loaded.IsCaught(Npc, 300 + ContainerLedger.CaughtRecoveryHours - 1));
        Assert.False(loaded.IsCaught(Npc, 300 + ContainerLedger.CaughtRecoveryHours));

        const string old = "{\"version\":1,\"player\":[],\"containers\":[],\"caught\":[\"000777:Fallout4.esm\"]}";
        var legacy = new ContainerLedger();
        ContainerStateSave.Apply(new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = System.Text.Encoding.UTF8.GetBytes(old) }, legacy, new PlayerItems());
        Assert.True(legacy.IsCaught(Npc, 400));
        Assert.False(legacy.IsCaught(Npc, 400 + ContainerLedger.CaughtRecoveryHours));
    }
}
