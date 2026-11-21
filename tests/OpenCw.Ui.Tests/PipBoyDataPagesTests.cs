using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyDataPagesTests
{
    static Quest Open(string id, string? target, bool completed = false) =>
        new(id, "Quest " + id, "Group", "Text", new[] { new QuestObjective(id + "1", "Step") }, completed, MapTarget: target);

    static QuestLogModel Log(IEnumerable<Quest> quests, IEnumerable<Workshop>? workshops = null, IEnumerable<StatEntry>? stats = null) =>
        new(quests, null, workshops is null ? null : new WorkshopLog(workshops), stats is null ? null : new StatisticsLog(stats));

    static Workshop[] Shops() => new[]
    {
        new Workshop("w1", "beta", Rating: 1, MapTarget: "m_beta"),
        new Workshop("w2", "Alpha", Rating: -1, Raiders: true),
        new Workshop("w3", "Charlie", Raiders: true),
        new Workshop("w4", "Delta"),
    };

    [Fact]
    public void Workshops_sort_by_name_ordinally_with_the_attention_suffix_beating_the_raider_one()
    {
        var log = Log(new[] { Open("a", null) }, Shops());
        var view = new WorkshopListView(log);

        Assert.Equal(new[] { "Alpha [NEEDS ATTENTION]", "Charlie [RAIDERS]", "Delta", "beta" },
                     Enumerable.Range(0, view.RowCount).Select(view.RowText));
    }

    [Fact]
    public void Workshop_metrics_run_in_the_fixed_order_with_unknown_values_and_rating_words()
    {
        var shop = new Workshop("w", "Site", new[]
        {
            new WorkshopMetric("Water", 4.25, 1),
            new WorkshopMetric("people", 6, -1),
            new WorkshopMetric("Power", null),
            new WorkshopMetric("Beds", 2.256),
        });

        Assert.Equal(new[]
        {
            "People  6  ALERT", "Food  --", "Water  4.25  RISING", "Power  --", "Defense  --", "Beds  2.26", "Happiness  --",
        }, WorkshopLog.MetricLines(shop));
        Assert.Equal(WorkshopLog.MetricNames.Select(name => name + "  --"), WorkshopLog.MetricLines(new Workshop("x", "Empty")));
    }

    [Fact]
    public void An_empty_workshop_page_says_so()
    {
        var log = Log(new[] { Open("a", null) }, new Workshop[0]);
        var view = new WorkshopListView(log);

        Assert.Equal("NO WORKSHOPS", log.Workshops!.EmptyMessage);
        Assert.Equal("NO WORKSHOPS", view.Summary);
        Assert.Equal(0, view.RowCount);
    }

    [Fact]
    public void Statistics_group_by_category_in_first_appearance_order_and_keep_each_entry()
    {
        var stats = new StatisticsLog(new[]
        {
            new StatEntry("k1", "One", "1", "Zed"),
            new StatEntry("k2", "Two", "2", "Alpha"),
            new StatEntry("k3", "Three", "3", "Zed"),
            new StatEntry("k4", "Four", "4"),
        });

        Assert.Equal(new[] { "Zed", "Alpha", "General" }, stats.Categories.Select(c => c.Name));
        Assert.Equal(new[] { "One", "Three" }, stats.Categories[0].Entries.Select(e => e.Label));
        Assert.Equal(string.Empty, stats.EmptyMessage);
        Assert.Equal("NO STATISTICS", new StatisticsLog().EmptyMessage);
    }

    [Fact]
    public void The_statistics_list_shows_categories_and_counts_and_replacing_data_rebuilds_it()
    {
        var log = Log(new[] { Open("a", null) }, null,
                      new[] { new StatEntry("k1", "One", "1", "Zed"), new StatEntry("k2", "Two", "2", "Zed") });
        var view = new StatisticsListView(log);

        Assert.Equal("Zed", view.RowText(0));
        Assert.Equal("2", view.RowDetail(0));
        Assert.Equal("1 categories", view.Summary);

        log.Statistics!.Replace(new[] { new StatEntry("k9", "Nine", "9", "Beta") });

        Assert.Equal("Beta", view.RowText(0));
        Assert.Equal("Beta", view.Selected!.Name);
    }

    [Fact]
    public void The_sub_tab_list_grows_only_with_the_pages_the_log_offers()
    {
        var plain = new QuestListView(Log(new[] { Open("a", null) }));
        var stats = new QuestListView(Log(new[] { Open("a", null) }, null, new[] { new StatEntry("k", "One", "1") }));
        var both = new QuestListView(Log(new[] { Open("a", null) }, Shops(), new[] { new StatEntry("k", "One", "1") }));

        Assert.Equal(new[] { "Active", "Completed", "All", "Notes" }, plain.Tabs);
        Assert.Equal(new[] { "Active", "Completed", "All", "Notes", "Statistics" }, stats.Tabs);
        Assert.Equal(new[] { "Active", "Completed", "All", "Notes", "Workshops", "Statistics" }, both.Tabs);
    }

    [Fact]
    public void Selecting_the_workshop_and_statistics_tabs_switches_the_page_and_back()
    {
        var log = Log(new[] { Open("a", null) }, Shops(), new[] { new StatEntry("k", "One", "1") });
        var view = new QuestListView(log);

        view.SelectTab(4);
        Assert.Equal(DataPage.Workshops, log.Page);
        Assert.Equal(4, view.TabIndex);

        view.SelectTab(5);
        Assert.Equal(DataPage.Statistics, log.Page);
        Assert.Equal(5, view.TabIndex);

        view.SelectTab(0);
        Assert.Equal(DataPage.Quests, log.Page);
        Assert.Equal(0, view.TabIndex);
    }

    [Fact]
    public void Statistics_without_workshops_sit_at_the_fifth_tab()
    {
        var log = Log(new[] { Open("a", null) }, null, new[] { new StatEntry("k", "One", "1") });
        var view = new QuestListView(log);

        view.SelectTab(4);

        Assert.Equal(DataPage.Statistics, log.Page);
        Assert.Equal(4, view.TabIndex);
    }

    [Fact]
    public void Keys_and_search_reach_workshops_and_statistics()
    {
        var log = Log(new[] { Open("a", null) }, Shops(), new[] { new StatEntry("k", "One", "1", "Zed") });

        var hit = Assert.Single(log.Search(ListSearch.Tokenize("charlie")));
        Assert.Equal("workshop:w3", hit.Key);

        Assert.True(log.Select("workshop:w3"));
        Assert.Equal(DataPage.Workshops, log.Page);
        Assert.Equal("workshop:w3", log.CurrentKey);

        Assert.True(log.Select("stats:Zed"));
        Assert.Equal(DataPage.Statistics, log.Page);
        Assert.Equal("stats:Zed", log.CurrentKey);

        Assert.False(log.Select("workshop:nope"));
        Assert.False(log.Select("stats:nope"));
        Assert.Equal(DataPage.Statistics, log.Page);
    }

    [Fact]
    public void Show_on_map_tracks_the_quest_first_then_raises_the_request_each_time()
    {
        var log = Log(new[] { Open("a", "m_a") });
        var view = new QuestListView(log);
        var commands = new List<QuestEvent>();
        var requests = new List<ShowOnMapResult>();
        view.Command += commands.Add;
        view.MapRequested += requests.Add;

        Assert.True(view.ShowOnMap());

        Assert.Equal("a", log.TrackedId);
        Assert.Equal(new[] { new QuestEvent(QuestEventKind.Track, "a") }, commands);
        Assert.Equal(new[] { "m_a" }, requests.Select(r => r.TargetId));

        Assert.True(view.ShowOnMap());

        Assert.Equal("a", log.TrackedId);
        Assert.Single(commands);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public void Show_on_map_is_refused_without_a_target_or_for_a_completed_quest()
    {
        var none = new QuestListView(Log(new[] { Open("a", null) }));
        var done = Log(new[] { Open("a", "m_a", completed: true) });
        done.SetStatusFilter(QuestStatusFilter.All);
        var finished = new QuestListView(done);

        Assert.False(none.ShowOnMap());
        Assert.False(none.Handle(MenuInputAction.Compare));
        Assert.DoesNotContain(none.Hints, h => h.Action == MenuInputAction.Compare);
        Assert.False(finished.ShowOnMap());
        Assert.Null(done.TrackedId);
    }

    [Fact]
    public void The_compare_key_runs_show_on_map_and_its_hint_appears_only_with_a_target()
    {
        var log = Log(new[] { Open("a", "m_a") });
        var view = new QuestListView(log);
        var requests = new List<ShowOnMapResult>();
        view.MapRequested += requests.Add;

        Assert.Contains(new MenuHint(MenuInputAction.Compare, "Show on map"), view.Hints);
        Assert.True(view.Handle(MenuInputAction.Compare));
        Assert.Single(requests);
    }

    [Fact]
    public void A_workshop_shows_on_map_without_tracking_anything()
    {
        var log = Log(new[] { Open("a", "m_a") }, Shops());
        var view = new WorkshopListView(log);
        var requests = new List<ShowOnMapResult>();
        view.MapRequested += requests.Add;
        log.SetPage(DataPage.Workshops);

        Assert.True(view.Select(3));
        Assert.Contains(new MenuHint(MenuInputAction.Compare, "Show on map"), view.Hints);
        Assert.True(view.Handle(MenuInputAction.Accept));

        Assert.Equal(new[] { "m_beta" }, requests.Select(r => r.TargetId));
        Assert.Null(requests[0].Tracked);
        Assert.Null(log.TrackedId);

        Assert.True(view.Select(0));
        Assert.False(view.ShowOnMap());
        Assert.DoesNotContain(view.Hints, h => h.Action == MenuInputAction.Compare);
    }

    [Fact]
    public void A_failed_map_jump_neither_tracks_the_quest_nor_raises_the_request()
    {
        var log = Log(new[] { Open("a", "m_a") });
        var view = new QuestListView(log);
        var commands = new List<QuestEvent>();
        var requests = new List<ShowOnMapResult>();
        view.Command += commands.Add;
        view.MapRequested += requests.Add;
        var asked = new List<string>();
        view.MapJump = target =>
        {
            asked.Add(target);
            return false;
        };

        Assert.False(view.ShowOnMap());

        Assert.Equal(new[] { "m_a" }, asked);
        Assert.Null(log.TrackedId);
        Assert.Empty(commands);
        Assert.Empty(requests);
    }

    [Fact]
    public void A_successful_map_jump_tracks_the_quest_and_raises_the_request()
    {
        var log = Log(new[] { Open("a", "m_a") });
        var view = new QuestListView(log);
        var commands = new List<QuestEvent>();
        var requests = new List<ShowOnMapResult>();
        view.Command += commands.Add;
        view.MapRequested += requests.Add;
        view.MapJump = _ => true;

        Assert.True(view.ShowOnMap());

        Assert.Equal("a", log.TrackedId);
        Assert.Single(commands);
        Assert.Single(requests);
    }

    [Fact]
    public void A_failed_workshop_jump_raises_nothing()
    {
        var log = Log(new[] { Open("a", null) }, Shops());
        var view = new WorkshopListView(log);
        var requests = new List<ShowOnMapResult>();
        view.MapRequested += requests.Add;
        view.MapJump = _ => false;
        log.SetPage(DataPage.Workshops);
        view.Select(3);

        Assert.False(view.ShowOnMap());
        Assert.Empty(requests);
    }

    [Fact]
    public void A_workshop_without_a_target_offers_no_show_on_map_hint_at_all()
    {
        var log = Log(new[] { Open("a", null) }, Shops());
        var view = new WorkshopListView(log);
        log.SetPage(DataPage.Workshops);

        view.Select(3);
        Assert.Contains(view.Hints, h => h.Action == MenuInputAction.Accept && h.Label == "Show on map");

        view.Select(0);
        Assert.DoesNotContain(view.Hints, h => h.Action == MenuInputAction.Accept);
        Assert.DoesNotContain(view.Hints, h => h.Label == "Show on map");
    }
}
