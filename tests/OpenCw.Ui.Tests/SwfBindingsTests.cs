using System;
using OpenCommonwealth.Services.Ui.Gallery;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Swf;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SwfBindingsTests
{
    [Theory]
    [InlineData("$TOTAL COST", "TOTAL COST")]
    [InlineData("$MY_ARMOR", "MY ARMOR")]
    [InlineData("110", "110")]
    [InlineData("AP", "AP")]
    public void CleanStripsStringKeyMarker(string input, string expected) =>
        Assert.Equal(expected, SwfBindings.Clean(input));

    [Fact]
    public void PlainTextStripsTagsAndEntities()
    {
        var t = new SwfLayoutTextRecord(null, 20, null, null, null, null, null, null, false, false, false, true, false, "",
            "<p align=\"left\"><font size=\"20\"><b>I&apos;m &amp; ok</b></font></p>", null);
        Assert.Equal("I'm & ok", SwfStage.PlainText(t));
        Assert.Equal("", SwfStage.PlainText(null));
    }

    [Fact]
    public void HudBindsMetersAndAmmo()
    {
        var s = new HudState();
        s.SetHealth(25, 100);
        s.SetActionPoints(40, 80);
        s.SetAmmo(7, 216);
        var b = HudArt.Build(s);
        Assert.Equal(0.25, b.FillX["LeftMeters_mc/HPMeter_mc/MeterBar_mc/MeterBarInternal_mc"], 3);
        Assert.Equal(0.5, b.FillX["RightMeters_mc/ActionPointMeter_mc/MeterBar_mc/MeterBarInternal_mc"], 3);
        Assert.Equal("007", b.Text["RightMeters_mc/AmmoCount_mc/ClipCount_tf"]);
        Assert.Equal("216", b.Text["RightMeters_mc/AmmoCount_mc/ReserveCount_tf"]);
        Assert.DoesNotContain("RightMeters_mc/AmmoCount_mc", b.Hide);
    }

    [Fact]
    public void HudHidesAmmoWithoutWeapon()
    {
        var s = new HudState();
        s.ClearAmmo();
        Assert.Contains("RightMeters_mc/AmmoCount_mc", HudArt.Build(s).Hide);
    }

    private sealed class NoLimbs : IPlayerStatusSource
    {
        public List<SpecialEntry> SpecialList { get; } = new();

        public List<OwnedPerk> PerkList { get; } = new();

        public string? PlayerName => "Name A";

        public double? Health => null;

        public double? MaxHealth => null;

        public double? PendingHealthGain => null;

        public double? Radiation => null;

        public IReadOnlyList<DamageTotal>? DamageTotals => null;

        public IReadOnlyList<DamageTotal>? ResistTotals => null;

        public AidSupply? Stimpak => null;

        public AidSupply? RadAway => null;

        public IReadOnlyList<EffectSource> Effects => Array.Empty<EffectSource>();

        public IReadOnlyList<SpecialEntry> Special => SpecialList;

        public IReadOnlyList<OwnedPerk> Perks => PerkList;

        public bool ReadOnly => false;

        public LimbCondition? Limb(LimbKind limb) => null;
    }

    private static StatusListView ViewOf(IPlayerStatusSource source) => new(new PlayerStatusModel(source));

    [Fact]
    public void PipboyShellBindsTheStatusStripFromTheFooterAndTheSubTabFromTheModel()
    {
        var sample = new SamplePlayerStatus();
        var view = ViewOf(sample);
        var b = PipboyArt.Shell(view, sample);
        var strip = "Menu_mc/BottomBar_mc/Info_mc/";
        Assert.Equal(PipBoyFooter.Segments(PipBoyTab.Stats, sample)[0], b.Text[strip + "HP_tf"]);
        Assert.Equal("HP 62/100", b.Text[strip + "HP_tf"]);
        Assert.Equal("LEVEL 7 50%", b.Text[strip + "LVL_tf"]);
        Assert.Equal("AP 70/90", b.Text[strip + "AP_tf"]);
        Assert.Equal(0.5, b.FillX[strip + "XPMeter_mc/Fill_mc"], 3);
        Assert.Equal("STATUS", b.Text["Menu_mc/Header_mc/TabHeader_mc/AlphaHolder/Selected/textField_tf"]);
        view.SelectTab(1);
        Assert.Equal("SPECIAL", PipboyArt.Shell(view, sample).Text["Menu_mc/Header_mc/TabHeader_mc/AlphaHolder/Selected/textField_tf"]);
    }

    [Fact]
    public void PipboyShellHidesTheCarouselSlotsTheInventoryShellFills()
    {
        var sample = new SamplePlayerStatus();
        var b = PipboyArt.Shell(ViewOf(sample), sample);
        foreach (var slot in new[] { "RightOne", "LeftOne", "RightTwo", "LeftTwo" })
            Assert.Contains("Menu_mc/Header_mc/TabHeader_mc/AlphaHolder/" + slot, b.Hide);
        Assert.DoesNotContain("Menu_mc/Header_mc/TabHeader_mc/AlphaHolder/Selected", b.Hide);
    }

    [Fact]
    public void PipboyShellHidesTheExperienceMeterAndShowsDashesWithoutASource()
    {
        var b = PipboyArt.Shell(ViewOf(new NoLimbs()), null);
        Assert.Contains("Menu_mc/BottomBar_mc/Info_mc/XPMeter_mc", b.Hide);
        Assert.Empty(b.FillX);
        Assert.Equal(PipBoyFooter.Unknown, b.Text["Menu_mc/BottomBar_mc/Info_mc/HP_tf"]);
        Assert.Equal(PipBoyFooter.Unknown, b.Text["Menu_mc/BottomBar_mc/Info_mc/AP_tf"]);
    }

    sealed class NoLevel : IPipBoyStatusSource
    {
        public double? Health => 50;
        public double? MaxHealth => 100;
        public int? Level => null;
        public double? LevelProgress => 0.5;
        public double? ActionPoints => 10;
        public double? MaxActionPoints => 20;
        public double? CarryWeight => null;
        public double? CarryCapacity => null;
        public long? Caps => null;
        public System.DateTime? WorldTime => null;
        public string? Location => null;
    }

    [Fact]
    public void PipboyShellHidesTheExperienceMeterWhenTheLevelIsUnknown()
    {
        var b = PipboyArt.Shell(ViewOf(new SamplePlayerStatus()), new NoLevel());
        Assert.Contains("Menu_mc/BottomBar_mc/Info_mc/XPMeter_mc", b.Hide);
        Assert.Empty(b.FillX);
    }

    [Fact]
    public void StatusRowsAreDrawnOnlyForLimbsTorsoAndEffects()
    {
        Assert.True(PipboyArt.IsDrawnRow(StatusSubTab.Status, PlayerStatusModel.TorsoKey));
        Assert.True(PipboyArt.IsDrawnRow(StatusSubTab.Status, PlayerStatusModel.LimbKey(LimbKind.Head)));
        Assert.True(PipboyArt.IsDrawnRow(StatusSubTab.Status, "effects"));
        foreach (var key in new[] { "radiation", "damage", "resist", "aid:stimpak", "aid:radaway", null })
            Assert.False(PipboyArt.IsDrawnRow(StatusSubTab.Status, key));
        Assert.True(PipboyArt.IsDrawnRow(StatusSubTab.Special, "special:strength"));
    }

    [Fact]
    public void EveryStatusRowOfTheSampleIsEitherDrawnOrUndrawnAsExpected()
    {
        var status = new PlayerStatusModel(new SamplePlayerStatus());
        var keys = status.View.Visible.Select(row => row.Key).ToList();
        Assert.Contains(keys, key => !PipboyArt.IsDrawnRow(StatusSubTab.Status, key));
        Assert.Contains(keys, key => PipboyArt.IsDrawnRow(StatusSubTab.Status, key));
    }

    [Fact]
    public void PipboyShellHidesThePlaceholderTriggerHints()
    {
        var b = PipboyArt.Shell(ViewOf(new SamplePlayerStatus()), null);
        Assert.Contains("Menu_mc/Header_mc/LeftTriggerButton_mc", b.Hide);
        Assert.Contains("Menu_mc/Header_mc/RightTriggerButton_mc", b.Hide);
    }

    [Fact]
    public void PipboyStatusPageFillsEachLimbMeterFromItsOwnCondition()
    {
        var b = PipboyArt.StatsPage(ViewOf(new SamplePlayerStatus()));
        Assert.Equal("Character A", b.Text["StatusTab_mc/Name_tf"]);
        Assert.Equal(1.0, b.FillX["StatusTab_mc/Head_Meter/Fill_mc"], 3);
        Assert.Equal(1.0, b.FillX["StatusTab_mc/LArm_Meter/Fill_mc"], 3);
        Assert.Equal(0.55, b.FillX["StatusTab_mc/RArm_Meter/Fill_mc"], 3);
        Assert.Equal(0.0, b.FillX["StatusTab_mc/LLeg_Meter/Fill_mc"], 3);
        Assert.Equal(1.0, b.FillX["StatusTab_mc/RLeg_Meter/Fill_mc"], 3);
        Assert.Equal(0.62, b.FillX["StatusTab_mc/Torso_Meter/Fill_mc"], 3);
        Assert.Equal(0.82, b.FillX["StatusTab_mc/Torso_Meter/GhostFill_mc"], 3);
        Assert.Contains("SPECIALTab_mc", b.Hide);
        Assert.DoesNotContain("StatusTab_mc", b.Hide);
        Assert.Empty(b.Lists);
    }

    [Fact]
    public void PipboyStatusPageHidesMetersWithoutLimbData()
    {
        var b = PipboyArt.StatsPage(ViewOf(new NoLimbs()));
        foreach (var meter in new[] { "Head", "LArm", "RArm", "LLeg", "RLeg", "Torso" })
            Assert.Contains("StatusTab_mc/" + meter + "_Meter", b.Hide);
        Assert.Empty(b.FillX);
    }

    [Fact]
    public void PipboyStatusPageListsActiveEffectsOnlyWhileTheyAreShown()
    {
        var view = ViewOf(new SamplePlayerStatus());
        Assert.Empty(PipboyArt.StatsPage(view).Lists);
        Assert.True(view.Status.ToggleEffects());
        var rows = PipboyArt.StatsPage(view).Lists["StatusTab_mc/ActiveEffects_mc"];
        Assert.Equal(view.Status.EffectLines, rows.Select(r => r.Text));
    }

    [Fact]
    public void PipboySpecialPageListsRowsMarksTheCursorAndDescribesTheSelection()
    {
        var source = new NoLimbs();
        source.SpecialList.Add(new SpecialEntry("a", "Name A", 6, 1, "Text A"));
        source.SpecialList.Add(new SpecialEntry("b", "Name B", 4, 0, "Text B"));
        var view = ViewOf(source);
        view.SelectTab(1);
        view.Select(1);
        var b = PipboyArt.StatsPage(view);
        var rows = b.Lists["SPECIALTab_mc/List_mc"];
        Assert.Equal(new[] { "Name A  +6", "Name B  4" }, rows.Select(r => r.Text));
        Assert.Equal(new[] { false, true }, rows.Select(r => r.Selected));
        Assert.Equal("Text B", b.Text["SPECIALTab_mc/Description_tf"]);
        Assert.Contains("StatusTab_mc", b.Hide);
        Assert.DoesNotContain("SPECIALTab_mc", b.Hide);
    }

    [Fact]
    public void PipboyPerksPageShowsTheRankLineAndTheEmptyMessage()
    {
        var source = new NoLimbs();
        var view = ViewOf(source);
        view.SelectTab(2);
        Assert.Equal("NO PERKS", PipboyArt.StatsPage(view).Text["SPECIALTab_mc/Description_tf"]);
        Assert.Empty(PipboyArt.StatsPage(view).Lists["SPECIALTab_mc/List_mc"]);

        var owned = new NoLimbs();
        owned.PerkList.Add(new OwnedPerk("p", "Perk A", 2, new[] { "Text one", "Text two", "Text three" }));
        var perks = ViewOf(owned);
        perks.SelectTab(2);
        var b = PipboyArt.StatsPage(perks);
        Assert.Equal("RANK 2 / 3\nText two", b.Text["SPECIALTab_mc/Description_tf"]);
        Assert.Equal("Perk A (2)", b.Lists["SPECIALTab_mc/List_mc"][0].Text);
    }

    sealed class StepSource : IPlayerStatusSource
    {
        public bool Torso { get; init; } = true;

        public bool Effect { get; init; }

        public string? PlayerName => "Name A";

        public double? Health => Torso ? 50 : null;

        public double? MaxHealth => Torso ? 100 : null;

        public double? PendingHealthGain => null;

        public double? Radiation => Torso ? null : 0.5;

        public IReadOnlyList<DamageTotal>? DamageTotals => null;

        public IReadOnlyList<DamageTotal>? ResistTotals => null;

        public AidSupply? Stimpak => Torso ? new AidSupply("Item A", 1) : null;

        public AidSupply? RadAway => null;

        public IReadOnlyList<EffectSource> Effects => Effect
            ? new[] { new EffectSource(1, "Source A", new[] { new EffectLine("Effect 1", 2) }) }
            : Array.Empty<EffectSource>();

        public IReadOnlyList<SpecialEntry> Special => Array.Empty<SpecialEntry>();

        public IReadOnlyList<OwnedPerk> Perks => Array.Empty<OwnedPerk>();

        public bool ReadOnly => false;

        public LimbCondition? Limb(LimbKind limb) => null;
    }

    [Fact]
    public void StepDownAtTheEndWithTrailingUndrawnRowsStaysOnTheLastDrawnRow()
    {
        var view = ViewOf(new StepSource());
        Assert.Equal(PlayerStatusModel.TorsoKey, view.Status.CurrentKey);
        Assert.False(PipboyArt.Step(view, MenuInputAction.Down));
        Assert.Equal(PlayerStatusModel.TorsoKey, view.Status.CurrentKey);
    }

    [Fact]
    public void StepUpAtTheTopStaysPut()
    {
        var view = ViewOf(new StepSource());
        PipboyArt.Step(view, MenuInputAction.Up);
        Assert.Equal(PlayerStatusModel.TorsoKey, view.Status.CurrentKey);
    }

    [Fact]
    public void StepSkipsUndrawnRowsToReachTheEffectsRow()
    {
        var view = ViewOf(new StepSource { Effect = true });
        Assert.True(PipboyArt.Step(view, MenuInputAction.Down));
        Assert.Equal("effects", view.Status.CurrentKey);
        PipboyArt.Step(view, MenuInputAction.Down);
        Assert.Equal("effects", view.Status.CurrentKey);
        Assert.True(PipboyArt.Step(view, MenuInputAction.Up));
        Assert.Equal(PlayerStatusModel.TorsoKey, view.Status.CurrentKey);
    }

    [Fact]
    public void SelectFirstDrawnMovesOffAnUndrawnFirstRow()
    {
        var view = ViewOf(new StepSource { Torso = false, Effect = true });
        Assert.Equal("radiation", view.Status.CurrentKey);
        PipboyArt.SelectFirstDrawn(view);
        Assert.Equal("effects", view.Status.CurrentKey);
    }

    [Fact]
    public void SelectFirstDrawnLeavesTheCursorWhenNoRowIsDrawn()
    {
        var view = ViewOf(new StepSource { Torso = false });
        PipboyArt.SelectFirstDrawn(view);
        Assert.Equal("radiation", view.Status.CurrentKey);
    }

    [Fact]
    public void BarterTakesItsPlayerListHeaderFromTheRules()
    {
        var rules = new BarterControllerTests.FakeRules();
        var b = BarterArt.Build(new BarterController(rules), rules);
        Assert.Equal("Your stuff", b.Text["FilterHolder_mc/Menu_mc/PlayerInventory_mc/PlayerListHeader/textField"]);
    }

    private const string Menu = "FilterHolder_mc/Menu_mc/";

    private static List<SwfListRow> Rows(int count, int selected) =>
        Enumerable.Range(0, count).Select(i => new SwfListRow("r" + i, i == selected)).ToList();

    [Fact]
    public void WindowShowsEveryRowWhenTheyFit()
    {
        var w = SwfBindings.Window(Rows(SwfBindings.ListRows, 0));
        Assert.Equal(SwfBindings.ListRows, w.Rows.Count);
        Assert.False(w.MoreAbove);
        Assert.False(w.MoreBelow);
    }

    [Fact]
    public void WindowScrollsToKeepTheSelectionVisible()
    {
        var top = SwfBindings.Window(Rows(20, 0));
        Assert.Equal("r0", top.Rows[0].Text);
        Assert.True(top.MoreBelow);
        Assert.False(top.MoreAbove);

        var end = SwfBindings.Window(Rows(20, 19));
        Assert.Equal("r19", end.Rows[^1].Text);
        Assert.Equal(SwfBindings.ListRows, end.Rows.Count);
        Assert.True(end.MoreAbove);
        Assert.False(end.MoreBelow);
        Assert.Single(end.Rows, r => r.Selected);
    }

    [Fact]
    public void BarterBindsEveryStackAndMarksOneSelectedRow()
    {
        var rules = new SampleBarter();
        var b = BarterArt.Build(new BarterController(rules), rules);
        var player = b.Lists[Menu + "PlayerInventory_mc/PlayerListHeader"];
        var vendor = b.Lists[Menu + "ContainerInventory_mc/ContainerListHeader"];
        Assert.True(rules.PlayerStacks.Count > SwfBindings.ListRows);
        Assert.Equal(SwfBindings.ListRows, player.Count);
        Assert.Equal(rules.VendorStacks.Count, vendor.Count);
        Assert.Equal(1, player.Concat(vendor).Count(r => r.Selected));
    }

    [Fact]
    public void BarterShowsScrollChevronsOnlyWhereRowsOverflow()
    {
        var rules = new SampleBarter();
        var b = BarterArt.Build(new BarterController(rules), rules);
        Assert.DoesNotContain(Menu + "PlayerInventory_mc/PlayerList_mc/ScrollDown", b.Hide);
        Assert.Contains(Menu + "PlayerInventory_mc/PlayerList_mc/ScrollUp", b.Hide);
        Assert.Contains(Menu + "ContainerList_mc/ScrollDown", b.Hide);
        Assert.Contains(Menu + "ContainerList_mc/ScrollUp", b.Hide);
    }

    [Theory]
    [InlineData("PlayerInventory_mc/PlayerSwitchButton_tf")]
    [InlineData("ContainerInventory_mc/ContainerSwitchButton_tf")]
    [InlineData("PlayerInventory_mc/LeftHitBox_tf")]
    [InlineData("ContainerInventory_mc/RightHitBox_tf")]
    public void BarterHidesTemplateButtonPromptFields(string path)
    {
        var rules = new SampleBarter();
        Assert.Contains(Menu + path, BarterArt.Build(new BarterController(rules), rules).Hide);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(110)]
    [InlineData(221)]
    [InlineData(500)]
    public void HeaderArrowClearsTheTextByTheGap(double textWidth)
    {
        const double textX = 19, field = 221;
        var arrow = SwfBindings.HeaderArrowX(textX, textWidth, field);
        Assert.True(arrow >= textX + Math.Min(textWidth, field) + SwfBindings.HeaderArrowGap);
        Assert.True(arrow <= textX + field + SwfBindings.HeaderArrowGap);
    }

    [Fact]
    public void HeaderArrowFollowsTheTextWidth()
    {
        Assert.True(SwfBindings.HeaderArrowX(19, 140, 221) > SwfBindings.HeaderArrowX(19, 90, 221));
    }

    [Fact]
    public void BarterPlacesArrowsOnBothListHeaders()
    {
        var rules = new SampleBarter();
        var b = BarterArt.Build(new BarterController(rules), rules);
        Assert.Contains(Menu + "PlayerInventory_mc/PlayerListHeader", b.Headers);
        Assert.Contains(Menu + "ContainerInventory_mc/ContainerListHeader", b.Headers);
        foreach (var h in b.Headers)
            Assert.True(b.Text.ContainsKey(h + SwfBindings.HeaderText));
    }
}
