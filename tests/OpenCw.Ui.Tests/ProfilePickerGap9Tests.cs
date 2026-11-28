using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Service;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ProfilePickerGap9Tests
{
    static (ProfilePickerModel Model, InMemoryProfileStore Store) WithWork(bool inGame = false)
    {
        var store = new InMemoryProfileStore();
        var model = new ProfilePickerModel(store, inGame);
        Assert.True(model.Create("Work"));
        return (model, store);
    }

    static string[] Names(ProfilePickerModel model) => model.View.Inner.Rows.Select(p => p.Name).ToArray();

    static string Detail(ProfilePickerModel model) => model.DetailText.Replace("\r\n", "\n");

    [Fact]
    public void Toggling_to_the_deleted_list_swaps_the_rows_clears_the_status_and_toggles_back()
    {
        var (model, store) = WithWork();
        Assert.True(model.RequestDelete());
        Assert.True(model.ConfirmPending());
        Assert.Equal("Moved \"Work\" to Deleted.", model.Status);

        model.ToggleDeleted();

        Assert.True(model.ShowingDeleted);
        Assert.Equal("", model.Status);
        Assert.Equal(new[] { "Work" }, Names(model));
        Assert.Equal("Work (deleted)", model.View.RowText(0));
        Assert.Equal("1 deleted profile(s)", model.Summary);
        Assert.Equal("Work\nState: deleted (can be restored)", Detail(model));

        model.ToggleDeleted();

        Assert.False(model.ShowingDeleted);
        Assert.Equal(new[] { "Default" }, Names(model));
        Assert.Equal("1 profile(s), active: Default", model.Summary);
        Assert.Equal("default", store.ActiveId);
    }

    [Fact]
    public void Toggling_to_an_empty_deleted_list_shows_the_empty_text()
    {
        var model = new ProfilePickerModel(new InMemoryProfileStore());

        model.ToggleDeleted();

        Assert.Empty(Names(model));
        Assert.Null(model.Selected);
        Assert.Equal("0 deleted profile(s)", model.Summary);
        Assert.Equal("No deleted profiles.", Detail(model));
    }

    [Fact]
    public void Toggling_is_ignored_while_a_confirmation_is_pending()
    {
        var (model, _) = WithWork();
        Assert.True(model.RequestDelete());
        var status = model.Status;

        model.ToggleDeleted();

        Assert.False(model.ShowingDeleted);
        Assert.Equal(ProfilePending.Delete, model.Pending);
        Assert.Equal(status, model.Status);
        Assert.Equal(new[] { "Default", "Work" }, Names(model));
    }

    [Fact]
    public void Requesting_a_delete_records_the_selected_profile_as_pending_and_cancelling_clears_it()
    {
        var (model, _) = WithWork();
        Assert.Null(model.PendingProfile);
        Assert.Equal(ProfilePending.None, model.Pending);

        Assert.True(model.RequestDelete());

        Assert.Equal(ProfilePending.Delete, model.Pending);
        Assert.Equal(new ProfileInfo("p1", "Work"), model.PendingProfile);
        Assert.Equal(
            "Delete \"Work\"? It moves to Deleted and can be restored. Press Accept to confirm or Back to cancel.",
            model.Status);

        model.CancelPending();

        Assert.Equal(ProfilePending.None, model.Pending);
        Assert.Null(model.PendingProfile);
        Assert.Equal("Cancelled.", model.Status);
    }

    [Fact]
    public void Confirming_a_delete_moves_the_pending_profile_even_when_the_selection_has_moved()
    {
        var (model, store) = WithWork();
        Assert.True(model.RequestDelete());
        Assert.True(model.View.Inner.Select(0));
        Assert.Equal("Default", model.Selected?.Name);

        Assert.True(model.ConfirmPending());

        Assert.Equal(ProfilePending.None, model.Pending);
        Assert.Null(model.PendingProfile);
        Assert.Equal(new[] { "Default" }, Names(model));
        Assert.Equal(new[] { "Work" }, store.Deleted().Select(p => p.Name).ToArray());
        Assert.Equal("Moved \"Work\" to Deleted.", model.Status);
    }

    [Fact]
    public void Confirming_a_delete_that_the_store_rejects_reports_the_failure_and_clears_the_pending_state()
    {
        var (model, store) = WithWork();
        Assert.True(model.RequestDelete());
        store.FailWrites = true;

        Assert.False(model.ConfirmPending());

        Assert.Equal(ProfilePending.None, model.Pending);
        Assert.Null(model.PendingProfile);
        Assert.Equal("Delete failed: The profile data could not be written.", model.Status);
        Assert.Equal(new[] { "Default", "Work" }, Names(model));
        Assert.Empty(store.Deleted());
    }

    [Fact]
    public void Confirming_an_in_game_switch_activates_the_pending_profile_and_announces_it()
    {
        var (model, store) = WithWork(inGame: true);
        var chosen = new List<ProfileInfo>();
        model.ProfileChosen += chosen.Add;
        Assert.True(model.Choose());
        Assert.Equal(ProfilePending.Switch, model.Pending);
        Assert.Equal(new ProfileInfo("p1", "Work"), model.PendingProfile);
        Assert.Equal("default", store.ActiveId);
        Assert.Empty(chosen);

        Assert.True(model.ConfirmPending());

        Assert.Equal(ProfilePending.None, model.Pending);
        Assert.Null(model.PendingProfile);
        Assert.Equal("p1", store.ActiveId);
        Assert.Equal(new[] { "p1" }, store.ChosenIds.ToArray());
        Assert.Equal(new[] { new ProfileInfo("p1", "Work") }, chosen.ToArray());
        Assert.Equal("Switched to \"Work\".", model.Status);
    }

    [Fact]
    public void Confirming_a_switch_the_store_refuses_reports_the_failure_and_keeps_the_active_profile()
    {
        var (model, store) = WithWork(inGame: true);
        var chosen = new List<ProfileInfo>();
        model.ProfileChosen += chosen.Add;
        Assert.True(model.Choose());
        store.FailWrites = true;

        Assert.False(model.ConfirmPending());

        Assert.Equal("default", store.ActiveId);
        Assert.Empty(chosen);
        Assert.Equal("Could not switch profile: The profile data could not be written.", model.Status);
        Assert.Equal(ProfilePending.None, model.Pending);
    }

    [Fact]
    public void Confirming_with_nothing_pending_does_nothing()
    {
        var (model, store) = WithWork();
        var status = model.Status;

        Assert.False(model.ConfirmPending());

        Assert.Equal(status, model.Status);
        Assert.Equal(ProfilePending.None, model.Pending);
        Assert.Null(model.PendingProfile);
        Assert.Equal(new[] { "Default", "Work" }, Names(model));
        Assert.Empty(store.Deleted());
        Assert.Empty(store.ChosenIds);
    }
}
