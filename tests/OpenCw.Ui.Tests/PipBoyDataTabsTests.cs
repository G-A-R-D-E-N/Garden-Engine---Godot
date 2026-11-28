using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyDataTabsTests
{
    static QuestLogModel Log(bool workshops, bool statistics) =>
        new(Enumerable.Empty<Quest>(), null, workshops ? new WorkshopLog() : null,
            statistics ? new StatisticsLog(new[] { new StatEntry("k1", "One", "1", "Zed") }) : null);

    [Fact]
    public void Tab_names_append_workshops_then_statistics_only_when_those_logs_exist()
    {
        Assert.Equal(new[] { "Active", "Completed", "All", "Notes" }, DataTabs.Of(Log(false, false)));
        Assert.Equal(new[] { "Active", "Completed", "All", "Notes", "Workshops" }, DataTabs.Of(Log(true, false)));
        Assert.Equal(new[] { "Active", "Completed", "All", "Notes", "Statistics" }, DataTabs.Of(Log(false, true)));
        Assert.Equal(new[] { "Active", "Completed", "All", "Notes", "Workshops", "Statistics" }, DataTabs.Of(Log(true, true)));
    }

    [Fact]
    public void Quest_pages_index_by_status_filter_and_the_other_pages_follow_the_notes_tab()
    {
        var log = Log(true, true);

        Assert.Equal(0, DataTabs.IndexOf(log));
        log.SetStatusFilter(QuestStatusFilter.Completed);
        Assert.Equal(1, DataTabs.IndexOf(log));
        log.SetStatusFilter(QuestStatusFilter.All);
        Assert.Equal(2, DataTabs.IndexOf(log));

        log.SetPage(DataPage.Notes);
        Assert.Equal(3, DataTabs.IndexOf(log));
        log.SetPage(DataPage.Workshops);
        Assert.Equal(4, DataTabs.IndexOf(log));
        log.SetPage(DataPage.Statistics);
        Assert.Equal(5, DataTabs.IndexOf(log));
    }

    [Fact]
    public void The_statistics_tab_moves_up_one_slot_when_there_is_no_workshops_tab()
    {
        var log = Log(false, true);

        log.SetPage(DataPage.Statistics);

        Assert.Equal(4, DataTabs.IndexOf(log));
    }

    [Fact]
    public void Applying_a_status_tab_sets_the_filter_and_returns_to_the_quests_page()
    {
        var log = Log(true, true);
        log.SetPage(DataPage.Notes);

        DataTabs.Apply(log, 1);

        Assert.Equal(DataPage.Quests, log.Page);
        Assert.Equal(QuestStatusFilter.Completed, log.StatusFilter);

        DataTabs.Apply(log, 2);

        Assert.Equal(QuestStatusFilter.All, log.StatusFilter);
        Assert.Equal(DataPage.Quests, log.Page);
    }

    [Fact]
    public void Applying_the_notes_workshops_and_statistics_tabs_switches_page_by_name()
    {
        var log = Log(false, true);

        DataTabs.Apply(log, 3);
        Assert.Equal(DataPage.Notes, log.Page);

        DataTabs.Apply(log, 4);
        Assert.Equal(DataPage.Statistics, log.Page);

        var both = Log(true, true);
        DataTabs.Apply(both, 4);
        Assert.Equal(DataPage.Workshops, both.Page);
        DataTabs.Apply(both, 5);
        Assert.Equal(DataPage.Statistics, both.Page);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(99)]
    public void Applying_an_out_of_range_tab_changes_nothing(int index)
    {
        var log = Log(false, false);
        log.SetStatusFilter(QuestStatusFilter.Completed);
        log.SetPage(DataPage.Notes);

        DataTabs.Apply(log, index);

        Assert.Equal(DataPage.Notes, log.Page);
        Assert.Equal(QuestStatusFilter.Completed, log.StatusFilter);
    }
}
