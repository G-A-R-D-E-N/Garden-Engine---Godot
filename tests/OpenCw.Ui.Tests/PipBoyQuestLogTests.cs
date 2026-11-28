using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyQuestLogTests
{
    static Quest[] Quests() => new[]
    {
        new Quest("q1", "Find the Wanderer", "Main", "Search the old mill",
                  new[]
                  {
                      new QuestObjective("o1", "Reach the mill", Completed: true),
                      new QuestObjective("o2", "Talk to the guard"),
                      new QuestObjective("o3", "Bring some tea", Optional: true),
                  }, Recent: 1),
        new Quest("q2", "Clear the Cellar", "Side", "Rats everywhere",
                  new[] { new QuestObjective("a", "Kill the rats") }, Recent: 3),
        new Quest("q3", "Deliver the Parcel", "Side", "A simple errand",
                  new[] { new QuestObjective("d", "Visit the trader", Completed: true) }, Completed: true, Recent: 2),
        new Quest("q4", "Beacon", "Main", "Follow the light", new QuestObjective[0], Recent: 4),
    };

    static QuestLogModel Make() => new(Quests());

    static string[] Ids(QuestLogModel model) => model.Quests.Visible.Select(q => q.Id).ToArray();

    [Fact]
    public void Starts_with_active_quests_only_newest_first()
    {
        Assert.Equal(new[] { "q4", "q2", "q1" }, Ids(Make()));
    }

    [Fact]
    public void Status_filter_switches_between_active_completed_and_all()
    {
        var model = Make();

        model.SetStatusFilter(QuestStatusFilter.Completed);
        Assert.Equal(new[] { "q3" }, Ids(model));

        model.SetStatusFilter(QuestStatusFilter.All);
        Assert.Equal(new[] { "q4", "q2", "q3", "q1" }, Ids(model));
    }

    [Fact]
    public void Sort_by_name_and_group_and_group_filter_combine_with_the_status_filter()
    {
        var model = Make();
        model.SetStatusFilter(QuestStatusFilter.All);

        model.SetSort(QuestSort.Name);
        Assert.Equal(new[] { "q4", "q2", "q3", "q1" }, Ids(model));

        model.SetSort(QuestSort.Group);
        Assert.Equal(new[] { "q1", "q4", "q2", "q3" }, Ids(model));

        model.SetSort(QuestSort.Recent);
        model.Quests.SetCategory("Side");
        Assert.Equal(new[] { "q2", "q3" }, Ids(model));

        model.SetStatusFilter(QuestStatusFilter.Active);
        Assert.Equal(new[] { "q2" }, Ids(model));
    }

    [Fact]
    public void Tracking_toggles_one_quest_and_refuses_completed_quests_and_the_notes_page()
    {
        var model = Make();
        model.Select("quest:q2");

        Assert.Equal(new QuestEvent(QuestEventKind.Track, "q2"), model.TrackCurrent());
        Assert.Equal("q2", model.TrackedQuest?.Id);

        model.Select("quest:q1");
        Assert.Equal(new QuestEvent(QuestEventKind.Track, "q1"), model.TrackCurrent());
        Assert.Equal("q1", model.TrackedId);

        Assert.Equal(new QuestEvent(QuestEventKind.Untrack, "q1"), model.TrackCurrent());
        Assert.Null(model.TrackedId);

        model.Select("quest:q3");
        Assert.Null(model.TrackCurrent());

        model.SetPage(DataPage.Notes);
        Assert.Null(model.TrackCurrent());
    }

    [Fact]
    public void Notes_can_be_added_with_distinct_ids()
    {
        var model = Make();

        var first = model.AddNote("Safe code", "1234");
        var second = model.AddNote("Shopping", "tea");

        Assert.Equal("note-1", first.Id);
        Assert.Equal("note-2", second.Id);
        Assert.Equal(new[] { "note-1", "note-2" }, model.Notes.Visible.Select(n => n.Id));

        Assert.Equal("note-3", model.AddNote("Again", "z").Id);
    }

    [Fact]
    public void Search_text_filters_quests_and_notes_together()
    {
        var model = Make();
        model.AddNote("Safe code", "1234");

        model.SetSearch("rats");
        Assert.Equal(new[] { "q2" }, Ids(model));
        Assert.Empty(model.Notes.Visible);

        model.SetSearch("code");
        Assert.Empty(model.Quests.Visible);
        Assert.Single(model.Notes.Visible);
    }

    [Fact]
    public void Global_search_finds_quests_by_objective_text_and_marks_completed_ones_and_notes()
    {
        var model = Make();
        model.AddNote("Mill key", "under the mill stone");

        var mill = model.Search(ListSearch.Tokenize("mill"));
        Assert.Equal(new[] { "quest:q1", "note:note-1" }, mill.Select(h => h.Key));
        Assert.Equal("Main", mill[0].Detail);
        Assert.Equal("Note", mill[1].Detail);

        var trader = Assert.Single(model.Search(ListSearch.Tokenize("trader")));
        Assert.Equal(new PipBoySearchHit(PipBoyTab.Data, "quest:q3", "Deliver the Parcel", "Side (completed)"), trader);
    }

    [Fact]
    public void Selecting_a_hidden_quest_widens_the_filters_and_selecting_a_note_switches_page()
    {
        var model = Make();
        model.SetSearch("rats");

        Assert.True(model.Select("quest:q3"));
        Assert.Equal(DataPage.Quests, model.Page);
        Assert.Equal("quest:q3", model.CurrentKey);
        Assert.Equal(QuestStatusFilter.All, model.StatusFilter);

        model.AddNote("Mill key", "x");
        Assert.True(model.Select("note:note-1"));
        Assert.Equal(DataPage.Notes, model.Page);
        Assert.Equal("note:note-1", model.CurrentKey);

        Assert.False(model.Select("bogus"));
        Assert.False(model.Select("quest:none"));
        Assert.False(model.Select("note:none"));
    }
}
