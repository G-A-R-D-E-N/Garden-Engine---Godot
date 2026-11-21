using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class BindingRowsTests
{
    static KeyBindingModel Model(InMemoryFileStore? store = null) => new(DefaultBindableActions.All, store ?? new InMemoryFileStore());

    [Fact]
    public void Every_action_has_two_keyboard_rows_and_two_pad_rows()
    {
        var rows = BindingRows.All(DefaultBindableActions.All).ToList();

        Assert.Equal(DefaultBindableActions.All.Count * 4, rows.Count);
        Assert.Equal(rows.Count, rows.Select(BindingRows.Id).Distinct().Count());
        Assert.Equal(2, rows.Count(r => r.ActionId == "jump" && r.Device == InputDevice.Controller));
        Assert.Equal(2, rows.Count(r => r.ActionId == "jump" && r.Device == InputDevice.Keyboard));
    }

    [Fact]
    public void Ids_round_trip_and_bad_ids_parse_to_nothing()
    {
        var row = new BindingRef("move_forward", InputDevice.Controller, BindingSlot.Secondary);

        Assert.Equal(row, BindingRows.Parse(BindingRows.Id(row)));
        Assert.Null(BindingRows.Parse("move_forward"));
        Assert.Null(BindingRows.Parse("move_forward|Nope|Primary"));
        Assert.Null(BindingRows.Parse(""));
    }

    [Fact]
    public void Labels_tell_the_four_slots_apart()
    {
        string Label(InputDevice d, BindingSlot s) => BindingRows.Label("Jump", new BindingRef("jump", d, s));

        Assert.Equal("Jump", Label(InputDevice.Keyboard, BindingSlot.Primary));
        Assert.Equal("Jump (2nd)", Label(InputDevice.Keyboard, BindingSlot.Secondary));
        Assert.Equal("Jump (pad)", Label(InputDevice.Controller, BindingSlot.Primary));
        Assert.Equal("Jump (pad 2nd)", Label(InputDevice.Controller, BindingSlot.Secondary));
    }

    [Fact]
    public void Text_shows_upper_case_codes_and_none_for_empty_slots()
    {
        var model = Model();

        Assert.Equal("SPACE", BindingRows.Text(model, new BindingRef("jump", InputDevice.Keyboard, BindingSlot.Primary)));
        Assert.Equal("NONE", BindingRows.Text(model, new BindingRef("jump", InputDevice.Keyboard, BindingSlot.Secondary)));
        Assert.Equal("PAD A", BindingRows.Text(model, new BindingRef("jump", InputDevice.Controller, BindingSlot.Primary)));
    }

    [Fact]
    public void Binding_a_free_key_is_saved_in_the_shared_model_file()
    {
        var store = new InMemoryFileStore();
        var model = Model(store);

        var bound = BindingRows.Bind(model, new BindingRef("jump", InputDevice.Keyboard, BindingSlot.Secondary), "G");

        Assert.True(bound);
        Assert.Equal("G", model.Get("jump", InputDevice.Keyboard, BindingSlot.Secondary));
        Assert.Equal(CaptureState.Idle, model.State);
        Assert.Contains("\"G\"", store.Files[KeyBindingModel.FileName]);
    }

    [Fact]
    public void Binding_a_pad_button_changes_only_the_controller_slot()
    {
        var model = Model();

        BindingRows.Bind(model, new BindingRef("jump", InputDevice.Controller, BindingSlot.Secondary), "Pad Start");

        Assert.Equal("Pad Start", model.Get("jump", InputDevice.Controller, BindingSlot.Secondary));
        Assert.Equal("Space", model.Get("jump", InputDevice.Keyboard, BindingSlot.Primary));
        Assert.Equal("Pad A", model.Get("jump", InputDevice.Controller, BindingSlot.Primary));
    }

    [Fact]
    public void Binding_a_taken_key_swaps_with_the_holder()
    {
        var model = Model();

        var bound = BindingRows.Bind(model, new BindingRef("move_forward", InputDevice.Keyboard, BindingSlot.Primary), "S");

        Assert.True(bound);
        Assert.Equal("S", model.Get("move_forward", InputDevice.Keyboard, BindingSlot.Primary));
        Assert.Equal("W", model.Get("move_back", InputDevice.Keyboard, BindingSlot.Primary));
        Assert.Equal(CaptureState.Idle, model.State);
    }

    [Fact]
    public void Binding_the_cancel_key_changes_nothing_and_leaves_no_capture_open()
    {
        var model = Model();

        var bound = BindingRows.Bind(model, new BindingRef("jump", InputDevice.Keyboard, BindingSlot.Primary), "Escape");

        Assert.False(bound);
        Assert.Equal("Space", model.Get("jump", InputDevice.Keyboard, BindingSlot.Primary));
        Assert.Equal(CaptureState.Idle, model.State);
    }

    [Fact]
    public void An_unknown_action_is_refused()
    {
        Assert.False(BindingRows.Bind(Model(), new BindingRef("nope", InputDevice.Keyboard, BindingSlot.Primary), "G"));
    }

    [Fact]
    public void Reset_all_restores_what_a_title_rebind_changed_in_the_same_store()
    {
        var store = new InMemoryFileStore();
        var model = Model(store);
        BindingRows.Bind(model, new BindingRef("jump", InputDevice.Keyboard, BindingSlot.Primary), "G");

        model.ResetAll();

        Assert.Equal("Space", model.Get("jump", InputDevice.Keyboard, BindingSlot.Primary));
        Assert.Equal("Space", Model(store).Get("jump", InputDevice.Keyboard, BindingSlot.Primary));
    }
}
