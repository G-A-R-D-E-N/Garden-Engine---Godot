using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettlerAssignmentsProviderTests
{
    sealed class Roster : ISettlerRoster
    {
        public event Action? Changed;

        public IReadOnlyList<SettlementInfo> Settlements { get; } = new[] { new SettlementInfo("s1", "Sanctuary", Array.Empty<SettlementMetric>()) };

        public IReadOnlyList<SettlerInfo> Settlers { get; } = new[]
        {
            new SettlerInfo("ann", "Ann", "s1"),
            new SettlerInfo("bob", "Bob", "s1", SettlerJob.Food),
        };

        public IReadOnlyList<WorkObject> Items { get; } = new[]
        {
            new WorkObject("crop1", "Crop", true, false),
            new WorkObject("bed1", "Bed", false, true),
            new WorkObject("post1", "Post", true, false, false),
            new WorkObject("lamp1", "Lamp", false, false),
        };

        public IReadOnlyList<WorkObject> Objects(string settlerId) => Items;

        public void Raise() => Changed?.Invoke();
    }

    static (SettlerAssignmentsProvider Provider, SettlerAssignments Store, Roster Roster) Make()
    {
        var roster = new Roster();
        var store = new SettlerAssignments();
        return (new SettlerAssignmentsProvider(roster, store), store, roster);
    }

    [Fact]
    public void JobTargetsAreTheWorkObjectsAndBedsWithTheirOwnerNamedUnlessItIsTheAskingSettler()
    {
        var (provider, store, _) = Make();
        store.Assign("bob", "crop1", false);

        var forAnn = provider.ListJobTargets("ann");
        var forBob = provider.ListJobTargets("bob");

        Assert.Equal(new[] { "crop1", "bed1" }, forAnn.Select(target => target.ObjectId).ToArray());
        Assert.Equal("Bob", forAnn.Single(target => target.ObjectId == "crop1").TakenBy);
        Assert.Equal(string.Empty, forBob.Single(target => target.ObjectId == "crop1").TakenBy);
        Assert.False(forAnn.Single(target => target.ObjectId == "bed1").NeedsWorker);
    }

    [Fact]
    public void AnObjectThatDoesNotAllowAssignmentIsNotAJobTarget()
    {
        var (provider, _, _) = Make();

        Assert.DoesNotContain("post1", provider.ListJobTargets("ann").Select(target => target.ObjectId));
    }

    [Fact]
    public void AssigningAJobStoresItReportsTheWorkAndRaisesChanged()
    {
        var (provider, store, _) = Make();
        var raised = 0;
        provider.Changed += () => raised++;

        var result = provider.AssignJob("ann", "crop1");

        Assert.True(result.Ok);
        Assert.Equal("crop1", store.WorkObjectOf("ann"));
        Assert.Equal(1, raised);
        var ann = provider.Settlers.Single(settler => settler.Id == "ann");
        Assert.Equal("Crop", ann.WorkObject);
        Assert.Equal(SettlerJob.Other, ann.Job);
        Assert.Equal(SettlerJob.Food, provider.Settlers.Single(settler => settler.Id == "bob").Job);
    }

    [Fact]
    public void ABedIsAssignedToTheBedTableAndNotShownAsWork()
    {
        var (provider, store, _) = Make();

        Assert.True(provider.AssignJob("ann", "bed1").Ok);

        Assert.Equal("bed1", store.BedOf("ann"));
        Assert.Equal(string.Empty, provider.Settlers.Single(settler => settler.Id == "ann").WorkObject);
    }

    [Theory]
    [InlineData("post1")]
    [InlineData("lamp1")]
    [InlineData("gone")]
    public void ForbiddenNonWorkAndUnknownObjectsFailWithoutChangingAnything(string objectId)
    {
        var (provider, store, _) = Make();

        Assert.False(provider.AssignJob("ann", objectId).Ok);

        Assert.True(store.IsEmpty);
    }

    [Fact]
    public void AnObjectHeldInTheOtherTableFails()
    {
        var (provider, store, _) = Make();
        store.Assign("bob", "crop1", true);

        Assert.False(provider.AssignJob("ann", "crop1").Ok);

        Assert.Equal("bob", store.OwnerOf("crop1"));
        Assert.Null(store.WorkObjectOf("ann"));
    }

    [Fact]
    public void UnassignReportsWhetherThereWasWork()
    {
        var (provider, store, _) = Make();
        store.Assign("ann", "crop1", false);

        Assert.True(provider.Unassign("ann").Ok);
        Assert.False(provider.Unassign("ann").Ok);
    }

    [Fact]
    public void UnassignBedClearsOnlyTheBedAndUnassignClearsOnlyTheWork()
    {
        var (provider, store, _) = Make();
        store.Assign("ann", "crop1", false);
        store.Assign("ann", "bed1", true);
        var raised = 0;
        provider.Changed += () => raised++;
        var ann = provider.Settlers.Single(settler => settler.Id == "ann");
        Assert.Equal("Bed", ann.BedObject);

        Assert.True(provider.UnassignBed("ann").Ok);

        Assert.Null(store.BedOf("ann"));
        Assert.Equal("crop1", store.WorkObjectOf("ann"));
        Assert.Equal(string.Empty, provider.Settlers.Single(settler => settler.Id == "ann").BedObject);
        Assert.False(provider.UnassignBed("ann").Ok);
        Assert.True(provider.Unassign("ann").Ok);
        Assert.Null(store.WorkObjectOf("ann"));
        Assert.Equal(2, raised);
    }

    [Fact]
    public void TheCommandControllerClearsAWorkAssignmentAndABedWithoutRemovingTheObjects()
    {
        var (provider, store, _) = Make();
        store.Assign("ann", "crop1", false);
        store.Assign("ann", "bed1", true);
        var work = new SettlerCommandController(provider, "ann");
        Press(work, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Accept);
        Assert.Null(store.WorkObjectOf("ann"));
        Assert.Equal("bed1", store.BedOf("ann"));

        var bed = new SettlerCommandController(provider, "ann");
        Press(bed, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Accept);

        Assert.Null(store.BedOf("ann"));
        Assert.True(store.IsEmpty);
        Assert.Equal(new[] { "crop1", "bed1" }, provider.ListJobTargets("ann").Select(target => target.ObjectId));
    }

    [Fact]
    public void ThePickerReassignsAnOccupiedWorkObjectAndBedAndNamesTheDisplacedSettler()
    {
        var (provider, store, _) = Make();
        store.Assign("bob", "crop1", false);
        store.Assign("bob", "bed1", true);

        var work = new SettlerCommandController(provider, "ann");
        Press(work, MenuInputAction.Accept, MenuInputAction.Accept);

        Assert.Equal("ann", store.OwnerOf("crop1"));
        Assert.Contains("Bob was unassigned.", work.Message);

        var bed = new SettlerCommandController(provider, "ann");
        Press(bed, MenuInputAction.Accept, MenuInputAction.Down, MenuInputAction.Accept);

        Assert.Equal("ann", store.OwnerOf("bed1"));
        Assert.Contains("Bob was unassigned.", bed.Message);
        Assert.Null(store.BedOf("bob"));
    }

    static void Press(SettlerCommandController controller, params MenuInputAction[] actions)
    {
        foreach (var action in actions) controller.Handle(action);
    }

    [Fact]
    public void MovingCaravansAndRoutesAreNotAvailableAndSettlersSayWhy()
    {
        var (provider, _, _) = Make();

        Assert.False(provider.MoveHome("ann", "s1").Ok);
        Assert.False(provider.AssignCaravan("ann", "s1").Ok);
        Assert.False(provider.RemoveRoute("r1").Ok);
        Assert.Empty(provider.Routes);
        Assert.All(provider.Settlers, settler =>
        {
            Assert.False(settler.CanMove);
            Assert.False(settler.CanCaravan);
        });
    }

    [Fact]
    public void RosterChangesAreForwarded()
    {
        var (provider, _, roster) = Make();
        var raised = 0;
        provider.Changed += () => raised++;

        roster.Raise();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void TheCommandControllerCanDriveItEndToEnd()
    {
        var (provider, store, _) = Make();
        var controller = new SettlerCommandController(provider, "ann");

        Assert.Equal(CommandLevel.Commands, controller.Level);
        controller.Handle(MenuInputAction.Accept);
        Assert.Equal(CommandLevel.Targets, controller.Level);
        Assert.Equal("crop1", controller.Rows.Selected?.Key);
        controller.Handle(MenuInputAction.Accept);

        Assert.Equal("crop1", store.WorkObjectOf("ann"));
    }

    [Fact]
    public void AnOwnerOutsideTheRosterIsNamedByTheResolverOrTheFallbackNeverByItsKey()
    {
        var roster = new Roster();
        var store = new SettlerAssignments();
        store.Assign("key:far", "crop1", false);
        store.Assign("key:lost", "bed1", true);
        var provider = new SettlerAssignmentsProvider(roster, store, key => key == "key:far" ? "Far Farmer" : null);

        var targets = provider.ListJobTargets("ann");

        Assert.Equal("Far Farmer", targets.Single(target => target.ObjectId == "crop1").TakenBy);
        Assert.Equal(SettlerCommandText.UnknownSettlerText, targets.Single(target => target.ObjectId == "bed1").TakenBy);
    }

    [Fact]
    public void AssigningOverAnotherOwnerSucceedsAndSaysWhoWasUnassigned()
    {
        var (provider, store, _) = Make();
        store.Assign("bob", "crop1", false);

        var result = provider.AssignJob("ann", "crop1");

        Assert.True(result.Ok);
        Assert.Equal("Bob was unassigned.", result.Reason);
        Assert.Equal("ann", store.OwnerOf("crop1"));
        Assert.Null(store.WorkObjectOf("bob"));
    }

    [Fact]
    public void AssigningAFreeObjectOrReassigningTheSameOneHasNoDisplacedNotice()
    {
        var (provider, _, _) = Make();

        Assert.Equal(string.Empty, provider.AssignJob("ann", "crop1").Reason);
        Assert.Equal(string.Empty, provider.AssignJob("ann", "crop1").Reason);
    }

    [Fact]
    public void ACommandPickerThatWentStaleStillNamesTheSettlerItDisplaced()
    {
        var (provider, store, _) = Make();
        var controller = new SettlerCommandController(provider, "ann");
        controller.Handle(MenuInputAction.Accept);
        store.Assign("bob", "crop1", false);

        controller.Handle(MenuInputAction.Accept);

        Assert.Equal("ann", store.OwnerOf("crop1"));
        Assert.Contains("Bob was unassigned.", controller.Message);
    }
}
