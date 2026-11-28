using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Ui.Gallery;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Swf;
using Radicle.Interop;
using OpenCommonwealth.Tests;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipboyLiveArtTests
{
    private const string Info = "Menu_mc/BottomBar_mc/Info_mc/";
    private const string Page = "Menu_mc/Header_mc/PageHeader_mc/";
    private const string Carousel = "Menu_mc/Header_mc/TabHeader_mc/AlphaHolder/";

    private sealed class FakeMetrics : ISwfMetrics
    {
        public bool Origin(string path, out double x, out double y)
        {
            (x, y) = path switch
            {
                Page + "Selector_Left" => (10.0, 2.0),
                Page + "Selector_Right" => (200.0, 2.0),
                Page + "LeftBorder" => (0.0, 60.0),
                Page + "RightBorder" => (300.0, 60.0),
                "Menu_mc/Header_mc/TabHeader_mc" => (50.0, 0.0),
                _ => (0.0, 0.0),
            };
            return true;
        }

        public bool TextSpan(string path, string text, out double left, out double right)
        {
            left = path.StartsWith(Page, StringComparison.Ordinal) ? 100 : 0;
            right = left + text.Length * 10;
            return true;
        }
    }

    [Fact]
    public void Prefs_read_the_pipboy_section_floats_and_clamp()
    {
        var c = PipboyPrefs.Parse("[Interface]\nfPipboyEffectColorR=0.5\n[Pipboy]\nfPipboyEffectColorR=0.25\nfPipboyEffectColorG=2\nfPipboyEffectColorB=0\n");
        Assert.Equal(0.25, c.R, 9);
        Assert.Equal(1.0, c.G, 9);
        Assert.Equal(HudColor.Min, c.B, 9);
        Assert.Equal(PipboyPrefs.Default, PipboyPrefs.Parse(null));
        Assert.Equal(0.08, PipboyPrefs.Default.R, 9);
        Assert.Equal(1.0, PipboyPrefs.Default.G, 9);
        Assert.Equal(0.09, PipboyPrefs.Default.B, 9);
        Assert.Equal(PipboyPrefs.Default, PipboyPrefs.Load(Path.Combine(Path.GetTempPath(), "ge-nope-" + Guid.NewGuid().ToString("N") + ".ini")));
    }

    [Fact]
    public void Condition_body_path_uses_the_flag_number()
    {
        Assert.Equal("Interface/Components/ConditionClips/Condition_Body_0.swf", PipboyArt.ConditionBody(0));
        Assert.Equal("Interface/Components/ConditionClips/Condition_Body_3.swf", PipboyArt.ConditionBody(3));
        Assert.Equal("StatusTab_mc/ConditionBoy_mc", PipboyArt.ConditionHost);
    }

    [Fact]
    public void Plans_pick_the_movie_and_the_art_or_drawn_parts()
    {
        var status = PipboyPages.Plan(PipBoyTab.Stats, StatusSubTab.Status, DataPage.Quests)!;
        Assert.Equal(PipboyArt.StatsMovie, status.Movie);
        Assert.False(status.ArtList);
        Assert.True(status.ArtDetail);
        Assert.True(PipboyPages.Plan(PipBoyTab.Stats, StatusSubTab.Perks, DataPage.Quests)!.ArtList);
        var inv = PipboyPages.Plan(PipBoyTab.Inventory, StatusSubTab.Status, DataPage.Quests)!;
        Assert.Equal(PipboyInvArt.Movie, inv.Movie);
        Assert.True(inv.ArtList);
        Assert.False(inv.ArtDetail);
        Assert.Equal(PipboyPages.DataMovie, PipboyPages.Plan(PipBoyTab.Data, StatusSubTab.Status, DataPage.Statistics)!.Movie);
        Assert.Equal(PipboyPages.RadioMovie, PipboyPages.Plan(PipBoyTab.Radio, StatusSubTab.Status, DataPage.Quests)!.Movie);
        Assert.Null(PipboyPages.Plan(PipBoyTab.Data, StatusSubTab.Status, DataPage.Notes));
        Assert.Null(PipboyPages.Plan(PipBoyTab.Map, StatusSubTab.Status, DataPage.Quests));
    }

    [Fact]
    public void Usable_needs_a_plan_and_every_required_node()
    {
        var plan = PipboyPages.Plan(PipBoyTab.Radio, StatusSubTab.Status, DataPage.Quests)!;
        Assert.True(PipboyPages.Usable(plan, _ => true));
        Assert.False(PipboyPages.Usable(plan, p => p != "DeadWave_mc"));
        Assert.False(PipboyPages.Usable(null, _ => true));
    }

    [Fact]
    public void Shell_binds_the_stats_footer_with_the_level_meter_and_drops_the_percent()
    {
        var b = PipboyShell.Bind(PipBoyTab.Stats, [PipBoyTab.Stats], null, new SamplePlayerStatus(), null);
        Assert.Equal("LEVEL 7", b.Text[Info + "LVL_tf"]);
        Assert.Equal(0.5, b.FillX[Info + "XPMeter_mc/Fill_mc"], 9);
        Assert.Contains("Menu_mc/ButtonHintBar_mc", b.Hide);
        Assert.Contains("Menu_mc/Header_mc/LeftTriggerButton_mc", b.Hide);
        Assert.Contains("Menu_mc/Header_mc/RightTriggerButton_mc", b.Hide);
    }

    [Fact]
    public void Shell_hides_unused_footer_fields_and_the_meter_off_the_stats_tab()
    {
        var b = PipboyShell.Bind(PipBoyTab.Radio, [PipBoyTab.Radio], null, new SamplePlayerStatus(), null);
        foreach (var field in PipboyShell.FooterFields)
            Assert.Contains(Info + field, b.Hide);
        Assert.Contains(Info + "XPMeter_mc", b.Hide);
        var data = PipboyShell.Bind(PipBoyTab.Data, [PipBoyTab.Data], null, new SamplePlayerStatus(), null);
        Assert.Equal(PipBoyFooter.Segments(PipBoyTab.Data, new SamplePlayerStatus())[0], data.Text[Info + "HP_tf"]);
        Assert.Contains(Info + "AP_tf", data.Hide);
    }

    [Fact]
    public void Shell_labels_the_pages_and_hides_the_ones_the_menu_does_not_have()
    {
        var b = PipboyShell.Bind(PipBoyTab.Inventory, [PipBoyTab.Stats, PipBoyTab.Inventory], null, null, null);
        Assert.Equal("STAT", b.Text[Page + "STAT_tf"]);
        Assert.Equal("INV", b.Text[Page + "INV_tf"]);
        Assert.Contains(Page + "DATA_tf", b.Hide);
        Assert.Contains(Page + "MAP_tf", b.Hide);
        Assert.Contains(Page + "RADIO_tf", b.Hide);
    }

    [Fact]
    public void Shell_puts_the_selectors_twelve_and_a_half_outside_the_current_page_label()
    {
        var b = PipboyShell.Bind(PipBoyTab.Stats, [PipBoyTab.Stats, PipBoyTab.Inventory], null, null, new FakeMetrics());
        Assert.Equal(new SwfPlace(1, 100 - 12.5 - 10, 0), b.Place[Page + "Selector_Left"]);
        Assert.Equal(new SwfPlace(1, 140 + 12.5 - 200, 0), b.Place[Page + "Selector_Right"]);
        Assert.Equal(2, b.Sprites.Count);
        var left = b.Sprites[0];
        Assert.Equal(0, left.X);
        Assert.Equal(87.5, left.Bar!.Width, 9);
        Assert.Equal(58.5, left.Y, 9);
        var right = b.Sprites[1];
        Assert.Equal(152.5, right.X);
        Assert.Equal(147.5, right.Bar!.Width, 9);
    }

    [Fact]
    public void Shell_carousel_does_not_wrap_and_hides_with_a_single_tab()
    {
        var view = new InventoryListView(GallerySamples.NewInventory());
        var tabs = view.Tabs;
        Assert.True(tabs.Count > 2);
        var b = PipboyShell.Bind(PipBoyTab.Inventory, [PipBoyTab.Inventory], view, null, null);
        Assert.Equal(tabs[0].ToUpperInvariant(), b.Text[Carousel + "Selected/textField_tf"]);
        Assert.Equal(tabs[1].ToUpperInvariant(), b.Text[Carousel + "RightOne/textField_tf"]);
        Assert.Equal(tabs[2].ToUpperInvariant(), b.Text[Carousel + "RightTwo/textField_tf"]);
        Assert.Contains(Carousel + "LeftOne", b.Hide);
        Assert.Contains(Carousel + "LeftTwo", b.Hide);
        var radio = new RadioListView(GallerySamples.NewRadio());
        Assert.Contains("Menu_mc/Header_mc/TabHeader_mc", PipboyShell.Bind(PipBoyTab.Radio, [PipBoyTab.Radio], radio, null, null).Hide);
    }

    [Fact]
    public void Shell_centres_the_carousel_on_the_page_label_and_spaces_the_slots()
    {
        var view = new InventoryListView(GallerySamples.NewInventory());
        var b = PipboyShell.Bind(PipBoyTab.Inventory, [PipBoyTab.Inventory], view, null, new FakeMetrics());
        Assert.Equal(new SwfPlace(1, (100 + 130) / 2.0 - 50, 0), b.Place["Menu_mc/Header_mc/TabHeader_mc"]);
        var selectedWidth = view.Tabs[0].Length * 10.0;
        var rightWidth = view.Tabs[1].Length * 10.0;
        Assert.Equal(selectedWidth / 2 + rightWidth / 2 + 12.5, b.Place[Carousel + "RightOne"].Tx, 9);
        Assert.False(b.Place.ContainsKey(Carousel + "LeftOne"));
    }

    [Fact]
    public void Quests_page_shows_the_objectives_list_with_marks_and_hides_the_other_sub_pages()
    {
        var view = new QuestListView(GallerySamples.NewQuestLog());
        var b = PipboyDataArt.Quests(view);
        Assert.Contains("StatsTab_mc", b.Hide);
        Assert.Contains("WorkshopsTab_mc", b.Hide);
        Assert.Contains("QuestsTab_mc/VaultBoyHolder_mc", b.Hide);
        Assert.Contains("QuestsTab_mc/ObjectivesList_mc", b.Show);
        Assert.Equal("Sample quest with a finished step and an optional step.", b.Text["QuestsTab_mc/Description_tf"]);
        var rows = b.Lists["QuestsTab_mc/ObjectivesList_mc"];
        Assert.Equal(["[x] Objective 1", "[ ] Objective 2", "[ ] Objective 3 (optional)"], rows.Select(r => r.Text));
        Assert.Equal(3, b.Lists["QuestsTab_mc/QuestsList_mc"].Count);
        Assert.True(b.Lists["QuestsTab_mc/QuestsList_mc"][0].Selected);
    }

    [Fact]
    public void Statistics_page_lists_categories_and_the_selected_categorys_values()
    {
        var log = GallerySamples.NewQuestLog();
        log.SetPage(DataPage.Statistics);
        var b = PipboyDataArt.Statistics(new StatisticsListView(log));
        Assert.Contains("StatsTab_mc", b.Show);
        Assert.Contains("QuestsTab_mc", b.Hide);
        Assert.Equal(3, b.Lists["StatsTab_mc/CategoryList_mc"].Count);
        Assert.Equal(["Count 1  12", "Count 2  3.5"], b.Lists["StatsTab_mc/ValueList_mc"].Select(r => r.Text));
        Assert.Contains("StatsTab_mc/ValueList_mc/ScrollUp", b.Hide);
    }

    [Fact]
    public void Workshops_page_draws_the_list_and_leaves_the_card_to_the_drawn_panel()
    {
        var log = GallerySamples.NewQuestLog();
        log.SetPage(DataPage.Workshops);
        var b = PipboyDataArt.Workshops(new WorkshopListView(log));
        Assert.Contains("WorkshopsTab_mc", b.Show);
        Assert.Contains("WorkshopsTab_mc/ItemCard_mc", b.Hide);
        Assert.Equal(3, b.Lists["WorkshopsTab_mc/List_mc"].Count);
    }

    [Fact]
    public void Radio_page_shows_the_wave_only_while_a_station_plays()
    {
        var radio = GallerySamples.NewRadio();
        var view = new RadioListView(radio);
        var idle = PipboyDataArt.Radio(view);
        Assert.Contains("RadioWave_mc", idle.Hide);
        Assert.DoesNotContain("DeadWave_mc", idle.Hide);
        Assert.Equal(4, idle.Lists["List_mc"].Count);
        radio.Play(radio.Stations[0].Id);
        var playing = PipboyDataArt.Radio(view);
        Assert.Contains("RadioWave_mc", playing.Show);
        Assert.Contains("DeadWave_mc", playing.Hide);
    }

    [Fact]
    public void Stats_special_list_is_windowed_to_the_art_rows_not_the_drawn_row_count()
    {
        var status = GallerySamples.NewStatus(new SamplePlayerStatus());
        status.SetSubTab(StatusSubTab.Special);
        var view = new StatusListView(status);
        view.SetViewRows(40);
        var b = PipboyDataArt.Stats(view);
        Assert.Equal(7, b.Lists["SPECIALTab_mc/List_mc"].Count);
        Assert.Contains("SPECIALTab_mc/List_mc", b.Show);
    }

    private static AssetResolver? OpenArchive() => Fo4Data.Assets();

    private static HashSet<string>? Paths(AssetResolver archive, string movie)
    {
        if (archive.Read(movie) is not { } bytes)
            return null;
        using var stage = SwfStage.Read(bytes);
        var paths = new HashSet<string>();
        void Walk(SwfNode n)
        {
            paths.Add(n.Path);
            foreach (var c in n.Children)
                Walk(c);
        }
        Walk(stage.Root);
        return paths;
    }

    [InterfaceArchiveFact]
    public void Every_shell_and_page_node_the_live_layer_needs_exists_in_the_real_movies()
    {
        using var archive = OpenArchive();
        if (archive is null)
            return;
        var shell = Paths(archive, PipboyArt.ShellMovie);
        Assert.NotNull(shell);
        Assert.Empty(PipboyShell.Required.Where(p => !shell!.Contains(p)));
        foreach (var tab in new[] { PipBoyTab.Stats, PipBoyTab.Inventory, PipBoyTab.Data, PipBoyTab.Radio })
            foreach (var sub in Enum.GetValues<StatusSubTab>())
                foreach (var data in Enum.GetValues<DataPage>())
                    if (PipboyPages.Plan(tab, sub, data) is { } plan)
                    {
                        var page = Paths(archive, plan.Movie);
                        Assert.NotNull(page);
                        Assert.Empty(plan.Required.Where(p => !page!.Contains(p)));
                    }
    }

    [InterfaceArchiveFact]
    public void Every_bound_path_of_the_live_page_builders_exists_in_the_real_movies()
    {
        using var archive = OpenArchive();
        if (archive is null)
            return;
        var log = GallerySamples.NewQuestLog();
        var radio = GallerySamples.NewRadio();
        radio.Play(radio.Stations[0].Id);
        var status = GallerySamples.NewStatus(new SamplePlayerStatus());
        var special = GallerySamples.NewStatus(new SamplePlayerStatus());
        special.SetSubTab(StatusSubTab.Special);
        log.SetPage(DataPage.Statistics);
        var stats = PipboyDataArt.Statistics(new StatisticsListView(log));
        log.SetPage(DataPage.Workshops);
        var workshops = PipboyDataArt.Workshops(new WorkshopListView(log));
        log.SetPage(DataPage.Quests);
        var cases = new (string Movie, SwfBindings B)[]
        {
            (PipboyArt.StatsMovie, PipboyDataArt.Stats(new StatusListView(status))),
            (PipboyArt.StatsMovie, PipboyDataArt.Stats(new StatusListView(special))),
            (PipboyInvArt.Movie, PipboyInvArt.Page(new InventoryListView(GallerySamples.NewInventory()))),
            (PipboyPages.DataMovie, PipboyDataArt.Quests(new QuestListView(log))),
            (PipboyPages.DataMovie, stats),
            (PipboyPages.DataMovie, workshops),
            (PipboyPages.RadioMovie, PipboyDataArt.Radio(new RadioListView(radio))),
        };
        foreach (var (movie, b) in cases)
        {
            var paths = Paths(archive, movie);
            Assert.NotNull(paths);
            var bound = b.Hide.Concat(b.Show).Concat(b.Text.Keys).Concat(b.FillX.Keys).Concat(b.Lists.Keys).Distinct().ToList();
            var missing = bound.Where(p => !paths!.Contains(p)).ToList();
            Assert.True(missing.Count == 0, movie + ": " + string.Join(", ", missing));
        }
    }

    [Fact]
    public void The_condition_figure_movies_and_anchors_exist_in_the_real_archive()
    {
        using var archive = OpenArchive();
        if (archive is null)
            return;
        var body = Paths(archive, PipboyArt.ConditionBody(PipboyArt.ConditionBodyFlags));
        Assert.NotNull(body);
        Assert.Contains(PipboyArt.ConditionHeadAnchor, body!);
        Assert.NotNull(Paths(archive, PipboyArt.ConditionHead));
        Assert.Contains(PipboyArt.ConditionHost, Paths(archive, PipboyArt.StatsMovie)!);
    }

    [Fact]
    public void PrefsGarbageValueFallsBackPerChannel()
    {
        var c = PipboyPrefs.Parse("[Pipboy]\nfPipboyEffectColorR=abc\nfPipboyEffectColorG=NaN\nfPipboyEffectColorB=0.5\n");
        Assert.Equal(PipboyPrefs.Default.R, c.R);
        Assert.Equal(PipboyPrefs.Default.G, c.G);
        Assert.Equal(0.5, c.B);
    }

    [Fact]
    public void PrefsMissingSectionIgnoresOtherSections()
    {
        Assert.Equal(PipboyPrefs.Default, PipboyPrefs.Parse("[Interface]\nfPipboyEffectColorR=0.5\nfPipboyEffectColorG=0.5\n"));
    }

    [Fact]
    public void PrefsCrlfFileParses()
    {
        var c = PipboyPrefs.Parse("[General]\r\nx=1\r\n[Pipboy]\r\nfPipboyEffectColorR=0.25\r\nfPipboyEffectColorG=0.5\r\nfPipboyEffectColorB=0.75\r\n");
        Assert.Equal(new HudColor(0.25, 0.5, 0.75), c);
    }

    [Fact]
    public void HitRowMapsPointerToAbsoluteRow()
    {
        Assert.Equal(0, PipboyPages.HitRow(0, 20, 100, 90, 100, 9));
        Assert.Equal(1, PipboyPages.HitRow(0, 20, 100, 90, 115, 9));
        Assert.Equal(8, PipboyPages.HitRow(0, 20, 100, 90, 189.9, 9));
        Assert.Equal(6, PipboyPages.HitRow(5, 20, 100, 90, 115, 9));
        Assert.Equal(-1, PipboyPages.HitRow(0, 20, 100, 90, 99.9, 9));
        Assert.Equal(-1, PipboyPages.HitRow(0, 20, 100, 90, 190, 9));
        Assert.Equal(-1, PipboyPages.HitRow(0, 3, 100, 90, 150, 9));
        Assert.Equal(2, PipboyPages.HitRow(0, 3, 100, 90, 125, 9));
        Assert.Equal(-1, PipboyPages.HitRow(0, 0, 100, 90, 100, 9));
        Assert.Equal(-1, PipboyPages.HitRow(0, 20, 100, 0, 100, 9));
        Assert.Equal(-1, PipboyPages.HitRow(0, 20, 100, 90, 100, 0));
    }

    [Fact]
    public void PlansCarryListAnchorsAndContentTop()
    {
        foreach (var tab in new[] { PipBoyTab.Inventory, PipBoyTab.Radio })
        {
            var plan = PipboyPages.Plan(tab, StatusSubTab.Status, DataPage.Quests)!;
            Assert.True(plan.ArtList);
            Assert.Equal("List_mc", plan.ListAnchor);
            Assert.Contains(plan.ListAnchor, plan.Required);
        }
        Assert.Null(PipboyPages.Plan(PipBoyTab.Stats, StatusSubTab.Status, DataPage.Quests)!.ListAnchor);
        Assert.Equal("SPECIALTab_mc/List_mc", PipboyPages.Plan(PipBoyTab.Stats, StatusSubTab.Special, DataPage.Quests)!.ListAnchor);
        Assert.Equal("QuestsTab_mc/QuestsList_mc", PipboyPages.Plan(PipBoyTab.Data, StatusSubTab.Status, DataPage.Quests)!.ListAnchor);
        Assert.Equal(PipboyPages.RadioContentTop, PipboyPages.Plan(PipBoyTab.Radio, StatusSubTab.Status, DataPage.Quests)!.ContentTop);
        Assert.Equal(PipboyPages.ContentTop, PipboyPages.Plan(PipBoyTab.Inventory, StatusSubTab.Status, DataPage.Quests)!.ContentTop);
    }

    [Fact]
    public void ContentRectSitsBelowRadioWave()
    {
        var radio = PipboyPages.Content(PipboyPages.Plan(PipBoyTab.Radio, StatusSubTab.Status, DataPage.Quests));
        var inv = PipboyPages.Content(PipboyPages.Plan(PipBoyTab.Inventory, StatusSubTab.Status, DataPage.Quests));
        Assert.Equal(PipboyPages.RadioContentTop, radio.Y);
        Assert.Equal(PipboyPages.ContentBottom, radio.Y + radio.Height);
        Assert.Equal(PipboyPages.ContentTop, inv.Y);
        Assert.Equal(PipboyPages.ContentBottom, inv.Y + inv.Height);
        Assert.Equal(PipboyPages.ContentTop, PipboyPages.Content(null).Y);
    }

    [Fact]
    public void PagePathsAndSlotPathsAreDistinctAndRooted()
    {
        var paths = new[] { PipBoyTab.Stats, PipBoyTab.Inventory, PipBoyTab.Data, PipBoyTab.Map, PipBoyTab.Radio }.Select(PipboyShell.PagePath).ToList();
        Assert.Equal(5, paths.Distinct().Count());
        Assert.All(paths, path => Assert.StartsWith("Menu_mc/Header_mc/PageHeader_mc/", path));
        Assert.EndsWith("STAT_tf", paths[0]);
        var slots = PipboyShell.Slots.Select(slot => PipboyShell.SlotPath(slot.Slot)).ToList();
        Assert.Equal(PipboyShell.Slots.Length, slots.Distinct().Count());
        Assert.All(slots, path => Assert.EndsWith("/textField_tf", path));
        Assert.Contains(PipboyShell.Slots, slot => slot.Offset == 0);
    }

    [Fact]
    public void PersistentWindowKeepsItsFirstRowWhenAVisibleRowIsSelected()
    {
        var items = Enumerable.Range(0, 20).Select(i => new InventoryItem($"i{i:00}", $"Item i{i:00}", "Cat A", 1, 10, 1, false)).ToArray();
        var view = new InventoryListView(new InventoryModel(items));
        var scroll = new SwfScroll();
        view.Select(15);
        var first = SwfScroll.Window(scroll, "List_mc", PipboyDataArt.Rows(view)).First;
        Assert.Equal(7, first);
        foreach (var visible in new[] { 10, 7, 15, 12 })
        {
            view.Select(visible);
            PipboyInvArt.Page(view, scroll);
            Assert.Equal(first, scroll.First("List_mc"));
        }
        view.Select(5);
        PipboyInvArt.Page(view, scroll);
        Assert.Equal(5, scroll.First("List_mc"));
        view.Select(15);
        view.Select(10);
        Assert.Equal(2, SwfBindings.Window(PipboyDataArt.Rows(view)).First);
    }

    private static InventoryListView TwentyItems() =>
        new(new InventoryModel(Enumerable.Range(0, 20).Select(i => new InventoryItem($"i{i:00}", $"Item i{i:00}", "Cat A", 1, 10, 1, false)).ToArray()));

    [Fact]
    public void WheelMovesTheFirstRowOneRowPerNotchAndClamps()
    {
        var scroll = new SwfScroll();
        Assert.Equal(1, scroll.Wheel("List_mc", 20, 1));
        Assert.Equal(2, scroll.Wheel("List_mc", 20, 1));
        Assert.Equal(1, scroll.Wheel("List_mc", 20, -1));
        Assert.Equal(0, scroll.Wheel("List_mc", 20, -1));
        Assert.Equal(0, scroll.Wheel("List_mc", 20, -1));
        for (var i = 0; i < 30; i++)
            scroll.Wheel("List_mc", 20, 1);
        Assert.Equal(11, scroll.First("List_mc"));
        Assert.Equal(0, scroll.Wheel("Short", 5, 1));
        Assert.Equal(0, scroll.Wheel("Empty", 0, 1));
        Assert.Equal(0, scroll.First("Short"));
    }

    [Fact]
    public void WheelClampsTheSelectionIntoTheWindowOnly()
    {
        Assert.Equal(3, PipboyPages.ClampSelection(3, 1, 20, 9));
        Assert.Equal(1, PipboyPages.ClampSelection(0, 1, 20, 9));
        Assert.Equal(9, PipboyPages.ClampSelection(15, 1, 20, 9));
        Assert.Equal(19, PipboyPages.ClampSelection(19, 11, 20, 9));
        Assert.Equal(2, PipboyPages.ClampSelection(2, 0, 3, 9));
        Assert.Equal(-1, PipboyPages.ClampSelection(-1, 4, 20, 9));
        Assert.Equal(0, PipboyPages.ClampSelection(0, 0, 0, 9));
    }

    [Fact]
    public void WheelKeepsTheWindowAndTheHitTestOnTheSameFirstRow()
    {
        var view = TwentyItems();
        var scroll = new SwfScroll();
        PipboyInvArt.Page(view, scroll);
        for (var notch = 1; notch <= 4; notch++)
        {
            var first = scroll.Wheel("List_mc", view.RowCount, 1);
            var keep = PipboyPages.ClampSelection(view.SelectedIndex, first, view.RowCount, SwfBindings.ListRows);
            if (keep != view.SelectedIndex)
                view.Select(keep);
            var window = PipboyInvArt.Page(view, scroll);
            Assert.Equal(notch, scroll.First("List_mc"));
            Assert.Equal(notch, view.SelectedIndex);
            Assert.Equal("Item i" + notch.ToString("00"), window.Lists["List_mc"][0].Text.Split("  ")[0]);
            Assert.Equal(notch, PipboyPages.HitRow(scroll.First("List_mc"), view.RowCount, 100, 90, 100, SwfBindings.ListRows));
        }
        view.Select(8);
        PipboyInvArt.Page(view, scroll);
        scroll.Wheel("List_mc", view.RowCount, 1);
        Assert.Equal(8, PipboyPages.ClampSelection(view.SelectedIndex, 5, view.RowCount, SwfBindings.ListRows));
        Assert.Equal(8, view.SelectedIndex);
        var shown = PipboyInvArt.Page(view, scroll);
        Assert.Equal(5, scroll.First("List_mc"));
        Assert.True(shown.Lists["List_mc"].Any(row => row.Selected));
    }

    [Fact]
    public void WheelWithoutTheSelectionClampIsPulledBackByTheWindow()
    {
        var view = TwentyItems();
        var scroll = new SwfScroll();
        PipboyInvArt.Page(view, scroll);
        scroll.Wheel("List_mc", view.RowCount, 1);
        PipboyInvArt.Page(view, scroll);
        Assert.Equal(0, scroll.First("List_mc"));
    }

    [Fact]
    public void StarCellsSitLeftOfTheListRowByRow()
    {
        var (x, y, w, h) = PipboyPages.StarRect(100, 50, 90, 9, 0);
        Assert.Equal((90.0, 50.0, 10.0, 10.0), (x, y, w, h));
        var third = PipboyPages.StarRect(100, 50, 90, 9, 2);
        Assert.Equal((90.0, 70.0), (third.X, third.Y));
        var rows = Enumerable.Range(0, 9).Select(slot => PipboyPages.StarRect(100, 50, 90, 9, slot)).ToList();
        Assert.Equal(9, rows.Select(r => r.Y).Distinct().Count());
        Assert.All(rows, r => Assert.True(r.X + r.Width <= 100));
        for (var slot = 0; slot < 9; slot++)
            Assert.Equal(slot, PipboyPages.HitRow(0, 20, 50, 90, rows[slot].Y + rows[slot].Height / 2, 9));
    }

    [Fact]
    public void SortLabelNamesTheSortAndItsDirection()
    {
        string[] names = ["Name", "Weight"];
        Assert.Equal("Sort: Name ^", PipboyPages.SortLabel(names, 0, false));
        Assert.Equal("Sort: Weight v", PipboyPages.SortLabel(names, 1, true));
        Assert.Equal("Sort", PipboyPages.SortLabel(names, -1, false));
        Assert.Equal("Sort", PipboyPages.SortLabel(names, 2, false));
        Assert.Equal("Sort", PipboyPages.SortLabel([], 0, false));
        Assert.True(TwentyItems().SortNames.Count > 0);
    }

    [Fact]
    public void StarsFollowTheListTypeNotTheSelection()
    {
        var view = TwentyItems();
        Assert.IsAssignableFrom<IFavouriteListView>(view);
        Assert.Contains(MenuInputAction.Favourite, view.Hints.Select(h => h.Action));
        view.Select(-1);
        Assert.IsAssignableFrom<IFavouriteListView>(view);
        Assert.IsAssignableFrom<IFavouriteListView>(new QuestListView(GallerySamples.NewQuestLog()));
        Assert.IsNotAssignableFrom<IFavouriteListView>(new RadioListView(GallerySamples.NewRadio()));
        Assert.IsNotAssignableFrom<IFavouriteListView>(new StatusListView(GallerySamples.NewStatus(new SamplePlayerStatus())));
    }

    [Fact]
    public void WheelOnlyAppliesToListsLongerThanTheWindow()
    {
        Assert.False(PipboyPages.CanScroll(0, 9));
        Assert.False(PipboyPages.CanScroll(9, 9));
        Assert.True(PipboyPages.CanScroll(10, 9));
    }
}
