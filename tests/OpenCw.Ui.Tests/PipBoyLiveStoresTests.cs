using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyLiveStoresTests
{
    static readonly string[] Special = ["Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck"];

    static Func<string, double?> AllStored(string? missing = null, double value = 5) => id => id == missing ? null : value;

    // Regression: the radiation row must stay absent while vitals hold rads, because no maximum is evidenced, instead of a fraction over an invented ceiling.
    [Fact]
    public void Radiation_row_stays_absent_while_no_maximum_is_known()
    {
        var vitals = new PlayerVitals(100, 100, 50, 50);
        vitals.AddRads(250);

        var model = new PlayerStatusModel(new LivePlayerStatus(vitals: vitals));

        Assert.Equal(250.0, vitals.Rads);
        Assert.Null(model.RadiationFraction);
    }

    // Regression: a limb the game has not reported must not show as full health, and a crippled flag must not survive on a limb above 0.
    [Fact]
    public void Limbs_are_unknown_until_set_and_only_a_limb_at_zero_is_crippled()
    {
        var limbs = new PlayerLimbs();
        var model = new PlayerStatusModel(new LivePlayerStatus(limbs: limbs));
        Assert.Empty(model.Condition);

        limbs.Set(LimbKind.LeftLeg, 0, true);
        limbs.Set(LimbKind.Head, 0.5, true);
        limbs.Set(LimbKind.RightArm, double.NaN);

        var rows = model.Condition;
        Assert.Equal(new[] { "limb:head", "limb:leftleg" }, rows.Select(row => row.Key));
        Assert.Equal(("50%", false), (rows[0].Text, rows[0].Crippled));
        Assert.Equal(("CRIPPLED", true), (rows[1].Text, rows[1].Crippled));

        limbs.Set(LimbKind.Head, 7);
        Assert.Equal(1.0, limbs.Get(LimbKind.Head)!.Fraction);
        limbs.Clear();
        Assert.Empty(model.Condition);
    }

    // Regression: a partial SPECIAL sheet (only the stats the player record happens to store) must show nothing, not a misleading subset.
    [Theory]
    [InlineData(null, 5.0, true)]
    [InlineData("Luck", 5.0, false)]
    [InlineData(null, 0.0, false)]
    [InlineData(null, double.NaN, false)]
    public void The_special_sheet_shows_only_when_all_seven_are_stored_and_positive(string? missing, double value, bool shown)
    {
        var rows = PlayerSpecialRows.Build(AllStored(missing, value));

        Assert.Equal(shown ? Special : Array.Empty<string>(), rows.Select(row => row.Id));
    }

    // Regression: SPECIAL names must come from the AVIF records, and the Special sub-tab must exist only when the feed has rows.
    [Fact]
    public void Special_rows_are_named_from_records_and_the_status_model_adds_the_special_tab_on_request()
    {
        var reader = new FakeRecordReader();
        reader.Add("000001:a.esm", "AVIF", "Strength", "Strength Name");
        var rows = PlayerSpecialRows.Build(AllStored());

        var named = new RecordPlayerStatusSource(new LivePlayerStatus(special: rows), reader).Special;
        Assert.Equal("Strength Name", named[0].Name);
        Assert.Equal("Perception", named[1].Name);

        var model = new PlayerStatusModel(new LivePlayerStatus(special: named), null, true, false, true);
        Assert.Equal(new[] { "STATUS", "SPECIAL" }, model.SubTabLabels);
        model.SetSubTab(StatusSubTab.Special);
        Assert.Equal("special:Strength", model.View.Visible.First().Key);
        Assert.Equal(new[] { "STATUS" }, new PlayerStatusModel(new LivePlayerStatus(), null, true).SubTabLabels);
        Assert.Equal(new[] { "STATUS", "SPECIAL", "PERKS" }, new PlayerStatusModel(new LivePlayerStatus(), null, true, true, true).SubTabLabels);
    }

    // Regression: the footer level must come from the supplied value, and the clock, weight and caps must stay at the unknown marker, never a made-up number.
    [Fact]
    public void The_live_footer_reports_stored_values_and_leaves_unsourced_ones_unknown()
    {
        var vitals = new PlayerVitals(100, 40, 50, 10);
        var status = new LivePipBoyStatus(vitals, () => 3, () => 10.0, () => 50.0);

        Assert.Equal(new[] { "HP 40/100", "LEVEL 3", "AP 10/50" }, PipBoyFooter.Segments(PipBoyTab.Stats, status));
        Assert.Equal(new[] { "WT --", "CAPS --" }, PipBoyFooter.Segments(PipBoyTab.Inventory, status));
        Assert.Equal(new[] { "--.--.----", "--:-- --" }, PipBoyFooter.Segments(PipBoyTab.Data, status));
        Assert.Equal(new[] { "HP --/--", "LEVEL --", "AP --/--" }, PipBoyFooter.Segments(PipBoyTab.Stats, new LivePipBoyStatus(null, () => null, () => null, () => null)));
    }

    // Regression: a counter must not rise for an unknown key or a non-positive amount, or a stray call writes a row the Statistics page cannot label.
    [Fact]
    public void Stat_counters_rise_only_for_known_keys_and_positive_amounts()
    {
        var stats = new PlayerStats();

        Assert.False(stats.Add("nonsense"));
        Assert.False(stats.Add(PlayerStats.Kills, 0));
        Assert.False(stats.Add(PlayerStats.Kills, -2));
        Assert.True(stats.IsEmpty);
        Assert.True(stats.Add(PlayerStats.Kills));
        Assert.True(stats.Add(PlayerStats.Kills, 2));
        Assert.Equal(3L, stats.Count(PlayerStats.Kills));
        Assert.Equal(0L, stats.Count(PlayerStats.LocksPicked));
    }

    // Regression: kill and lock counters must survive a save and load, and a load of a save without the section must clear counters gained since.
    [Fact]
    public void Stat_counters_round_trip_through_the_save_section_and_a_missing_section_clears_them()
    {
        var saved = new PlayerStats();
        saved.Add(PlayerStats.Kills, 4);
        saved.Add(PlayerStats.LocksPicked);
        var loaded = new PlayerStats();
        loaded.Apply(saved.Sections());

        Assert.Equal((4L, 1L), (loaded.Count(PlayerStats.Kills), loaded.Count(PlayerStats.LocksPicked)));
        Assert.Empty(new PlayerStats().Sections());
        loaded.Apply(new Dictionary<string, byte[]>());
        Assert.True(loaded.IsEmpty);
    }

    // Regression: a corrupt stats section must be refused whole and leave the live counters alone; a counter this build does not know is dropped, not kept as an unlabeled row.
    [Theory]
    [InlineData("[]")]
    [InlineData("{\"kills\":-1}")]
    [InlineData("{\"kills\":\"many\"}")]
    [InlineData("{\"kills\":null}")]
    [InlineData("not json")]
    public void A_malformed_stats_section_throws_and_keeps_the_current_counters(string payload)
    {
        var stats = new PlayerStats();
        stats.Add(PlayerStats.Kills, 2);

        Assert.Throws<FormatException>(() => stats.Apply(new Dictionary<string, byte[]> { [PlayerStats.SectionName] = Encoding.UTF8.GetBytes(payload) }));
        Assert.Equal(2L, stats.Count(PlayerStats.Kills));

        stats.Apply(new Dictionary<string, byte[]> { [PlayerStats.SectionName] = "{\"future\":9,\"kills\":1}"u8.ToArray() });
        Assert.Equal(1L, stats.Count(PlayerStats.Kills));
        Assert.Single(stats.Sections());
        Assert.DoesNotContain("future", Encoding.UTF8.GetString(stats.Sections()[PlayerStats.SectionName]));
    }

    // Regression: a settlement must be listed once however often its workshop opens, and must survive a save and load.
    [Fact]
    public void Settlements_are_listed_once_per_workshop_and_round_trip_through_the_save_section()
    {
        var known = new PlayerSettlements();

        Assert.False(known.Note("", "Workshop"));
        Assert.False(known.Note("000A:a.esm", " "));
        Assert.True(known.Note("000B:a.esm", "Workshop"));
        Assert.True(known.Note("000A:a.esm", "Workshop"));
        Assert.True(known.Note("000A:a.esm", "Sanctuary Workshop"));
        Assert.Equal(new[] { "000A:a.esm", "000B:a.esm" }, known.All.Select(row => row.Id));

        var loaded = new PlayerSettlements();
        loaded.Apply(known.Sections());
        Assert.Equal(known.All, loaded.All);
        Assert.Throws<FormatException>(() => loaded.Apply(new Dictionary<string, byte[]> { [PlayerSettlements.SectionName] = "[{\"id\":\"x\"}]"u8.ToArray() }));
        Assert.Equal(2, loaded.All.Count);
        loaded.Apply(new Dictionary<string, byte[]>());
        Assert.True(loaded.IsEmpty);
    }

    // Regression: the Data tab pages must be absent while their store is empty, so the shipped Pip-Boy shows nothing instead of a sample list.
    [Fact]
    public void Data_pages_exist_only_for_a_store_with_rows()
    {
        Assert.Null(PlayerDataLogs.Statistics(new PlayerStats()));
        Assert.Null(PlayerDataLogs.Workshops(new PlayerSettlements()));

        var stats = new PlayerStats();
        stats.Add(PlayerStats.Kills, 12);
        var settlements = new PlayerSettlements();
        settlements.Note("000A:a.esm", "Sanctuary Workshop");
        var log = new QuestLogModel(Array.Empty<Quest>(), null, PlayerDataLogs.Workshops(settlements), PlayerDataLogs.Statistics(stats));

        Assert.Equal(new[] { "Sanctuary Workshop" }, log.Workshops!.Items.Select(shop => shop.Name));
        var entry = log.Statistics!.Categories.Single().Entries.Single();
        Assert.Equal(("Actors killed", "12"), (entry.Label, entry.Value));
    }

    static WorldMapData World(params MapMarker[] markers) => new(new MapBounds(-40000, -40000, 40000, 40000), markers);

    // Regression: the local map must hold only the markers within two cells of the player (edge included) and carry the player position, so it is not the whole world nor an empty box.
    [Fact]
    public void The_local_map_is_a_square_around_the_player_holding_only_nearby_markers()
    {
        var data = World(new MapMarker("a", "Near", "location", 100, -200), new MapMarker("b", "Edge", "location", 1000 + WorldMapData.LocalHalfExtent, 1000),
                         new MapMarker("c", "Far", "location", 1001 + WorldMapData.LocalHalfExtent, 1000));

        var local = data.NewLocalModel(1000, 1000)!;

        Assert.Equal(new MapBounds(1000 - WorldMapData.LocalHalfExtent, 1000 - WorldMapData.LocalHalfExtent, 1000 + WorldMapData.LocalHalfExtent, 1000 + WorldMapData.LocalHalfExtent), local.Bounds);
        Assert.Equal(new[] { "Edge", "Near" }, local.VisibleMarkers.Select(marker => marker.Name).OrderBy(name => name));
        Assert.Equal(1000.0, local.PlayerX);
        Assert.Equal(1000.0, local.PlayerY);
        Assert.Null(data.NewLocalModel(double.NaN, 0));
        Assert.Equal(3, data.NewModel().VisibleMarkers.Count());
    }

    // Regression: the level steps must land on the cumulative curve (200, 475, 825) and the level must rise exactly at each threshold, not one point early or late.
    [Fact]
    public void The_xp_curve_totals_and_levels_meet_at_each_threshold()
    {
        var curve = XpCurve.Default;

        Assert.Equal(new long[] { 0, 200, 475, 825 }, new[] { 1, 2, 3, 4 }.Select(curve.TotalFor));
        Assert.Equal(new long[] { 0, 0 }, new[] { 0, -5 }.Select(curve.TotalFor));
        Assert.Equal(new[] { 1, 2, 2, 3, 1 }, new long[] { 199, 200, 474, 475, 0 }.Select(curve.LevelAt));
        Assert.Equal(0.0, curve.Progress(200, 2));
        Assert.Equal(137.0 / 275, curve.Progress(337, 2), 9);
        Assert.Equal(0.0, curve.Progress(long.MaxValue, XpCurve.MaxLevel));
        Assert.Equal(new long[] { 5, 5, 50 }, new[] { 1, 0, 10 }.Select(XpCurve.KillXp));
    }

    // Regression: the curve must come from the two level settings when both are sound and fall back as a pair otherwise, or a half-read pair would give a level gap of 0.
    [Fact]
    public void The_xp_curve_reads_both_settings_or_keeps_the_default_pair()
    {
        static XpCurve Read(string? baseValue, string? bumpValue)
        {
            var reader = new FakeRecordReader();
            if (baseValue is not null) reader.Add("000001:a.esm", "GMST", XpCurve.BaseSetting).Scalars[RecordCrimeFacts.IntegerPath] = baseValue;
            if (bumpValue is not null) reader.Add("000002:a.esm", "GMST", XpCurve.BumpSetting).Scalars[RecordCrimeFacts.IntegerPath] = bumpValue;
            return XpCurve.Read(reader);
        }

        Assert.Equal(new XpCurve(100, 10), Read("100", "10"));
        Assert.Equal(new XpCurve(100, 0), Read("100", "0"));
        Assert.Equal(XpCurve.Default, Read("100", null));
        Assert.Equal(XpCurve.Default, Read(null, "10"));
        Assert.Equal(XpCurve.Default, Read("0", "10"));
        Assert.Equal(XpCurve.Default, Read("100", "-1"));
        Assert.Equal(XpCurve.Default, Read("100", "x"));
    }

    // Regression: a victim with an unresolved level must award no kill XP, or every template-level or zoned actor pays a made-up 5 XP as if it were level 1.
    [Fact]
    public void An_unresolved_victim_level_awards_no_kill_xp()
    {
        Assert.Equal(50L, XpCurve.KillXpOf(10, null));
        Assert.Null(XpCurve.KillXpOf(1, "template stats"));
        Assert.Null(XpCurve.KillXpOf(0, "zone"));
    }

    // Regression: awards must add only positive amounts, move the level at the threshold and notify once per change, or the footer level drifts from the stored XP.
    [Fact]
    public void Experience_awards_raise_the_level_and_ignore_non_positive_amounts()
    {
        var xp = new PlayerExperience();
        var changes = 0;
        xp.Changed += () => changes++;

        Assert.True(xp.IsEmpty);
        Assert.Equal(1, xp.Level);
        Assert.False(xp.Add(0));
        Assert.False(xp.Add(-10));
        Assert.Equal(0, changes);
        Assert.True(xp.Add(199));
        Assert.Equal(1, xp.Level);
        Assert.True(xp.Add(1));
        Assert.Equal((200L, 2), (xp.Total, xp.Level));
        Assert.Equal(0.0, xp.Progress);
        Assert.Equal(2, changes);
        xp.Add(long.MaxValue);
        Assert.Equal(long.MaxValue, xp.Total);
        xp.Clear();
        Assert.Equal((0L, 1), (xp.Total, xp.Level));
    }

    // Regression: a new curve must re-derive the level from the saved total, or a load-order change leaves a stale level beside the new thresholds.
    [Fact]
    public void A_new_curve_rederives_the_level_from_the_stored_total()
    {
        var xp = new PlayerExperience();
        xp.Add(475);
        Assert.Equal(3, xp.Level);
        var changes = 0;
        xp.Changed += () => changes++;

        xp.Curve = new XpCurve(1000, 0);
        Assert.Equal(1, xp.Level);
        xp.Curve = new XpCurve(1000, 0);
        Assert.Equal(1, changes);
    }

    // Regression: the XP total must survive save and load, a save without the section must reset it, and a corrupt section must not zero or replace the live total.
    [Fact]
    public void Experience_saves_round_trips_and_rejects_malformed_sections()
    {
        var xp = new PlayerExperience();
        Assert.Empty(xp.Sections());
        xp.Add(500);
        var saved = xp.Sections();

        var loaded = new PlayerExperience();
        loaded.Apply(saved);
        Assert.Equal((500L, 3), (loaded.Total, loaded.Level));
        loaded.Apply(new Dictionary<string, byte[]>());
        Assert.True(loaded.IsEmpty);

        loaded.Apply(saved);
        foreach (var bad in new[] { "[]", "{}", "{\"total\":-1}", "{\"total\":\"x\"}", "not json" })
        {
            var sections = new Dictionary<string, byte[]> { [PlayerExperience.SectionName] = Encoding.UTF8.GetBytes(bad) };
            Assert.Throws<FormatException>(() => loaded.Apply(sections));
            Assert.Equal(500L, loaded.Total);
        }
    }

    // Regression: only a current teammate may share XP, and clearing the teammate flag must drop the share, or a dismissed companion keeps awarding kills.
    [Fact]
    public void A_teammate_shares_xp_only_while_it_is_a_teammate()
    {
        var teammates = new PlayerTeammates();

        teammates.SetGivesXp("00A", true);
        Assert.False(teammates.GivesXp("00A"));
        teammates.Set("00A", true);
        teammates.SetGivesXp("00A", true);
        Assert.True(teammates.GivesXp("00A"));
        teammates.SetGivesXp("00A", false);
        Assert.False(teammates.GivesXp("00A"));
        teammates.SetGivesXp("00A", true);
        teammates.Set("00A", false);
        teammates.Set("00A", true);
        Assert.False(teammates.GivesXp("00A"));
        teammates.SetGivesXp("00A", true);
        teammates.Clear();
        Assert.False(teammates.GivesXp("00A"));
    }

    // Regression: the footer must print every sourced field (level progress, weight, caps, date, clock, place) and drop the place when it is blank, not show a fixed or empty segment.
    [Fact]
    public void The_live_footer_reports_every_sourced_field()
    {
        var when = new DateTime(2287, 10, 23, 15, 5, 0);
        var place = "Sanctuary";
        var status = new LivePipBoyStatus(new PlayerVitals(100, 40, 50, 10), () => 3, () => 10.0, () => 50.0,
                                          () => 0.5, () => 12.0, () => 42L, () => when, () => place);

        Assert.Equal("LEVEL 3 50%", PipBoyFooter.Segments(PipBoyTab.Stats, status)[1]);
        Assert.Equal(new[] { "WT 12", "CAPS 42" }, PipBoyFooter.Segments(PipBoyTab.Inventory, status));
        Assert.Equal(new[] { "10.23.2287", "3:05 PM" }, PipBoyFooter.Segments(PipBoyTab.Data, status));
        Assert.Equal(new[] { "10.23.2287", "3:05 PM", "Sanctuary" }, PipBoyFooter.Segments(PipBoyTab.Map, status));
        place = " ";
        Assert.Equal(2, PipBoyFooter.Segments(PipBoyTab.Map, status).Count);
    }

    // Regression: the footer weight must be weight times count, unknown when any record weight is unknown, and recomputed only when the stacks change, or it reads every frame or goes stale.
    [Fact]
    public void Carry_weight_sums_stacks_and_rereads_records_only_when_the_stacks_change()
    {
        var reader = new FakeRecordReader();
        reader.Add("000001:a.esm", "MISC", "Wrench").Scalars["MISC\\Weight"] = "1.5";
        reader.Add("000002:a.esm", "MISC", "Spring").Scalars["MISC\\Weight"] = "0.25";
        reader.Add("000003:a.esm", "MISC", "Mystery");
        var items = new RecordItems(reader);
        var stacks = new List<InventoryStack>();
        var reading = new CarryWeightReading(() => stacks, () => items);

        Assert.Equal(0.0, reading.Weight);
        stacks.Add(new InventoryStack("000001:a.esm", 2));
        stacks.Add(new InventoryStack("000002:a.esm", 4));
        Assert.Equal(4.0, reading.Weight);
        var calls = reader.SignatureCalls;
        Assert.Equal(4.0, reading.Weight);
        Assert.Equal(calls, reader.SignatureCalls);
        stacks[1] = new InventoryStack("000002:a.esm", 8);
        Assert.Equal(5.0, reading.Weight);
        stacks.Add(new InventoryStack("000003:a.esm", 1));
        Assert.Null(reading.Weight);
        Assert.Null(new CarryWeightReading(() => stacks, () => null).Weight);
    }

    // Regression: with Notes not offered the Data tab must list no Notes page, move the later pages up, refuse the page and refuse new notes, so no empty page is shown on a build without a note source.
    [Fact]
    public void The_data_tab_without_notes_drops_the_page_and_refuses_it()
    {
        var log = new QuestLogModel(Enumerable.Empty<Quest>(), null, new WorkshopLog(), new StatisticsLog(new[] { new StatEntry("k1", "One", "1", "Zed") }), false);

        Assert.False(log.NotesOffered);
        Assert.Equal(new[] { "Active", "Completed", "All", "Workshops", "Statistics" }, DataTabs.Of(log));
        log.SetPage(DataPage.Notes);
        Assert.Equal(DataPage.Quests, log.Page);
        log.SetPage(DataPage.Workshops);
        Assert.Equal(3, DataTabs.IndexOf(log));
        DataTabs.Apply(log, 4);
        Assert.Equal(DataPage.Statistics, log.Page);
        DataTabs.Apply(log, 1);
        Assert.Equal((DataPage.Quests, QuestStatusFilter.Completed), (log.Page, log.StatusFilter));
        Assert.Throws<InvalidOperationException>(() => log.AddNote("t", "x"));
    }
}
