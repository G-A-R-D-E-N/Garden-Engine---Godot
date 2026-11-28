using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Physics;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class PlayPathGatesTests
{
    [Theory]
    [InlineData(true, true, false, Placement.Place)]
    [InlineData(false, true, true, Placement.NeverResident)]
    [InlineData(false, true, false, Placement.Wait)]
    [InlineData(true, false, false, Placement.Wait)]
    [InlineData(false, false, false, Placement.Wait)]
    [InlineData(true, false, true, Placement.NoFloor)]
    [InlineData(false, false, true, Placement.NeverResident)]
    public void PlacementRequiresAResidentStartCellAndFloor(bool startCellResident, bool floorHit, bool timedOut, Placement expected) =>
        Assert.Equal(expected, PlacementGate.Decide(startCellResident, floorHit, timedOut));

    [Fact]
    public void TheTwoPlacementTimeoutsNameDifferentCauses()
    {
        Assert.NotEqual(PlacementGate.Reason(Placement.NoFloor), PlacementGate.Reason(Placement.NeverResident));
        Assert.Equal("", PlacementGate.Reason(Placement.Place));
        Assert.Equal("", PlacementGate.Reason(Placement.Wait));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void FallRecoveryNeedsTheStartCellAndAFloorHit(bool resident, bool floor)
    {
        Assert.Equal(resident && floor, PlacementGate.CanRecoverFall(resident, floor));
    }

    [Theory]
    [InlineData(true, true, 0, 30, 0, 5, ArrivalDecision.Place)]
    [InlineData(false, false, 0, 30, 0, 5, ArrivalDecision.Wait)]
    [InlineData(false, false, 30, 30, 0, 5, ArrivalDecision.GiveUp)]
    [InlineData(true, false, 0, 30, 5, 5, ArrivalDecision.GiveUp)]
    [InlineData(true, false, 0, 30, 4, 5, ArrivalDecision.Wait)]
    [InlineData(false, true, 0, 30, 0, 5, ArrivalDecision.Wait)]
    public void AnArrivalGivesUpOnlyAtTheFrameBudgetOrAfterWaitingForTheGround(
        bool cellResident, bool groundFound, int waitedFrames, int maxFrames, int residentFrames, int groundFrames,
        ArrivalDecision expected) =>
        Assert.Equal(expected, ArrivalGate.Decide(cellResident, groundFound, waitedFrames, maxFrames, residentFrames, groundFrames));

    [Theory]
    [InlineData(ArrivalDecision.Place, true, true, false, true)]
    [InlineData(ArrivalDecision.Place, true, false, true, true)]
    [InlineData(ArrivalDecision.Place, true, false, false, false)]
    [InlineData(ArrivalDecision.GiveUp, true, true, false, true)]
    [InlineData(ArrivalDecision.GiveUp, true, false, true, true)]
    [InlineData(ArrivalDecision.GiveUp, true, false, false, false)]
    [InlineData(ArrivalDecision.GiveUp, false, true, true, false)]
    [InlineData(ArrivalDecision.Wait, true, true, true, false)]
    public void APlayerIsReleasedOnlyWithAResidentCellAndStandableFloor(ArrivalDecision decision, bool resident, bool landingGround, bool anchorGround, bool expected) =>
        Assert.Equal(expected, ArrivalGate.ShouldReleasePlayer(decision, resident, landingGround, anchorGround));

}
