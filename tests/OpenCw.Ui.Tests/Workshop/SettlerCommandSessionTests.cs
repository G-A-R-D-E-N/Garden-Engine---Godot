using System;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettlerCommandSessionTests
{
    static readonly CommandTarget Crop = new("crop1", true, true, false);
    static readonly CommandTarget Bed = new("bed1", true, false, true);
    static readonly CommandTarget Lamp = new("lamp1", true, false, false);
    static readonly CommandTarget Locked = new("post1", false, true, false);

    static (SettlerCommandSession Session, SettlerAssignments Store) Make()
    {
        var store = new SettlerAssignments();
        return (new SettlerCommandSession(store), store);
    }

    [Fact]
    public void ACommandableSettlerInAWorkshopEntersTheCommandState()
    {
        var (session, _) = Make();

        Assert.True(session.Start("ann", new SettlerFacts(true)));

        Assert.Equal("ann", session.Active);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ASettlerThatIsNotCommandableOrHasNoWorkshopNeverEntersIt(bool commandable, bool inWorkshop)
    {
        var (session, _) = Make();

        Assert.False(session.Start("ann", new SettlerFacts(commandable, inWorkshop)));

        Assert.Null(session.Active);
    }

    [Fact]
    public void AWorkObjectGivenWhileCommandingAssignsTheSettlerAndEndsTheState()
    {
        var (session, store) = Make();
        session.Start("ann", new SettlerFacts(true));

        var result = session.Give(Crop);

        Assert.Equal(new CommandResult(CommandOutcome.Assigned), result);
        Assert.Equal("crop1", store.WorkObjectOf("ann"));
        Assert.Null(session.Active);
    }

    [Fact]
    public void ABedGivenWhileCommandingAssignsTheBed()
    {
        var (session, store) = Make();
        session.Start("ann", new SettlerFacts(true));

        Assert.Equal(CommandOutcome.Assigned, session.Give(Bed).Outcome);

        Assert.Equal("bed1", store.BedOf("ann"));
        Assert.Null(store.WorkObjectOf("ann"));
    }

    [Fact]
    public void TakingAnObjectAnotherSettlerHoldsReportsThemDisplaced()
    {
        var (session, store) = Make();
        store.Assign("bob", "crop1", false);
        session.Start("ann", new SettlerFacts(true));

        var result = session.Give(Crop);

        Assert.Equal(new CommandResult(CommandOutcome.Assigned, "bob"), result);
        Assert.Null(store.WorkObjectOf("bob"));
    }

    [Fact]
    public void AnObjectThatIsNeitherWorkNorABedEndsTheStateWithoutAssigning()
    {
        var (session, store) = Make();
        session.Start("ann", new SettlerFacts(true));

        Assert.Equal(CommandOutcome.Ended, session.Give(Lamp).Outcome);

        Assert.True(store.IsEmpty);
        Assert.Null(session.Active);
    }

    [Fact]
    public void AnObjectThatForbidsPlayerAssignmentRefusesAndKeepsTheState()
    {
        var (session, store) = Make();
        session.Start("ann", new SettlerFacts(true));

        Assert.Equal(CommandOutcome.Refused, session.Give(Locked).Outcome);

        Assert.True(store.IsEmpty);
        Assert.Equal("ann", session.Active);
    }

    [Fact]
    public void AnObjectAlreadyHeldInTheOtherTableIsRefusedAndKeepsTheState()
    {
        var (session, store) = Make();
        store.Assign("bob", "bed1", true);
        session.Start("ann", new SettlerFacts(true));

        Assert.Equal(CommandOutcome.Refused, session.Give(new CommandTarget("bed1", true, true, false)).Outcome);

        Assert.Equal("ann", session.Active);
        Assert.Equal("bob", store.OwnerOf("bed1"));
        Assert.Null(store.WorkObjectOf("ann"));
    }

    [Fact]
    public void CommandingIsTrueOnlyForAStartedNonCompanion()
    {
        var (session, _) = Make();

        Assert.False(session.Commanding);
        session.Start("ann", new SettlerFacts(true));
        Assert.True(session.Commanding);
        session.Start("piper", new SettlerFacts(true, true, true));
        Assert.False(session.Commanding);
        session.Cancel();
        Assert.False(session.Commanding);
    }

    [Fact]
    public void GivingWithoutACommandStateIsIgnored()
    {
        var (session, store) = Make();

        Assert.Equal(CommandOutcome.Ignored, session.Give(Crop).Outcome);

        Assert.True(store.IsEmpty);
    }

    [Fact]
    public void ACompanionIsNeverAssigned()
    {
        var (session, store) = Make();
        session.Start("piper", new SettlerFacts(true, true, true));

        Assert.Equal(CommandOutcome.Ignored, session.Give(Crop).Outcome);

        Assert.True(store.IsEmpty);
    }

    [Fact]
    public void ASecondSelfActivationEndsTheStateButTheFirstDoesNot()
    {
        var (session, _) = Make();
        session.Start("ann", new SettlerFacts(true));

        Assert.False(session.SelfActivate());
        Assert.Equal("ann", session.Active);
        Assert.True(session.SelfActivate());
        Assert.Null(session.Active);
    }

    [Fact]
    public void StartingAgainResetsTheSelfActivationCount()
    {
        var (session, _) = Make();
        session.Start("ann", new SettlerFacts(true));
        session.SelfActivate();
        session.Start("ann", new SettlerFacts(true));

        Assert.False(session.SelfActivate());
        Assert.Equal("ann", session.Active);
    }

    [Fact]
    public void LeavingTheBuildAreaEndsTheStateOnlyAtTheNextFiveSecondCheck()
    {
        var (session, _) = Make();
        session.Start("ann", new SettlerFacts(true));

        Assert.False(session.Tick(4.0, false));
        Assert.Equal("ann", session.Active);
        Assert.True(session.Tick(1.0, false));
        Assert.Null(session.Active);
    }

    [Fact]
    public void StayingInTheBuildAreaRearmsTheCheck()
    {
        var (session, _) = Make();
        session.Start("ann", new SettlerFacts(true));

        Assert.False(session.Tick(5.0, true));
        Assert.False(session.Tick(4.0, false));
        Assert.True(session.Tick(1.0, false));
    }

    [Fact]
    public void TickAndSelfActivateDoNothingWithoutACommandState()
    {
        var (session, _) = Make();

        Assert.False(session.Tick(10, false));
        Assert.False(session.SelfActivate());
    }

    [Fact]
    public void CancelEndsTheStateAndALaterGiveIsIgnored()
    {
        var (session, store) = Make();
        session.Start("ann", new SettlerFacts(true));

        session.Cancel();

        Assert.Equal(CommandOutcome.Ignored, session.Give(Crop).Outcome);
        Assert.True(store.IsEmpty);
    }

    [Fact]
    public void ASessionNeedsAStore()
    {
        Assert.Throws<ArgumentNullException>(() => new SettlerCommandSession(null!));
    }
}
