using OpenCommonwealth.Services.Physics;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class SpawnSettleTests
{
    [Fact]
    public void NearbyFloorWinsOverARaisedOne() =>
        Assert.Equal(0.05f, SpawnSettle.Resolve(1.6f, 0.05f, 2.0f, true));

    [Fact]
    public void RaisedFloorIsUsedUpToTheFullReach() =>
        Assert.Equal(2.05f, SpawnSettle.Resolve(0f, null, 2.05f, true));

    [Fact]
    public void RaisedFloorPastTheReachIsRejected() =>
        Assert.Null(SpawnSettle.Resolve(0f, null, 2.06f, true));

    [Fact]
    public void BuriedPointSettlesOntoAFloorAboveWithinReach() =>
        Assert.Equal(0.05f, SpawnSettle.Resolve(-1.4f, null, 0.05f, true));

    [Fact]
    public void ReachIsTheDocumentedTwoPointZeroFiveMetres()
    {
        Assert.True(SpawnSettle.RiseWithinReach(0f, 2.05f));
        Assert.False(SpawnSettle.RiseWithinReach(0f, 2.06f));
        Assert.True(SpawnSettle.MaxRise > 1.5f);
    }

    [Fact]
    public void PointOverNoCollisionIsKeptAsAuthored() =>
        Assert.Equal(7.5f, SpawnSettle.Resolve(7.5f, null, null, false));

    [Fact]
    public void PointInAirAboveADistantFloorIsKeptAsAuthored() =>
        Assert.Equal(5f, SpawnSettle.Resolve(5f, null, null, false));

    [Fact]
    public void PointOverUnstandableCollisionIsDiscarded() =>
        Assert.Null(SpawnSettle.Resolve(7.5f, null, null, true));

    [Fact]
    public void SolidBetweenIgnoresTheFloorSurfaceOnTheSameCollider()
    {
        Assert.False(SpawnSettle.SolidBetweenBlocksRaised(0f, 0.05f, true, true, 0.04f));
        Assert.True(SpawnSettle.SolidBetweenBlocksRaised(0f, 0.05f, true, true, -0.2f));
    }

    [Fact]
    public void SolidBetweenBlocksAnotherColliderAboveThePoint() =>
        Assert.True(SpawnSettle.SolidBetweenBlocksRaised(0.05f, 2.1f, true, false, 1.5f));

    [Fact]
    public void RaisedFeetAreSkippedWhenNearbyFloorExists() =>
        Assert.Null(SpawnSettle.RaisedFeetIfAccepted(1.6f, 0.05f, 2.0f, false));

    [Fact]
    public void RaisedFeetAreRejectedWhenSomethingSolidIsBetween() =>
        Assert.Null(SpawnSettle.RaisedFeetIfAccepted(-1.4f, null, 0.05f, true));

    [Fact]
    public void RaisedFeetAreAcceptedThroughAClearPathWithinReach() =>
        Assert.Equal(0.05f, SpawnSettle.RaisedFeetIfAccepted(-1.4f, null, 0.05f, false));

    [Fact]
    public void DiscardProbeDepthMatchesNearbySettleSearch() =>
        Assert.Equal(SpawnSettle.MaxBelow, SpawnSettle.AuthoredDiscardProbeDepth);

    private sealed record Slab(long Id, float Top, float? Under);

    private static float? RunWorld(float spawnY, Slab? floor, Slab? ceiling, bool floorIsSheet)
    {
        SettleFloor? Settle(float above, float below)
        {
            float from = spawnY + above;
            float to = spawnY - below;
            if (floor is not null && floor.Top <= from && floor.Top >= to) return new SettleFloor(floor.Top + 0.05f, floor.Id);
            return null;
        }

        SettleRayHit? RayUp(float fromY, float feetY)
        {
            var hits = new List<SettleRayHit>();
            if (floor is not null && floorIsSheet && floor.Top >= fromY && floor.Top <= feetY) hits.Add(new SettleRayHit(floor.Id, floor.Top));
            if (ceiling?.Under is float under && under >= fromY && under <= feetY) hits.Add(new SettleRayHit(ceiling.Id, under));
            return hits.Count == 0 ? null : hits.OrderBy(h => h.HitY).First();
        }

        bool Collision(float above, float depth)
        {
            float from = spawnY + above;
            float to = spawnY - depth;
            bool Within(float y) => y <= from && y >= to;
            return floor is not null && Within(floor.Top) || ceiling?.Under is float under && Within(under);
        }

        return SpawnSettle.ResolveSpawn(spawnY, Settle, RayUp, Collision);
    }

    private static readonly Slab Floor = new(1, 0f, null);
    private static readonly Slab Roof = new(2, 2.5f, 2.3f);

    [Fact]
    public void SpawnBelowCeilingSettlesToTheNearbyFloor() =>
        Assert.Equal(0.05f, RunWorld(1.6f, Floor, Roof, false));

    [Fact]
    public void SpawnDoesNotSettleOntoTheRoofSlabOverAnOutOfReachFloor() =>
        Assert.Null(RunWorld(2.1f, Floor, Roof, false));

    [Fact]
    public void BuriedSpawnSettlesUpOntoTheSheetFloorAbove() =>
        Assert.Equal(0.05f, RunWorld(-1.4f, Floor, Roof, true));

    [Fact]
    public void BuriedSpawnIsRejectedWhenAnotherColliderIsBetween()
    {
        Assert.Null(RunWorld(-1.4f, Floor, new Slab(3, -0.5f, -0.6f), true));
    }

    [Fact]
    public void SpawnWithNoCollisionAtAllIsKeptAsAuthored() =>
        Assert.Equal(4f, RunWorld(4f, null, null, false));

    [Fact]
    public void SpawnAboveAFloorFartherThanTheSettleDepthIsKeptAsAuthored() =>
        Assert.Equal(6f, RunWorld(6f, Floor, null, true));
}
