using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettlerWiringTests
{
    [Theory]
    [InlineData(AreaShape.Box, 2.0, 1.0, 3.0, 1.9, 0.9, -2.9, true)]
    [InlineData(AreaShape.Box, 2.0, 1.0, 3.0, 2.1, 0.0, 0.0, false)]
    [InlineData(AreaShape.Box, 2.0, 1.0, 3.0, 0.0, 0.0, 3.1, false)]
    [InlineData(AreaShape.Sphere, 5.0, 0.0, 0.0, 3.0, 4.0, 0.0, true)]
    [InlineData(AreaShape.Sphere, 5.0, 0.0, 0.0, 3.0, 4.0, 0.1, false)]
    [InlineData(AreaShape.Ellipsoid, 4.0, 2.0, 1.0, 0.0, 1.9, 0.0, true)]
    [InlineData(AreaShape.Ellipsoid, 4.0, 2.0, 1.0, 3.0, 1.5, 0.0, false)]
    [InlineData(AreaShape.Ellipsoid, 4.0, 0.0, 1.0, 0.0, 0.0, 0.0, false)]
    public void AVolumeContainsAPointByItsShape(AreaShape shape, double hx, double hy, double hz, double x, double y, double z, bool inside)
    {
        Assert.Equal(inside, WorkshopArea.InVolume(shape, hx, hy, hz, x, y, z));
    }

    [Fact]
    public void ABuildAreaCheckRunsOnlyWhenTheFiveSecondsAreUp()
    {
        var store = new SettlerAssignments();
        var session = new SettlerCommandSession(store);
        session.Start("ann", new SettlerFacts(true));
        var asked = 0;

        Assert.False(session.Tick(4.0, () => { asked++; return false; }));
        Assert.Equal(0, asked);
        Assert.True(session.Tick(1.0, () => { asked++; return false; }));
        Assert.Equal(1, asked);
        Assert.Null(session.Active);
    }

    [Fact]
    public void AnInsideSettlerKeepsTheStateAndTheCheckRearms()
    {
        var session = new SettlerCommandSession(new SettlerAssignments());
        session.Start("ann", new SettlerFacts(true));
        var asked = 0;

        Assert.False(session.Tick(5.0, () => { asked++; return true; }));
        Assert.False(session.Tick(4.0, () => { asked++; return false; }));
        Assert.True(session.Tick(1.0, () => { asked++; return false; }));

        Assert.Equal(2, asked);
        Assert.Null(session.Active);
    }

    [Fact]
    public void AWorkshopItemThatIsNeitherWorkNorBedIsATargetThatOnlyEndsTheState()
    {
        Assert.Equal(new CommandTarget("lamp1", true, false, false), WorkshopObjectFacts.Of("lamp1", false, false, workshopItem: true));
        Assert.Equal(new CommandTarget("crop1", true, true, false), WorkshopObjectFacts.Of("crop1", true, false, workshopItem: true));
        Assert.Null(WorkshopObjectFacts.Of("door1", false, false, workshopItem: false));
    }

    [Fact]
    public void ActivatingAnyOtherWorkshopItemEndsTheCommandStateWithoutAssigning()
    {
        var store = new SettlerAssignments();
        var session = new SettlerCommandSession(store);
        var reports = new List<CommandResult>();
        var handler = new SettlerCommandActivation(session, target => WorkshopObjectFacts.Of(target.RefKey, false, false, workshopItem: true), reports.Add);
        session.Start("ann", new SettlerFacts(true));

        var result = handler.Activate(new ActivationTarget("lamp1", "base", "STAT", new HashSet<string>()));

        Assert.Equal(ActivationStatus.Handled, result.Status);
        Assert.Equal(CommandOutcome.Ended, reports.Single().Outcome);
        Assert.Null(session.Active);
        Assert.Null(store.WorkObjectOf("ann"));
        Assert.Null(store.BedOf("ann"));
    }

    [Fact]
    public void TheRosterReadsOncePerRefreshAndRaisesChangedOnRefresh()
    {
        var home = new SettlementInfo("bench", "Home", Array.Empty<SettlementMetric>());
        var settlerReads = 0;
        var objectReads = 0;
        var roster = new WorkshopRoster(home,
            () => { settlerReads++; return new[] { new SettlerInfo("ann", "Ann", "bench") }; },
            () => { objectReads++; return new[] { new WorkObject("crop1", "Crop", true, false) }; });
        var changed = 0;
        roster.Changed += () => changed++;

        Assert.Equal("bench", roster.Settlements.Single().Id);
        Assert.Equal("ann", roster.Settlers.Single().Id);
        Assert.Equal("ann", roster.Settlers.Single().Id);
        Assert.Equal("crop1", roster.Objects("ann").Single().Id);
        Assert.Equal("crop1", roster.Objects("bob").Single().Id);
        Assert.Equal((1, 1, 0), (settlerReads, objectReads, changed));

        roster.Refresh();
        _ = roster.Settlers;
        _ = roster.Objects("ann");

        Assert.Equal((2, 2, 1), (settlerReads, objectReads, changed));
    }

    [Fact]
    public void AProviderOverTheRosterAssignsARosterObjectToARosterSettler()
    {
        var home = new SettlementInfo("bench", "Home", Array.Empty<SettlementMetric>());
        var roster = new WorkshopRoster(home,
            () => new[] { new SettlerInfo("ann", "Ann", "bench") },
            () => new[] { new WorkObject("crop1", "Crop", true, false) });
        var store = new SettlerAssignments();
        var provider = new SettlerAssignmentsProvider(roster, store);

        Assert.True(provider.AssignJob("ann", "crop1").Ok);

        Assert.Equal("crop1", store.WorkObjectOf("ann"));
        Assert.Equal("Crop", provider.Settlers.Single().WorkObject);
        Assert.False(provider.AssignJob("ann", "unknown").Ok);
    }

    [Fact]
    public void AnAimedSettlerCardMakesTheSessionAimAtTheSettler()
    {
        const string bench = "000010:a.esm";
        var reader = new FakeRecordReader();
        reader.Add(bench, "KYWD", "WorkshopWorkbenchTypeSettlement");
        var pool = new ComponentPool(reader, new PlayerItems());
        var catalog = new RecordWorkshopCatalog(reader, pool, [bench], false);
        WorkshopSettlerCard? card = null;
        var session = new WorkshopSession(catalog, pool, _ => PlacementStatus.Valid, _ => true, aimedSettler: () => card);

        Assert.Equal(WorkshopAim.Browsing, session.Aim);
        Assert.Null(session.Settler);

        card = new WorkshopSettlerCard("ann", "Ann") { CanCommand = true };

        Assert.Equal(WorkshopAim.TargetSettler, session.Aim);
        Assert.Equal("ann", session.Settler?.Id);
    }
}
