using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyOtherViewTests
{
    static Quest[] Quests() => new[]
    {
        new Quest("q1", "Find the Wanderer", "Main", "Search the old mill",
                  new[] { new QuestObjective("o1", "Reach the mill"), new QuestObjective("o2", "Talk to the guard") }, Recent: 1),
        new Quest("q2", "Clear the Cellar", "Side", "Rats everywhere",
                  new[] { new QuestObjective("a", "Kill the rats") }, Recent: 3),
        new Quest("q3", "Deliver the Parcel", "Side", "A simple errand",
                  new[] { new QuestObjective("d", "Visit the trader", Completed: true) }, Completed: true, Recent: 2),
        new Quest("q4", "Beacon", "Main", "Follow the light", new QuestObjective[0], Recent: 4),
    };

    static RadioModel Radio() => new(new[]
    {
        new RadioStation("s1", "Alpha FM", "Classical"),
        new RadioStation("s2", "Beta Radio", "Swing"),
    });

    [Fact]
    public void Data_tabs_switch_between_quest_status_filters_and_the_notes_page_and_announce_the_change()
    {
        var log = new QuestLogModel(Quests());
        var quests = new QuestListView(log);
        var notes = new NoteListView(log);
        var pageChanges = 0;
        quests.PageChanged += () => pageChanges++;
        notes.PageChanged += () => pageChanges++;

        Assert.Equal(new[] { "Active", "Completed", "All", "Notes" }, quests.Tabs);
        Assert.Equal(3, quests.RowCount);

        quests.SelectTab(1);
        Assert.Equal(1, quests.RowCount);

        quests.SelectTab(3);
        Assert.Equal(DataPage.Notes, log.Page);
        Assert.Equal(3, notes.TabIndex);

        notes.SelectTab(0);
        Assert.Equal(DataPage.Quests, log.Page);
        Assert.Equal(QuestStatusFilter.Active, log.StatusFilter);
        Assert.Equal(3, pageChanges);
    }

    [Fact]
    public void Accepting_or_starring_a_quest_tracks_it_and_the_hint_follows()
    {
        var log = new QuestLogModel(Quests());
        var view = new QuestListView(log);
        var events = new List<QuestEvent>();
        view.Command += events.Add;
        view.Select(1);

        Assert.True(view.Handle(MenuInputAction.Accept));
        Assert.True(view.RowIsFavourite(1));
        Assert.Contains(new MenuHint(MenuInputAction.Accept, "Stop tracking"), view.Hints);
        Assert.Equal("3 quests, tracking Clear the Cellar", view.Summary);

        Assert.True(view.ToggleFavouriteAt(1));
        Assert.False(view.RowIsFavourite(1));
        Assert.Equal(new[] { QuestEventKind.Track, QuestEventKind.Untrack }, events.Select(e => e.Kind));
    }

    [Fact]
    public void Completed_quests_are_viewable_but_cannot_be_tracked()
    {
        var log = new QuestLogModel(Quests());
        var view = new QuestListView(log);
        var events = new List<QuestEvent>();
        view.Command += events.Add;
        view.SelectTab(1);
        view.Select(0);

        Assert.True(view.Handle(MenuInputAction.Accept));
        Assert.Empty(events);
        Assert.Contains(new MenuHint(MenuInputAction.Accept, "View"), view.Hints);
        Assert.DoesNotContain(view.Hints, hint => hint.Action == MenuInputAction.Favourite);
    }

    [Fact]
    public void Quest_sort_keys_map_to_the_model_sort()
    {
        var log = new QuestLogModel(Quests());
        var view = new QuestListView(log);

        view.SortBy(1);

        Assert.Equal(QuestSort.Name, log.Sort);
        Assert.Equal(1, view.SortIndex);
        Assert.Equal("Beacon", view.RowText(0));
    }

    [Fact]
    public void Radio_accept_plays_then_stops_the_cursor_station_and_the_summary_tracks_it()
    {
        var radio = Radio();
        var view = new RadioListView(radio);
        var events = new List<RadioEvent>();
        view.Command += events.Add;
        Assert.Equal("Radio off", view.Summary);

        Assert.True(view.Handle(MenuInputAction.Accept));
        Assert.Equal("Alpha FM (playing)", view.RowText(0));
        Assert.Equal("Playing Alpha FM", view.Summary);

        radio.SetNowPlaying("Some Song");
        Assert.True(view.ToggleMute());
        Assert.Equal("Playing Alpha FM - Some Song (muted)", view.Summary);

        Assert.True(view.Handle(MenuInputAction.Accept));
        Assert.Equal("Radio off", view.Summary);
        Assert.Equal(new[] { RadioEventKind.Play, RadioEventKind.Mute, RadioEventKind.Stop }, events.Select(e => e.Kind));
    }

    [Fact]
    public void Radio_next_and_prev_buttons_change_station_and_report_it()
    {
        var radio = Radio();
        var view = new RadioListView(radio);

        Assert.True(view.Next());
        Assert.Equal("s1", radio.PlayingId);
        Assert.True(view.Next());
        Assert.Equal("s2", radio.PlayingId);
        Assert.True(view.Prev());
        Assert.Equal("s1", radio.PlayingId);
    }

    [Fact]
    public void Search_results_view_lists_hits_with_tab_names_and_accept_chooses_the_selected_hit()
    {
        var view = PipBoySearchView.Create();
        var chosen = new List<int>();
        view.Chosen += chosen.Add;
        var rifle = new PipBoySearchHit(PipBoyTab.Inventory, "a", "Rifle", "Weapons x1");
        view.SetHits(new[] { rifle, new PipBoySearchHit(PipBoyTab.Map, "b", "Rifle shop", "shop") });

        Assert.Equal("Items: Rifle", view.RowText(0));
        Assert.Equal("Map: Rifle shop", view.RowText(1));
        Assert.Equal("2 matches", view.Summary);

        view.Select(1);
        Assert.True(view.Handle(MenuInputAction.Accept));
        Assert.Equal(new[] { 1 }, chosen);

        view.SetHits(new[] { rifle });
        Assert.Equal(0, view.SelectedIndex);

        view.SetHits(Array.Empty<PipBoySearchHit>());
        Assert.Equal("No matches", view.Summary);
        Assert.False(view.Handle(MenuInputAction.Accept));
        Assert.Equal("Stats", PipBoyTabNames.Label(PipBoyTab.Stats));
    }
}
