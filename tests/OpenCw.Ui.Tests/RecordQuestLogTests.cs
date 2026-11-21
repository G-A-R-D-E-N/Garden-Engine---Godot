using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class RecordQuestLogTests
{
    static QuestStageLog Log(int stage, bool done, string? text) => new(stage, done, text);

    static QuestSnapshot Snap(string id, string? name, bool completed, int stage, params QuestStageLog[] logs) =>
        new(id, name, completed, stage, logs);

    [Fact]
    public void Empty_input_gives_an_empty_model()
    {
        var model = RecordQuestLog.Build(System.Array.Empty<QuestSnapshot>());
        model.SetStatusFilter(QuestStatusFilter.All);
        Assert.Empty(model.Quests.Visible);
    }

    [Fact]
    public void Unnamed_quests_are_dropped()
    {
        var model = RecordQuestLog.Build(new[]
        {
            Snap("a", null, false, 10, Log(10, true, "text")),
            Snap("b", "  ", false, 10, Log(10, true, "text")),
            Snap("c", "Kept", false, 10, Log(10, true, "text")),
        });
        Assert.Equal(new[] { "c" }, model.Quests.Visible.Select(q => q.Id).ToArray());
    }

    [Fact]
    public void Quests_without_any_log_text_are_dropped()
    {
        var model = RecordQuestLog.Build(new[]
        {
            Snap("a", "No logs", false, 10),
            Snap("b", "Blank log", false, 10, Log(10, true, " ")),
            Snap("c", "Future log", false, 10, Log(20, false, "not yet")),
        });
        model.SetStatusFilter(QuestStatusFilter.All);
        Assert.Empty(model.Quests.Visible);
    }

    [Fact]
    public void Description_is_the_highest_done_or_current_stage_with_text()
    {
        var logs = new[] { Log(10, true, "first"), Log(20, true, "second"), Log(30, false, "future"), Log(25, true, null) };
        Assert.Equal("second", RecordQuestLog.CurrentLog(25, logs));
        Assert.Equal("future", RecordQuestLog.CurrentLog(30, logs));
        Assert.Null(RecordQuestLog.CurrentLog(5, new[] { Log(10, false, "later") }));
    }

    [Fact]
    public void Running_and_completed_quests_split_across_the_status_filter()
    {
        var model = RecordQuestLog.Build(new[]
        {
            Snap("run", "Running", false, 10, Log(10, true, "go")),
            Snap("done", "Finished", true, 200, Log(200, true, "over")),
        });
        Assert.Equal(new[] { "run" }, model.Quests.Visible.Select(q => q.Id).ToArray());
        model.SetStatusFilter(QuestStatusFilter.Completed);
        Assert.Equal(new[] { "done" }, model.Quests.Visible.Select(q => q.Id).ToArray());
        model.SetStatusFilter(QuestStatusFilter.All);
        Assert.Equal(2, model.Quests.Visible.Count);
    }

    [Fact]
    public void Entry_carries_name_group_description_and_no_objectives()
    {
        var model = RecordQuestLog.Build(new[] { Snap("q", "Find Shaun", false, 40, Log(40, true, "Head south")) });
        var quest = Assert.Single(model.Quests.Visible);
        Assert.Equal("Find Shaun", quest.Name);
        Assert.Equal("Quests", quest.Group);
        Assert.Equal("Head south", quest.Description);
        Assert.Empty(quest.Objectives);
        Assert.Equal(40, quest.Recent);
    }

    [Fact]
    public void Shown_objectives_list_with_earlier_ones_completed()
    {
        // Detects: a quest whose objectives were dropped, or an earlier objective that must show as completed once a later one is shown.
        var objectives = new[] { new QuestObjectiveLog(10, true, "Find the key"), new QuestObjectiveLog(20, true, "Open the door"), new QuestObjectiveLog(30, false, "Leave"), new QuestObjectiveLog(40, true, " ") };
        var quest = RecordQuestLog.Entries(new[] { new QuestSnapshot("q", "Quest", false, 20, new[] { Log(20, true, "text") }, objectives) }).Single();
        Assert.Equal(new[] { "Find the key", "Open the door" }, quest.Objectives.Select(o => o.Text).ToArray());
        Assert.Equal(new[] { true, false }, quest.Objectives.Select(o => o.Completed).ToArray());
    }

    [Fact]
    public void Completed_quest_completes_every_shown_objective()
    {
        // Detects: a completed quest whose last objective must read as completed.
        var objectives = new[] { new QuestObjectiveLog(10, true, "Only") };
        var quest = RecordQuestLog.Entries(new[] { new QuestSnapshot("q", "Quest", true, 10, new[] { Log(10, true, "text") }, objectives) }).Single();
        Assert.True(Assert.Single(quest.Objectives).Completed);
    }
}
