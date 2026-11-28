using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class UiRecordSessionTests
{
    [Fact]
    public void Absent_records_are_not_live()
    {
        var loaded = new object();
        Assert.False(UiRecordSession.IsLiveRecords(null, null, loaded));
    }

    [Fact]
    public void Records_with_a_null_environment_are_not_live()
    {
        var records = new object();
        Assert.False(UiRecordSession.IsLiveRecords(records, null, new object()));
    }

    [Fact]
    public void Records_backed_by_a_stale_environment_are_not_live()
    {
        var records = new object();
        var stale = new object();
        var loaded = new object();
        Assert.False(UiRecordSession.IsLiveRecords(records, stale, loaded));
    }

    [Fact]
    public void Records_backed_by_the_loaded_environment_are_live()
    {
        var loaded = new object();
        var records = new object();
        Assert.True(UiRecordSession.IsLiveRecords(records, loaded, loaded));
    }

    [Fact]
    public void The_map_feed_refuses_when_no_load_order_is_ready()
    {
        var loaded = new object();
        var records = new object();
        Assert.False(UiRecordSession.IsLiveRecords(false, records, loaded, loaded));
    }

    [Fact]
    public void A_null_loaded_environment_is_never_live()
    {
        var records = new object();
        Assert.False(UiRecordSession.IsLiveEnvironment(records, null));
        Assert.False(UiRecordSession.IsLiveRecords(records, records, null));
    }

    [Fact]
    public void Alias_name_lookup_rejects_a_superseded_records_instance()
    {
        var active = new object();
        var held = new object();
        Assert.False(UiRecordSession.IsSameRecords(held, active));
        Assert.True(UiRecordSession.IsSameRecords(active, active));
    }

    [Fact]
    public void A_stale_session_guard_skips_follow_on_work()
    {
        var loaded = new object();
        var stale = new object();
        var records = new object();
        var ran = false;
        if (UiRecordSession.IsLiveRecords(records, stale, loaded))
            ran = true;
        Assert.False(ran);
    }

    [Fact]
    public void An_unbuilt_alias_session_is_never_current()
    {
        Assert.False(UiRecordSession.IsCurrentSession(-1, -1));
    }

    [Fact]
    public void A_superseded_alias_session_is_not_current()
    {
        Assert.False(UiRecordSession.IsCurrentSession(2, 3));
        Assert.True(UiRecordSession.IsCurrentSession(3, 3));
    }

    [Fact]
    public void A_door_without_records_toggles_only_when_no_load_order_exists()
    {
        Assert.True(DoorVisual.TogglesWithoutRecords(false, false));
    }

    [Fact]
    public void A_door_is_not_toggled_while_a_reload_leaves_the_sources_null()
    {
        Assert.False(DoorVisual.TogglesWithoutRecords(true, false));
        Assert.False(DoorVisual.TogglesWithoutRecords(true, true));
        Assert.False(DoorVisual.TogglesWithoutRecords(false, true));
    }
}
