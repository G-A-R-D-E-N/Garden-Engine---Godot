using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettlementLogFeedTests
{
    static PlayerSettlements Known()
    {
        var known = new PlayerSettlements();
        known.Note("000A:a.esm", "Sanctuary Workshop");
        return known;
    }

    [Fact]
    public void A_measured_settlement_shows_people_and_beds_and_marks_the_rest_as_not_tracked()
    {
        var known = Known();
        known.Measure("000A:a.esm", people: 5, beds: 5);

        var shop = PlayerDataLogs.Workshops(known)!.Items.Single();

        Assert.Equal(new[]
        {
            "People  5", "Food  n/a", "Water  n/a", "Power  n/a", "Defense  n/a", "Beds  5", "Happiness  n/a",
        }, WorkshopLog.MetricLines(shop));
        Assert.Equal(0, shop.Rating);
        Assert.False(shop.Raiders);
    }

    [Fact]
    public void A_settlement_not_yet_measured_shows_unknown_for_people_and_beds_without_an_alert()
    {
        var shop = PlayerDataLogs.Workshops(Known())!.Items.Single();

        Assert.Equal("People  --", WorkshopLog.MetricLines(shop)[0]);
        Assert.Equal("Beds  --", WorkshopLog.MetricLines(shop)[5]);
        Assert.Equal(0, shop.Rating);
        Assert.Equal("Sanctuary Workshop", WorkshopLog.RowText(shop));
    }

    [Fact]
    public void Fewer_beds_than_people_raises_the_bed_alert_and_the_needs_attention_row()
    {
        var known = Known();
        known.Measure("000A:a.esm", people: 4, beds: 2);

        var shop = PlayerDataLogs.Workshops(known)!.Items.Single();

        Assert.Equal("Beds  2  ALERT", WorkshopLog.MetricLines(shop)[5]);
        Assert.Equal(-1, shop.Rating);
        Assert.Equal("Sanctuary Workshop " + WorkshopLog.AttentionWord, WorkshopLog.RowText(shop));
    }

    [Fact]
    public void Measuring_ignores_unknown_settlements_negative_counts_and_keeps_the_other_field()
    {
        var known = Known();

        Assert.False(known.Measure("nope", people: 1));
        Assert.True(known.Measure("000A:a.esm", people: 3));
        Assert.True(known.Measure("000A:a.esm", beds: 2));
        Assert.True(known.Measure("000A:a.esm", people: -1, beds: -5));

        var row = known.All.Single();
        Assert.Equal(((int?)3, (int?)2), (row.People, row.Beds));
        Assert.True(known.Note("000A:a.esm", "Renamed"));
        Assert.Equal(((int?)3, (int?)2), (known.All.Single().People, known.All.Single().Beds));
    }

    [Fact]
    public void A_settlement_with_a_map_position_gets_the_marker_the_feed_resolves_for_it()
    {
        var known = Known();
        known.Place("000A:a.esm", 100, -200);
        var asked = new List<(double X, double Y)>();

        var shop = PlayerDataLogs.Workshops(known, (x, y) => { asked.Add((x, y)); return "marker_a"; })!.Items.Single();

        Assert.Equal("marker_a", shop.MapTarget);
        Assert.Equal(new[] { (100.0, -200.0) }, asked);
    }

    [Fact]
    public void A_settlement_without_a_position_or_without_a_marker_has_no_map_target()
    {
        var unplaced = PlayerDataLogs.Workshops(Known(), (_, _) => "marker_a")!.Items.Single();
        Assert.Null(unplaced.MapTarget);

        var known = Known();
        known.Place("000A:a.esm", 1, 2);
        var unmatched = PlayerDataLogs.Workshops(known, (_, _) => null)!.Items.Single();
        Assert.Null(unmatched.MapTarget);

        var log = new QuestLogModel(Array.Empty<Quest>(), null, new WorkshopLog(new[] { unmatched }), null);
        log.SetPage(DataPage.Workshops);
        Assert.Null(log.CurrentMapTarget);

        var placed = new QuestLogModel(Array.Empty<Quest>(), null, new WorkshopLog(new[] { unmatched with { MapTarget = "marker_a" } }), null);
        placed.SetPage(DataPage.Workshops);
        Assert.Equal("marker_a", placed.CurrentMapTarget);
    }

    [Fact]
    public void Measurements_and_position_round_trip_and_an_older_section_without_them_still_loads()
    {
        var known = Known();
        known.Measure("000A:a.esm", people: 6, beds: 4);
        known.Place("000A:a.esm", 4096.5, -8192);
        known.Note("000B:a.esm", "Plain");

        var loaded = new PlayerSettlements();
        loaded.Apply(known.Sections());

        Assert.Equal(known.All, loaded.All);
        Assert.Equal(new KnownSettlement("000A:a.esm", "Sanctuary Workshop", 6, 4, 4096.5, -8192), loaded.All[0]);
        Assert.Equal(new KnownSettlement("000B:a.esm", "Plain"), loaded.All[1]);

        var older = new PlayerSettlements();
        older.Apply(new Dictionary<string, byte[]> { [PlayerSettlements.SectionName] = Encoding.UTF8.GetBytes("[{\"id\":\"x\",\"name\":\"Old\"}]") });
        Assert.Equal(new KnownSettlement("x", "Old"), older.All.Single());
    }

    [Fact]
    public void Place_refuses_non_finite_positions_and_unknown_settlements()
    {
        var known = Known();

        Assert.False(known.Place("000A:a.esm", double.NaN, 1));
        Assert.False(known.Place("nope", 1, 1));
        Assert.Null(known.All.Single().MapX);
    }

    [Fact]
    public void The_nearest_marker_within_a_cell_is_chosen_and_ties_go_to_the_lower_id()
    {
        var markers = new[]
        {
            new MapMarker("b", "B", "location", 110, 100),
            new MapMarker("a", "A", "location", 90, 100),
            new MapMarker("far", "Far", "location", 100 + WorldMapFeed.CellSize + 1, 100),
            new MapMarker("near", "Near", "location", 101, 100),
        };

        Assert.Equal("near", MapDiscovery.Nearest(markers, 100, 100));
        Assert.Equal("a", MapDiscovery.Nearest(markers.Where(m => m.Id != "near"), 100, 100));
        Assert.Null(MapDiscovery.Nearest(markers.Where(m => m.Id == "far"), 100, 100));
        Assert.Null(MapDiscovery.Nearest(markers, double.NaN, 100));
        Assert.Null(MapDiscovery.Nearest(Array.Empty<MapMarker>(), 0, 0));
    }

    [Fact]
    public void A_settlement_placed_in_another_worldspace_is_not_matched_against_this_ones_markers()
    {
        var known = Known();
        known.Place("000A:a.esm", 100, -200, "0000003C:Fallout4.esm");
        var asked = 0;
        string? Near(double x, double y) { asked++; return "marker_a"; }

        Assert.Null(PlayerDataLogs.Workshops(known, Near, "0000AAAA:Other.esm")!.Items.Single().MapTarget);
        Assert.Equal(0, asked);
        Assert.Equal("marker_a", PlayerDataLogs.Workshops(known, Near, "0000003C:Fallout4.esm")!.Items.Single().MapTarget);

        var loaded = new PlayerSettlements();
        loaded.Apply(known.Sections());
        Assert.Equal("0000003C:Fallout4.esm", loaded.All.Single().World);
    }
}
