using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.MainMenu;
using Xunit;

namespace OpenCw.Ui.Tests;

public class NamedSaveWriterTests
{
    static NamedSaveWriter Make(InMemorySaveStore saves, List<string> written, bool ok = true) =>
        new(saves, id => { written.Add(id); return ok; });

    [Fact]
    public void New_saves_take_the_first_free_number()
    {
        var saves = new InMemorySaveStore();
        saves.SaveNew();
        var written = new List<string>();

        Assert.True(Make(saves, written).SaveNew().Ok);

        Assert.Equal(new[] { "save_2" }, written);
    }

    [Fact]
    public void Quicksave_uses_the_quicksave_slot()
    {
        var written = new List<string>();
        Assert.True(Make(new InMemorySaveStore(), written).Quicksave().Ok);
        Assert.Equal(new[] { "quicksave" }, written);
    }

    [Fact]
    public void Overwrite_of_a_missing_slot_fails_without_writing()
    {
        var written = new List<string>();
        var result = Make(new InMemorySaveStore(), written).Overwrite("save_9");
        Assert.False(result.Ok);
        Assert.Equal(NamedSaveWriter.GoneText, result.Reason);
        Assert.Empty(written);
    }

    [Fact]
    public void Overwrite_of_an_existing_slot_writes_it()
    {
        var saves = new InMemorySaveStore();
        saves.SaveNew();
        var written = new List<string>();
        Assert.True(Make(saves, written).Overwrite("save_1").Ok);
        Assert.Equal(new[] { "save_1" }, written);
    }

    [Fact]
    public void A_failed_write_reports_the_standard_failure_text()
    {
        var result = Make(new InMemorySaveStore(), new List<string>(), ok: false).SaveNew();
        Assert.Equal(new OpenCommonwealth.Services.Ui.Menus.ActionResult(false, SaveListController.FailedText), result);
    }

    [Fact]
    public void An_io_error_is_reported_not_thrown()
    {
        var writer = new NamedSaveWriter(new InMemorySaveStore(), _ => throw new System.IO.IOException("disk"));
        Assert.False(writer.Quicksave().Ok);
    }
}
