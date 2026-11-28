using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Flow;
using OpenCommonwealth.Services.Ui.MainMenu;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PauseFlowSaveTests
{
    [Fact]
    public void No_save_entry_until_a_writer_is_connected()
    {
        var model = new PauseFlowModel();
        model.Open(true);
        Assert.DoesNotContain(model.Entries, e => e.Action == PauseAction.SaveGame);
    }

    [Fact]
    public void Save_entry_sits_after_resume_and_opens_the_save_state()
    {
        var model = new PauseFlowModel { SaveAvailable = true };
        model.Open(false);
        Assert.Equal(PauseAction.SaveGame, model.Entries[1].Action);
        Assert.True(model.Select(PauseAction.SaveGame));
        Assert.Equal(PauseAction.SaveGame, model.Activate()!.Action);
        Assert.Equal(PauseFlowState.Save, model.State);
        Assert.True(model.Back());
        Assert.Equal(PauseFlowState.Pause, model.State);
    }

    [Fact]
    public void Refresh_keeps_the_selected_save_entry()
    {
        var model = new PauseFlowModel { SaveAvailable = true };
        model.Open(false);
        model.Select(PauseAction.SaveGame);
        model.Refresh(true);
        Assert.Equal(PauseAction.SaveGame, model.Selected.Action);
    }

    [Fact]
    public void Accepting_new_save_reaches_the_connected_writer()
    {
        var written = new List<string>();
        Func<string, bool>? write = id => { written.Add(id); return true; };
        var ctrl = NamedSaveWriter.Controller(new InMemorySaveStore(), () => write);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "save_1" }, written);
        Assert.True(ctrl.IsFinished);
    }

    [Fact]
    public void A_writer_reset_while_the_list_is_open_blocks_the_save_without_throwing()
    {
        var written = new List<string>();
        Func<string, bool>? write = id => { written.Add(id); return true; };
        var ctrl = NamedSaveWriter.Controller(new InMemorySaveStore(), () => write);
        write = null;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Empty(written);
        Assert.False(ctrl.IsFinished);
        Assert.Contains(ctrl.DetailLines, l => l.Text == SaveWriterGate.UnavailableText);
    }
}
