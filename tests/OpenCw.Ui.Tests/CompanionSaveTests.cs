using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CompanionSaveTests
{
    const string Ann = "00AA:A.esm";
    const string AnnBase = "0A01:A.esm";
    const string Chest = "000A01:Fallout4.esm";
    const string Leather = "000C01:Fallout4.esm";
    const string Ammo = "000F02:Fallout4.esm";

    static ContainerLedger Recruited()
    {
        var ledger = new ContainerLedger();
        ledger.Companions.Recruit(Ann, AnnBase);
        ledger.Companions.Configure(Ann, CompanionState.Wait, CompanionDistance.Far, CompanionStance.Aggressive);
        return ledger;
    }

    static IReadOnlyDictionary<string, byte[]> Json(string text) =>
        new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = Encoding.UTF8.GetBytes(text) };

    [Fact]
    public void A_companion_survives_a_save_and_load_with_its_state()
    {
        var sections = ContainerStateSave.Sections(Recruited(), new PlayerItems());
        var loaded = new ContainerLedger();

        ContainerStateSave.Apply(sections, loaded, new PlayerItems());

        Assert.Equal(new CompanionEntry(Ann, AnnBase, CompanionState.Wait, CompanionDistance.Far, CompanionStance.Aggressive), loaded.Companions.Current);
        Assert.True(loaded.Companions.HasBeenCompanion(Ann));
    }

    [Fact]
    public void A_save_without_the_key_loads_as_no_companion_and_clears_the_one_held()
    {
        var ledger = new ContainerLedger();
        ledger.Freeze(Chest, [new InventoryStack(Leather, 2)]);
        var sections = ContainerStateSave.Sections(ledger, new PlayerItems());
        var target = Recruited();

        ContainerStateSave.Apply(sections, target, new PlayerItems());

        Assert.Null(target.Companions.Current);
        Assert.Equal([(Leather, 2)], target.Find(Chest)!.Stacks.Select(s => (s.Id, s.Count)));
    }

    [Fact]
    public void An_empty_roster_writes_no_key_and_a_roster_only_ledger_is_not_empty()
    {
        var ledger = new ContainerLedger();
        ledger.Freeze(Chest, [new InventoryStack(Leather, 2)]);
        var withoutRoster = Encoding.UTF8.GetString(ContainerStateSave.Sections(ledger, new PlayerItems())[ContainerStateSave.SectionName]);
        Assert.DoesNotContain("\"companions\"", withoutRoster);

        var rosterOnly = new ContainerLedger();
        rosterOnly.Companions.Recruit(Ann, AnnBase);
        Assert.False(rosterOnly.IsEmpty);
        var withRoster = Encoding.UTF8.GetString(ContainerStateSave.Sections(rosterOnly, new PlayerItems())[ContainerStateSave.SectionName]);
        Assert.Contains("\"companions\"", withRoster);

        Assert.True(new ContainerLedger().IsEmpty);
    }

    [Fact]
    public void An_empty_companion_list_loads_as_no_companion()
    {
        var target = Recruited();

        ContainerStateSave.Apply(Json("{\"version\":1,\"player\":[],\"containers\":[],\"companions\":[]}"), target, new PlayerItems());

        Assert.Null(target.Companions.Current);
    }

    [Theory]
    [InlineData("[{\"ref\":\"00AA:A.esm\",\"base\":\"0A01:A.esm\",\"state\":\"Stay\",\"distance\":\"Medium\",\"stance\":\"Defensive\"}]")]
    [InlineData("[{\"ref\":\"00AA:A.esm\",\"base\":\"0A01:A.esm\",\"state\":\"Follow\",\"distance\":\"Medium\"}]")]
    [InlineData("[{\"ref\":\"00AA:A.esm\",\"base\":\"0A01:A.esm\",\"state\":\"Follow\",\"distance\":\"Medium\",\"stance\":\"Defensive\"},{\"ref\":\"00BB:A.esm\",\"base\":\"0B01:A.esm\",\"state\":\"Follow\",\"distance\":\"Medium\",\"stance\":\"Defensive\"}]")]
    [InlineData("[{\"ref\":\"00AA:A.esm\",\"base\":\"0A01:A.esm\",\"state\":\"1\",\"distance\":\"Medium\",\"stance\":\"Defensive\"}]")]
    [InlineData("[{\"ref\":\"00AA:A.esm\",\"base\":\"0A01:A.esm\",\"state\":\"Follow\",\"distance\":\"Near,Medium\",\"stance\":\"Defensive\"}]")]
    [InlineData("{\"ref\":\"00AA:A.esm\"}")]
    [InlineData("[{\"ref\":\"\",\"base\":\"0A01:A.esm\",\"state\":\"Follow\",\"distance\":\"Medium\",\"stance\":\"Defensive\"}]")]
    public void A_malformed_companion_row_throws_and_leaves_the_ledger_and_player_untouched(string companions)
    {
        var target = Recruited();
        target.Freeze(Chest, [new InventoryStack(Leather, 2)]);
        var player = new PlayerItems();
        player.Add(Ammo, 5);
        var payload = "{\"version\":1,\"player\":[],\"containers\":[],\"companions\":" + companions + "}";

        Assert.Throws<FormatException>(() => ContainerStateSave.Apply(Json(payload), target, player));

        Assert.Equal(new CompanionEntry(Ann, AnnBase, CompanionState.Wait, CompanionDistance.Far, CompanionStance.Aggressive), target.Companions.Current);
        Assert.Equal([(Leather, 2)], target.Find(Chest)!.Stacks.Select(s => (s.Id, s.Count)));
        Assert.Equal(5, player.CountOf(Ammo));
    }

    [Fact]
    public void A_loaded_follow_companion_is_respawned_and_carried_but_a_waiting_one_stays_put()
    {
        var follow = new ContainerLedger();
        follow.Companions.Recruit(Ann, AnnBase);
        var loadedFollow = new ContainerLedger();
        ContainerStateSave.Apply(ContainerStateSave.Sections(follow, new PlayerItems()), loadedFollow, new PlayerItems());
        var loadedWait = new ContainerLedger();
        ContainerStateSave.Apply(ContainerStateSave.Sections(Recruited(), new PlayerItems()), loadedWait, new PlayerItems());

        Assert.True(CompanionFollowRules.NeedsRespawn(loadedFollow.Companions.Current, false, true));
        Assert.True(CompanionFollowRules.Carries(loadedFollow.Companions.Current, Ann, true));
        Assert.False(CompanionFollowRules.NeedsRespawn(loadedWait.Companions.Current, false, true));
        Assert.False(CompanionFollowRules.Carries(loadedWait.Companions.Current, Ann, true));
    }
}
