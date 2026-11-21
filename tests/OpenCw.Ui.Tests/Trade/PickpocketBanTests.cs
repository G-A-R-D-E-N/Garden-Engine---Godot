using System.Text;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PickpocketBanTests
{
    const string Npc = "000777:Fallout4.esm";
    const string Other = "000778:Fallout4.esm";

    [Fact]
    public void ABanIsCaseInsensitiveAndCanBeLifted()
    {
        var ledger = new ContainerLedger();

        ledger.ForbidPickpocket(Npc);

        Assert.True(ledger.IsPickpocketForbidden(Npc.ToLowerInvariant()));
        Assert.False(ledger.IsPickpocketForbidden(Other));
        Assert.False(ledger.IsEmpty);
        ledger.ForbidPickpocket(Npc, false);
        Assert.False(ledger.IsPickpocketForbidden(Npc));
        Assert.True(ledger.IsEmpty);
    }

    [Fact]
    public void ABanSurvivesASaveAndAnOldSaveWithoutOneStillLoads()
    {
        var ledger = new ContainerLedger();
        ledger.ForbidPickpocket(Npc);
        var player = new PlayerItems();
        player.Add("000100:a.esm", 1);

        var saved = ContainerStateSave.Sections(ledger, player);
        var loaded = new ContainerLedger();
        ContainerStateSave.Apply(saved, loaded, new PlayerItems());

        Assert.True(loaded.IsPickpocketForbidden(Npc));

        const string old = "{\"version\":1,\"player\":[],\"containers\":[]}";
        ContainerStateSave.Apply(new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = Encoding.UTF8.GetBytes(old) }, loaded, new PlayerItems());
        Assert.False(loaded.IsPickpocketForbidden(Npc));
    }

    [Fact]
    public void ABannedOrAngryTargetIsRefusedByTheLivePickpocketRules()
    {
        var banned = new PickpocketTarget(0, 0, true, true, false, false, false);
        var angry = new PickpocketTarget(0, 0, true, false, true, false, false);

        Assert.Equal(PickpocketOffer.NotOffered, PickpocketRules.Offer(banned, true));
        Assert.Equal(PickpocketOffer.AlreadyCaught, PickpocketRules.Offer(angry, true));
    }
}
