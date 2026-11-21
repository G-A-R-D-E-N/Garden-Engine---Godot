using OpenCommonwealth.Services.Ui.Flow;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PauseFlowCharacterTests
{
    static PauseFlowModel At(PauseAction action)
    {
        var model = new PauseFlowModel();
        Assert.True(model.Open(false));
        Assert.True(model.Select(action));
        return model;
    }

    [Theory]
    [InlineData(PauseAction.Character, PauseFlowState.Character)]
    [InlineData(PauseAction.Appearance, PauseFlowState.Appearance)]
    public void Activating_a_character_entry_moves_to_its_state_and_returns_the_entry(PauseAction action, PauseFlowState state)
    {
        var model = At(action);

        Assert.Equal(action, model.Activate()?.Action);
        Assert.Equal(state, model.State);
    }

    [Theory]
    [InlineData(PauseAction.Character)]
    [InlineData(PauseAction.Appearance)]
    public void Back_from_a_character_state_returns_to_the_pause_menu_then_closes(PauseAction action)
    {
        var model = At(action);
        model.Activate();

        Assert.True(model.Back());
        Assert.Equal(PauseFlowState.Pause, model.State);
        Assert.Equal(action, model.Selected.Action);
        Assert.True(model.Back());
        Assert.Equal(PauseFlowState.Closed, model.State);
    }

    [Theory]
    [InlineData(PauseAction.Character)]
    [InlineData(PauseAction.Appearance)]
    public void Character_entries_stay_enabled_without_saves(PauseAction action)
    {
        var model = At(action);

        Assert.True(model.Selected.Enabled);
        Assert.Equal("", model.Selected.DisabledReason);
    }

    [Fact]
    public void Character_entries_do_not_open_a_second_flow_while_one_is_active()
    {
        var model = At(PauseAction.Character);
        model.Activate();

        Assert.Null(model.Activate());
        Assert.False(model.Open(false));
        Assert.Equal(PauseFlowState.Character, model.State);
    }
}
