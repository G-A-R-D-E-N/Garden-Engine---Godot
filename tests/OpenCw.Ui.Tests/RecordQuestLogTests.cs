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
    public void Running_quests_hide_objectives_until_runtime_tracks_them()
    {
        var objectives = new[] { new QuestObjective("10", "Find the door"), new QuestObjective("20", "Open it", Optional: true) };
        var snapshot = new QuestSnapshot("q", "Find Shaun", false, 40, new[] { Log(40, true, "Head south") }, objectives, true);
        Assert.Empty(RecordQuestLog.VisibleObjectives(snapshot));
        var model = RecordQuestLog.Build(new[] { snapshot });
        var quest = Assert.Single(model.Quests.Visible);
        Assert.Empty(quest.Objectives);
        Assert.Equal("q", model.TrackedId);
        Assert.Equal("quest:q", model.CurrentKey);
    }

    [Fact]
    public void Completed_snapshots_expose_objectives_marked_done()
    {
        var objectives = new[] { new QuestObjective("10", "Find the door"), new QuestObjective("20", "Open it", Optional: true) };
        var visible = RecordQuestLog.VisibleObjectives(new QuestSnapshot("q", "Find Shaun", true, 200, new[] { Log(200, true, "Done") }, objectives, true));
        Assert.Equal(new[] { "10", "20" }, visible.Select(o => o.Id).ToArray());
        Assert.All(visible, o => Assert.True(o.Completed));
        Assert.True(visible[1].Optional);
    }

    [Fact]
    public void Completed_quests_mark_their_objectives_completed()
    {
        var objectives = new[] { new QuestObjective("10", "Find the door"), new QuestObjective("20", "Open it", Optional: true) };
        var model = RecordQuestLog.Build(new[]
        {
            new QuestSnapshot("q", "Find Shaun", true, 200, new[] { Log(200, true, "Done") }, objectives, true),
        });
        model.SetStatusFilter(QuestStatusFilter.Completed);
        var quest = Assert.Single(model.Quests.Visible);
        Assert.All(quest.Objectives, o => Assert.True(o.Completed));
        Assert.True(quest.Objectives[1].Optional);
        Assert.Null(model.TrackedId);
    }

    [Fact]
    public void TrackedChanged_fires_on_track_and_untrack()
    {
        var model = RecordQuestLog.Build(new[]
        {
            Snap("a", "Alpha", false, 10, Log(10, true, "first")),
            Snap("b", "Beta", false, 20, Log(20, true, "second")),
        });
        var events = new System.Collections.Generic.List<QuestEvent>();
        model.TrackedChanged += events.Add;

        Assert.True(model.Select("quest:a"));
        model.TrackCurrent();
        model.TrackCurrent();

        Assert.Equal(new[] { new QuestEvent(QuestEventKind.Track, "a"), new QuestEvent(QuestEventKind.Untrack, "a") }, events);
    }

    [Fact]
    public void Tracked_quest_id_needs_an_active_uncompleted_quest_with_a_row()
    {
        var objectives = System.Array.Empty<QuestObjective>();
        Assert.Equal("live", RecordQuestLog.TrackedQuestId(new[]
        {
            new QuestSnapshot("done", "Completed", true, 200, new[] { Log(200, true, "over") }, objectives, true),
            new QuestSnapshot("idle", "Inactive", false, 10, new[] { Log(10, true, "go") }),
            new QuestSnapshot("live", "Active", false, 20, new[] { Log(20, true, "go") }, objectives, true),
            new QuestSnapshot("later", "Second", false, 30, new[] { Log(30, true, "go") }, objectives, true),
        }));
        Assert.Null(RecordQuestLog.TrackedQuestId(new[]
        {
            new QuestSnapshot("nameless", null, false, 10, new[] { Log(10, true, "go") }, objectives, true),
            new QuestSnapshot("logless", "No logs", false, 10, System.Array.Empty<QuestStageLog>(), objectives, true),
        }));
    }

    [Fact]
    public void Build_tracks_the_first_active_quest_that_still_has_a_row()
    {
        var objectives = System.Array.Empty<QuestObjective>();
        var model = RecordQuestLog.Build(new[]
        {
            new QuestSnapshot("unnamed", null, false, 10, new[] { Log(10, true, "drop me") }, objectives, true),
            new QuestSnapshot("second", "Second", false, 20, new[] { Log(20, true, "go") }, objectives, true),
            new QuestSnapshot("third", "Third", false, 30, new[] { Log(30, true, "go") }, objectives, true),
        });
        Assert.Equal("second", model.TrackedId);
        Assert.Equal(DataPage.Quests, model.Page);
        Assert.Equal("quest:second", model.CurrentKey);
    }

    [Fact]
    public void Build_leaves_the_tracked_quest_unset_when_none_is_active()
    {
        var model = RecordQuestLog.Build(new[]
        {
            new QuestSnapshot("done", "Completed", true, 200, new[] { Log(200, true, "over") }, null, true),
            new QuestSnapshot("idle", "Inactive", false, 10, new[] { Log(10, true, "go") }, null, false),
        });
        Assert.Null(model.TrackedId);
        Assert.Equal(DataPage.Quests, model.Page);
    }
}
