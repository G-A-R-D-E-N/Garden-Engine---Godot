using System.Text;
using OpenCommonwealth.Services.Papyrus.Save;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class GameScriptSaveTests
{
    private static Dictionary<string, byte[]> Quest(string json) =>
        new() { [QuestStateSave.SectionName] = Encoding.UTF8.GetBytes(json) };

    [Fact]
    public void ANullSessionWithNoFailureWritesNoSections()
    {
        Assert.Empty(GameScriptSave.Capture(null, null));
    }

    [Fact]
    public void AFailedSessionRefusesTheCaptureInsteadOfWritingNothing()
    {
        var error = Assert.Throws<InvalidOperationException>(() => GameScriptSave.Capture(null, "no script cache"));
        Assert.Contains("no script cache", error.Message);
    }

    [Fact]
    public void ASaveWithNoScriptSectionsRestoresNothingAndSucceedsWithoutASession()
    {
        var outcome = GameScriptSave.Restore(new Dictionary<string, byte[]> { ["npc.actors.json"] = new byte[] { 1 } }, null);
        Assert.True(outcome.Ok);
        Assert.Empty(outcome.Warnings);
    }

    [Fact]
    public void ASaveWithScriptSectionsIsRefusedWhenThereIsNoSession()
    {
        var outcome = GameScriptSave.Restore(Quest("{\"version\":1,\"quests\":[]}"), null);
        Assert.False(outcome.Ok);
        Assert.Contains("not running", outcome.Failure);
    }

    [Fact]
    public void AMalformedScriptSectionFailsTheLoadWithTheSectionNamed()
    {
        var outcome = GameScriptSave.Restore(Quest("{\"version\":9,\"quests\":[]}"), null);
        Assert.False(outcome.Ok);
        Assert.Contains("quest section", outcome.Failure);
    }
}
