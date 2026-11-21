using System.Linq;
using OpenCommonwealth.Services.Ui.Flow;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PauseFlowCharacterGateTests
{
    static PauseFlowModel Opened(bool includeCharacter, bool hasSaves = true)
    {
        var model = new PauseFlowModel(includeGallery: true, includeServices: true, includeCharacter: includeCharacter);
        Assert.True(model.Open(hasSaves));
        return model;
    }

    [Fact]
    public void Without_character_the_entries_are_only_the_real_ones()
    {
        var model = Opened(false);

        Assert.Equal(
            new[] { PauseAction.Resume, PauseAction.LoadGame, PauseAction.Settings, PauseAction.Services, PauseAction.Gallery, PauseAction.Quit },
            model.Entries.Select(e => e.Action));
    }

    [Fact]
    public void Without_character_the_character_entries_cannot_be_selected()
    {
        var model = Opened(false);

        Assert.False(model.Select(PauseAction.Character));
        Assert.False(model.Select(PauseAction.Appearance));
        Assert.Equal(PauseAction.Resume, model.Selected.Action);
    }

    [Fact]
    public void Without_character_selection_survives_refresh()
    {
        var model = Opened(false, hasSaves: false);
        Assert.True(model.Select(PauseAction.Settings));

        model.Refresh(true);

        Assert.Equal(PauseAction.Settings, model.Selected.Action);
    }

    [Fact]
    public void Character_entries_default_to_present_and_sit_after_settings()
    {
        var model = new PauseFlowModel();

        Assert.Equal(
            new[] { PauseAction.Resume, PauseAction.LoadGame, PauseAction.Settings, PauseAction.Character, PauseAction.Appearance, PauseAction.Quit },
            model.Entries.Select(e => e.Action));
    }
}
