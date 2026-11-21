using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.RemoteAccess;
using Xunit;

namespace OpenCw.Ui.Tests;

public class RemoteAccessTests
{
    static (RemoteAccessController Ctrl, List<string> Events) Make(string title = "Vault door", string status = "Ready")
    {
        var events = new List<string>();
        var ctrl = RemoteAccessController.TryCreate(title, status, () => events.Add("done"));
        Assert.NotNull(ctrl);
        ctrl!.Finished += _ => events.Add("finished");
        return (ctrl, events);
    }

    [Fact]
    public void It_shows_the_title_and_status_exactly_as_supplied_with_one_close_row()
    {
        var (ctrl, _) = Make("Tür é Access", "Status: Ready");

        Assert.Equal("Tür é Access", ctrl.Title);
        Assert.Equal("Status: Ready", ctrl.Status);
        Assert.Equal(new[] { new MenuTextLine("Status: Ready") }, ctrl.DetailLines);
        Assert.Equal(1, ctrl.Rows.RowCount);
        Assert.Equal(RemoteAccessController.CloseText, ctrl.Rows.RowText(0));
    }

    [Theory]
    [InlineData(null, "status")]
    [InlineData("", "status")]
    [InlineData("   ", "status")]
    [InlineData("title", null)]
    [InlineData("title", "")]
    [InlineData("title", "  ")]
    public void A_missing_or_blank_title_or_status_is_refused(string? title, string? status)
    {
        Assert.Null(RemoteAccessController.TryCreate(title, status, () => { }));
    }

    [Fact]
    public void A_missing_callback_is_refused()
    {
        Assert.Null(RemoteAccessController.TryCreate("title", "status", null));
    }

    [Fact]
    public void SetStatus_replaces_only_the_status_and_ignores_blank_updates()
    {
        var (ctrl, _) = Make();
        var changes = 0;
        ctrl.Changed += () => changes++;

        Assert.True(ctrl.SetStatus("Connected"));

        Assert.Equal("Vault door", ctrl.Title);
        Assert.Equal("Connected", ctrl.Status);
        Assert.Equal(new[] { new MenuTextLine("Connected") }, ctrl.DetailLines);
        Assert.Equal(1, changes);
        Assert.False(ctrl.SetStatus(null));
        Assert.False(ctrl.SetStatus("   "));
        Assert.Equal("Connected", ctrl.Status);
    }

    [Theory]
    [InlineData(MenuInputAction.Accept)]
    [InlineData(MenuInputAction.Back)]
    public void Accept_and_back_close_once_and_report_through_the_callback(MenuInputAction action)
    {
        var (ctrl, events) = Make();

        Assert.True(ctrl.Handle(action));
        Assert.False(ctrl.Handle(action));
        ctrl.Close();

        Assert.Equal(new[] { "finished", "done" }, events);
        Assert.True(ctrl.IsFinished);
    }

    [Fact]
    public void Updates_after_close_are_ignored_and_other_inputs_are_not_handled()
    {
        var (ctrl, events) = Make();

        Assert.False(ctrl.Handle(MenuInputAction.Down));
        Assert.Empty(events);

        ctrl.Close();

        Assert.False(ctrl.SetStatus("Late"));
        Assert.Equal("Ready", ctrl.Status);
    }

    [Fact]
    public void Close_is_the_only_hint()
    {
        var (ctrl, _) = Make();

        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Close") }, ctrl.Hints);
    }
}
