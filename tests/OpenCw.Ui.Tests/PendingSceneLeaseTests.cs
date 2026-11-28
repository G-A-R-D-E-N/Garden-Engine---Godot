using OpenCommonwealth.Services.Ui.MainMenu;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PendingSceneLeaseTests
{
    [Fact]
    public void An_adopted_destination_leaves_nothing_to_release()
    {
        var lease = new PendingSceneLease();

        lease.HandToEngine(7);
        lease.Adopted();

        Assert.False(lease.IsArmed);
        Assert.False(lease.ChangeInFlight);
        Assert.Equal(0UL, lease.PendingId);
        Assert.Equal(PendingSceneVerdict.None, lease.ReleaseOrphan(true, false));
    }

    [Fact]
    public void A_superseded_destination_is_freed_once_the_handoff_settles()
    {
        var lease = new PendingSceneLease();

        lease.HandToEngine(7);
        lease.Settled();

        Assert.Equal(PendingSceneVerdict.Free, lease.ReleaseOrphan(true, false));
        Assert.False(lease.IsArmed);
        Assert.False(lease.ChangeInFlight);
        Assert.Equal(PendingSceneVerdict.None, lease.ReleaseOrphan(true, false));
    }

    [Fact]
    public void An_unrelated_scene_change_does_not_release_a_destination_still_in_flight()
    {
        var lease = new PendingSceneLease();

        lease.HandToEngine(7);

        Assert.Equal(PendingSceneVerdict.None, lease.ReleaseOrphan(true, false));
        Assert.Equal(PendingSceneVerdict.None, lease.ReleaseOrphan(true, true));
        Assert.True(lease.IsArmed);
        Assert.True(lease.ChangeInFlight);

        lease.Settled();

        Assert.Equal(PendingSceneVerdict.Free, lease.ReleaseOrphan(true, false));
    }

    [Fact]
    public void Settling_after_the_engine_installed_the_destination_releases_nothing()
    {
        var lease = new PendingSceneLease();

        lease.HandToEngine(7);
        lease.Adopted();
        lease.Settled();

        Assert.False(lease.IsArmed);
        Assert.Equal(PendingSceneVerdict.None, lease.ReleaseOrphan(true, false));
    }

    [Fact]
    public void Leaving_the_tree_during_the_handoff_does_not_take_the_destination_from_the_engine()
    {
        var lease = new PendingSceneLease();

        lease.HandToEngine(7);

        Assert.Equal(PendingSceneVerdict.None, lease.ReleaseOnExit(true, false));
        Assert.Equal(PendingSceneVerdict.None, lease.ReleaseOnExit(true, true));
        Assert.True(lease.IsArmed);
    }

    [Fact]
    public void Leaving_the_tree_after_an_aborted_change_frees_the_orphan()
    {
        var lease = new PendingSceneLease();

        lease.HandToEngine(7);
        lease.Settled();

        Assert.Equal(PendingSceneVerdict.Free, lease.ReleaseOnExit(true, false));
        Assert.False(lease.IsArmed);
        Assert.Equal(PendingSceneVerdict.None, lease.ReleaseOnExit(true, false));
    }

    [Fact]
    public void A_destination_the_engine_already_freed_only_clears_the_lease()
    {
        var lease = new PendingSceneLease();

        lease.HandToEngine(7);
        lease.Settled();

        Assert.Equal(PendingSceneVerdict.Forget, lease.ReleaseOrphan(false, false));
        Assert.False(lease.IsArmed);
    }

    [Fact]
    public void A_destination_already_inside_the_tree_belongs_to_the_engine()
    {
        var lease = new PendingSceneLease();

        lease.HandToEngine(7);
        lease.Settled();

        Assert.Equal(PendingSceneVerdict.Forget, lease.ReleaseOrphan(true, true));
        Assert.False(lease.IsArmed);
    }

    [Fact]
    public void A_retry_after_releasing_an_orphan_tracks_only_the_new_destination()
    {
        var lease = new PendingSceneLease();

        lease.HandToEngine(4);
        lease.Settled();
        Assert.Equal(PendingSceneVerdict.Free, lease.ReleaseOrphan(true, false));

        lease.HandToEngine(9);

        Assert.Equal(9UL, lease.PendingId);
        Assert.Equal(PendingSceneVerdict.None, lease.ReleaseOrphan(true, false));
        Assert.Equal(9UL, lease.PendingId);

        lease.Adopted();

        Assert.False(lease.IsArmed);
        Assert.Equal(PendingSceneVerdict.None, lease.ReleaseOrphan(true, false));
    }

    [Fact]
    public void An_untracked_lease_never_releases_anything()
    {
        var lease = new PendingSceneLease();

        Assert.False(lease.IsArmed);
        Assert.False(lease.ChangeInFlight);
        Assert.Equal(PendingSceneVerdict.None, lease.ReleaseOrphan(true, false));
        Assert.Equal(PendingSceneVerdict.None, lease.ReleaseOnExit(true, false));

        lease.HandToEngine(0);

        Assert.False(lease.IsArmed);
        Assert.Equal(PendingSceneVerdict.None, lease.ReleaseOrphan(true, false));
    }
}