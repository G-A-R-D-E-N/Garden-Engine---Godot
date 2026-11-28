using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Swf;
using Radicle.Interop;
using OpenCommonwealth.Tests;
using Xunit;

namespace OpenCw.Ui.Tests;

public class HudReadoutArtTests
{
    private static HudState Full()
    {
        var s = new HudState();
        s.SetActionPoints(60, 80);
        s.Instruments.SetEnemy("Raider", 0.5);
        s.Instruments.SetEnemyDetails(14, true, true);
        s.Instruments.Tick(1);
        s.Instruments.SetActionPointPreview(30, null);
        s.Instruments.SetCritical(0.7, 2);
        s.Instruments.SetEffectIcons(new[] { HudEffectIcon.Chems, HudEffectIcon.Thirst });
        s.Experience.Sync(5, 0.25);
        s.Experience.Gain(40);
        s.Experience.Sync(6, 0.1);
        s.Location.Announce("Concord");
        s.Location.Tick(0.6);
        s.Toasts = new ToastQueue();
        s.Toasts.Show("Quicksave complete");
        s.Toasts.Tick(0.5);
        s.PowerArmor.Apply(new PowerArmorState(null, 0.1));
        s.PowerArmor.Tick(1);
        return s;
    }

    [Fact]
    public void Experience_popup_follows_gains_and_level_ups()
    {
        var xp = new HudExperience();
        xp.Sync(3, 0.5);
        Assert.False(xp.Active);
        Assert.False(xp.LevelUp);
        xp.Gain(0);
        xp.Gain(-4);
        Assert.False(xp.Active);
        xp.Gain(25);
        Assert.True(xp.Active);
        Assert.Equal(25, xp.LastGain);
        Assert.Equal(1, xp.Alpha);
        xp.Sync(4, 0.1);
        Assert.True(xp.LevelUp);
        Assert.Equal(4, xp.Level);
        xp.Tick(HudExperience.PopupSeconds - HudExperience.FadeSeconds / 2);
        Assert.Equal(0.5, xp.Alpha, 6);
        xp.Tick(1);
        Assert.False(xp.Active);
        Assert.False(xp.LevelUp);
        xp.Sync(4, double.NaN);
        Assert.Equal(0.1, xp.Progress, 6);
        xp.Sync(2, 7);
        Assert.Equal(1, xp.Progress);
        Assert.False(xp.LevelUp);
    }

    [Fact]
    public void Loading_a_save_does_not_pop_a_level_up()
    {
        var xp = new HudExperience();
        xp.Sync(12, 0.3);
        Assert.False(xp.Active);
        Assert.False(xp.LevelUp);
    }

    [Fact]
    public void A_level_jump_without_a_gain_does_not_pop_a_level_up()
    {
        var xp = new HudExperience();
        xp.Sync(1, 0);
        xp.Sync(12, 0.3);
        Assert.Equal(12, xp.Level);
        Assert.False(xp.Active);
        Assert.False(xp.LevelUp);
        xp.Gain(10);
        xp.Sync(12, 0.4);
        Assert.False(xp.LevelUp);
        xp.Sync(13, 0.1);
        Assert.False(xp.LevelUp);
    }

    [Fact]
    public void A_later_gain_keeps_an_active_level_up_text()
    {
        var xp = new HudExperience();
        xp.Sync(1, 0);
        xp.Gain(100);
        xp.Sync(2, 0.1);
        Assert.True(xp.LevelUp);
        xp.Gain(5);
        Assert.True(xp.LevelUp);
        Assert.Equal(5, xp.LastGain);
        xp.Tick(HudExperience.PopupSeconds + 1);
        Assert.False(xp.LevelUp);
    }

    [Fact]
    public void Player_experience_reports_each_gain()
    {
        var xp = new PlayerExperience();
        var gains = new List<long>();
        xp.Gained += gains.Add;
        Assert.False(xp.Add(0));
        Assert.True(xp.Add(15));
        Assert.True(xp.Add(5));
        Assert.Equal(new long[] { 15, 5 }, gains);
    }

    [Fact]
    public void Location_announces_a_change_and_fades()
    {
        var l = new HudLocation();
        l.Set("Sanctuary");
        Assert.Equal("Sanctuary", l.Name);
        Assert.Equal(0, l.Alpha);
        l.Set("Concord");
        Assert.Equal(0, l.Alpha);
        l.Tick(0.01);
        l.Set("Diamond City");
        l.Tick(HudLocation.FadeSeconds / 2);
        Assert.InRange(l.Alpha, 0.01, 0.99);
        l.Tick(HudLocation.FadeSeconds);
        Assert.Equal(1, l.Alpha);
        l.Tick(4.0);
        Assert.InRange(l.Alpha, 0.01, 0.99);
        l.Tick(10);
        Assert.Equal(0, l.Alpha);
        l.Set("  ");
        Assert.Equal("", l.Name);
    }

    [Fact]
    public void Location_announces_again_after_the_name_was_lost()
    {
        var l = new HudLocation();
        l.Set("Sanctuary");
        l.Set(null);
        Assert.Equal("", l.Name);
        Assert.Equal(0, l.Alpha);
        l.Set("Concord");
        l.Tick(HudLocation.FadeSeconds);
        Assert.Equal(1, l.Alpha);
    }

    [Theory]
    [InlineData(10, 5, 5, true)]
    [InlineData(10, 5, 6, false)]
    [InlineData(3, 5, 5, false)]
    [InlineData(10, 5, null, false)]
    public void Skull_needs_a_readable_level_difference(int enemy, int player, int? difference, bool expected) =>
        Assert.Equal(expected, HudEnemyRules.ShowsSkull(enemy, player, difference));

    // Regression: a keyword NPC_ must read legendary, a normal NPC_ or an unrelated keyword must not; the binding is untested.
    [Fact]
    public void Legendary_needs_the_legendary_keyword_on_the_npc()
    {
        var reader = new FakeRecordReader();
        reader.Add("000001:Fallout4.esm", "KYWD", "EncTypeLegendary");
        reader.Add("000002:Fallout4.esm", "KYWD", "EncTypeRaider");
        reader.Add("000010:Fallout4.esm", "NPC_").Links["KWDA"] = ["000002:Fallout4.esm", "000001:Fallout4.esm"];
        reader.Add("000011:Fallout4.esm", "NPC_").Links["KWDA"] = ["000002:Fallout4.esm"];
        reader.Add("000012:Fallout4.esm", "NPC_");
        Assert.True(HudEnemyRules.IsLegendary(reader, "000010:Fallout4.esm"));
        Assert.False(HudEnemyRules.IsLegendary(reader, "000011:Fallout4.esm"));
        Assert.False(HudEnemyRules.IsLegendary(reader, "000012:Fallout4.esm"));
        Assert.False(HudEnemyRules.IsLegendary(new FakeRecordReader(), "000010:Fallout4.esm"));
    }

    private sealed class EnvSource(RadicleEnvironment environment) : IRecordSource
    {
        public RadicleEnvironment Environment => environment;

        public StringTables Strings => null!;
    }

    // Regression: the rule must read the KWDA link through the real Radicle path: keyword present, absent, and no keywords at all.
    [Fact]
    public void Legendary_reads_the_keyword_link_through_radicle()
    {
        const string name = "LegendaryNpcs.esp";
        using var file = PluginFile.Create(name);
        file.SetNextFormId(0x800);
        file.CreateRecord("KYWD", "EncTypeLegendary");
        file.SetNextFormId(0x801);
        file.CreateRecord("KYWD", "EncTypeRaider");
        file.SetNextFormId(0x810);
        var legendary = file.CreateRawRecord("NPC_", 131);
        file.SetSubrecordBytes(legendary, "KSIZ", BitConverter.GetBytes(2u));
        file.SetSubrecordBytes(legendary, "KWDA", BitConverter.GetBytes(0x801u).Concat(BitConverter.GetBytes(0x800u)).ToArray());
        file.SetNextFormId(0x811);
        var plain = file.CreateRawRecord("NPC_", 131);
        file.SetSubrecordBytes(plain, "KSIZ", BitConverter.GetBytes(1u));
        file.SetSubrecordBytes(plain, "KWDA", BitConverter.GetBytes(0x801u));
        file.SetNextFormId(0x812);
        file.CreateRawRecord("NPC_", 131);

        var directory = Directory.CreateTempSubdirectory("garden-legendary-");
        try
        {
            var path = Path.Combine(directory.FullName, name);
            file.Write(path);
            using var environment = RadicleEnvironment.Load(new[] { path });
            var reader = new RadicleRecordReader(new EnvSource(environment));
            Assert.True(HudEnemyRules.IsLegendary(reader, "000810:" + name));
            Assert.False(HudEnemyRules.IsLegendary(reader, "000811:" + name));
            Assert.False(HudEnemyRules.IsLegendary(reader, "000812:" + name));
        }
        finally { directory.Delete(true); }
    }

    // Regression: the Enc*Legendary variants must read legendary and a plain variant must not (needs FO4_DATA_DIR).
    [Fact]
    public void Legendary_matches_the_vanilla_variants()
    {
        if (Fo4Data.Master is not { } master)
            return;
        using var environment = RadicleEnvironment.Load(new[] { master });
        var reader = new RadicleRecordReader(new EnvSource(environment));
        var legendary = reader.FindEditorId("EncSentryBot04Legendary", "NPC_");
        var plain = reader.FindEditorId("EncSentryBot04", "NPC_");
        Assert.NotNull(legendary);
        Assert.NotNull(plain);
        Assert.True(HudEnemyRules.IsLegendary(reader, legendary!));
        Assert.False(HudEnemyRules.IsLegendary(reader, plain!));
        var flagged = reader.Winners("NPC_").Count(id => HudEnemyRules.IsLegendary(reader, id));
        Assert.InRange(flagged, 100, 400);
    }

    [Fact]
    public void Effect_icons_keep_only_real_frames_up_to_the_cap()
    {
        var i = new HudInstruments();
        i.SetEffectIcons(new[] { HudEffectIcon.None, 0, -3, HudEffectIcon.Max + 1, HudEffectIcon.Chems });
        Assert.Equal(new[] { HudEffectIcon.Chems }, i.EffectIcons);
        i.SetEffectIcons(Enumerable.Repeat(HudEffectIcon.Hunger, 20).ToArray());
        Assert.Equal(HudInstruments.MaxEffects, i.EffectIcons.Count);
        i.SetEffectIcons(null);
        Assert.Empty(i.EffectIcons);
    }

    [Fact]
    public void Enemy_skull_clears_with_a_new_target()
    {
        var i = new HudInstruments();
        i.SetEnemy("Raider", 1);
        i.SetEnemyDetails(20, false, true);
        Assert.True(i.EnemySkull);
        i.SetEnemy("Ghoul", 1);
        Assert.False(i.EnemySkull);
    }

    [Fact]
    public void Idle_state_hides_every_new_widget_and_binds_nothing_extra()
    {
        var b = HudArt.Build(new HudState());
        foreach (var path in new[] { HudArt.Notifications, HudArt.Location, HudArt.Crit, HudArt.PowerArmorWarning, HudArt.ActionPoints + "/Optional_mc" })
            Assert.Contains(path, b.Hide);
        Assert.DoesNotContain(b.Sprites, x => x.Parent is HudArt.Messages or HudArt.Effects || x.Parent.StartsWith(HudArt.Crit));
        Assert.DoesNotContain(HudArt.Effects, b.Show);
    }

    [Fact]
    public void Experience_popup_binds_the_xp_meter()
    {
        var s = Full();
        var b = HudArt.Build(s);
        Assert.Contains(HudArt.Notifications, b.Show);
        Assert.DoesNotContain(HudArt.Notifications, b.Hide);
        Assert.Contains(HudArt.Experience, b.Show);
        Assert.Equal("40", b.Text[HudArt.Experience + "/NumberText"]);
        Assert.Equal(0.1, b.FillX[HudArt.Experience + "/LevelUPBar/MeterBarInternal_mc"], 6);
        Assert.Equal("LEVEL 6", b.Text[HudArt.Experience + "/LeveUpTextClip/textField"]);
        Assert.Contains(HudArt.Experience + "/LeveUpTextClip", b.Show);
        foreach (var other in new[] { "/QuestUpdates_mc", "/TutorialText_mc", "/PromptMessageHolder_mc", "/ObjectiveUpdates_mc", "/QuestVaultBoy_mc" })
            Assert.Contains(HudArt.Notifications + other, b.Hide);

        s.Experience.Sync(6, 0.2);
        s.Experience.Gain(5);
        b = HudArt.Build(s);
        Assert.Contains(HudArt.Experience + "/LeveUpTextClip", b.Show);
        Assert.Equal("LEVEL 6", b.Text[HudArt.Experience + "/LeveUpTextClip/textField"]);
        Assert.Equal("5", b.Text[HudArt.Experience + "/NumberText"]);
        s.Experience.Tick(10);
        b = HudArt.Build(s);
        Assert.DoesNotContain(HudArt.Experience + "/LeveUpTextClip/textField", b.Text.Keys);
        for (var i = 0; i < 100; i++)
            s.Toasts!.Tick(0.5);
        b = HudArt.Build(s);
        Assert.Contains(HudArt.Notifications, b.Hide);
    }

    [Fact]
    public void Toasts_become_labels_under_messages_and_follow_the_setting()
    {
        var s = Full();
        s.Experience.Tick(10);
        s.Toasts!.Show("Second toast");
        s.Toasts.Tick(1);
        var b = HudArt.Build(s);
        var rows = b.Sprites.Where(x => x.Parent == HudArt.Messages).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Quicksave complete", rows[0].Label!.Text);
        Assert.Equal(0, rows[0].Y);
        Assert.Equal(26.0, rows[1].Y);
        Assert.Contains(HudArt.Messages, b.Show);
        Assert.Contains(HudArt.Experience, b.Hide);

        s.Settings.SetEnabled(HudElement.Toasts, false);
        b = HudArt.Build(s);
        Assert.Contains(HudArt.Notifications, b.Hide);
        Assert.DoesNotContain(b.Sprites, x => x.Parent == HudArt.Messages);
    }

    [Fact]
    public void Action_point_preview_drives_the_optional_bar_and_dims_the_main_bar()
    {
        var s = Full();
        var b = HudArt.Build(s);
        var optional = HudArt.ActionPoints + "/Optional_mc";
        Assert.Contains(optional, b.Show);
        Assert.DoesNotContain(optional, b.Hide);
        Assert.Equal(new SwfFill(30 / 80.0, 202), b.FillRight[optional + "/MeterBarInternal_mc"]);
        Assert.Equal(new SwfFill(60 / 80.0, 202), b.FillRight[HudArt.ActionPoints + "/MeterBar_mc/MeterBarInternal_mc"]);
        Assert.Equal(0.45, b.Alpha[HudArt.ActionPoints + "/MeterBar_mc"]);

        s.Instruments.SetActionPointPreview(null, null);
        b = HudArt.Build(s);
        Assert.Contains(optional, b.Hide);
        Assert.DoesNotContain(HudArt.ActionPoints + "/MeterBar_mc", b.Alpha.Keys);
    }

    [Fact]
    public void Critical_meter_fills_flashes_and_counts_stars()
    {
        var s = Full();
        var b = HudArt.Build(s);
        Assert.Contains(HudArt.Crit, b.Show);
        Assert.Equal(0.7, b.FillX[HudArt.Crit + "/MeterBar_mc/MeterBarInternal_mc"], 6);
        Assert.Equal(s.Instruments.CriticalAlpha, b.Alpha[HudArt.Crit]);
        var stars = b.Sprites.Where(x => x.Parent == HudArt.Crit + "/CritMeterStars_mc").ToList();
        Assert.Equal(new[] { HudArt.CritFullTemplate, HudArt.CritFullTemplate, HudArt.CritEmptyTemplate }, stars.Select(x => x.Template));
        Assert.Equal(new[] { 0.0, 22.0, 44.0 }, stars.Select(x => x.X));

        s.Instruments.SetGroupVisible(HudGroup.Centre, false);
        b = HudArt.Build(s);
        Assert.Contains(HudArt.Crit, b.Hide);
        Assert.DoesNotContain(b.Sprites, x => x.Parent.StartsWith(HudArt.Crit));

        s.Instruments.SetGroupVisible(HudGroup.Centre, true);
        s.Instruments.ClearCritical();
        Assert.Contains(HudArt.Crit, HudArt.Build(s).Hide);
    }

    [Fact]
    public void Enemy_skull_shows_with_the_enemy_only()
    {
        var s = Full();
        var b = HudArt.Build(s);
        Assert.Contains(HudArt.Enemy + "/SkullIcon_mc", b.Show);
        s.Instruments.SetEnemyDetails(14, true, false);
        Assert.Contains(HudArt.Enemy + "/SkullIcon_mc", HudArt.Build(s).Hide);
        s.Instruments.ClearEnemy();
        s.Instruments.Tick(5);
        Assert.Contains(HudArt.Enemy, HudArt.Build(s).Hide);
    }

    [Fact]
    public void Location_text_binds_with_fade_and_group_visibility()
    {
        var s = Full();
        var b = HudArt.Build(s);
        Assert.Contains(HudArt.Location, b.Show);
        Assert.Equal("Concord", b.Text[HudArt.Location + "/RegionText"]);
        Assert.Equal(1, b.Alpha[HudArt.Location]);
        s.Instruments.SetGroupVisible(HudGroup.Vitals, false);
        Assert.Contains(HudArt.Location, HudArt.Build(s).Hide);
        s.Instruments.SetGroupVisible(HudGroup.Vitals, true);
        s.Location.Tick(10);
        Assert.Contains(HudArt.Location, HudArt.Build(s).Hide);
    }

    [Fact]
    public void Effect_icons_stack_below_the_widget_and_shift_in_power_armour()
    {
        var s = Full();
        var b = HudArt.Build(s);
        var icons = b.Sprites.Where(x => x.Parent == HudArt.Effects).ToList();
        Assert.Equal(new[] { HudArt.EffectTemplate + HudEffectIcon.Chems, HudArt.EffectTemplate + HudEffectIcon.Thirst }, icons.Select(x => x.Template));
        Assert.All(icons, x => Assert.Equal(0.6, x.Scale));
        Assert.Equal(25.66, icons[1].Y - icons[0].Y, 6);
        Assert.Equal(61.52, icons[0].X, 6);
        Assert.Equal(26.52, icons[0].Y, 6);
        Assert.Contains(HudArt.Effects, b.Show);

        s.Instruments.SetGroupVisible(HudGroup.Effects, false);
        Assert.DoesNotContain(HudArt.Build(s).Sprites, x => x.Parent == HudArt.Effects);
    }

    [Fact]
    public void Power_armour_warning_text_binds_with_the_model_alpha()
    {
        var s = Full();
        var b = HudArt.Build(s);
        var tf = HudArt.PowerArmorWarning + "/WarningTextHolder_mc/PowerArmorLowBatteryWarning_tf";
        Assert.Contains(HudArt.PowerArmorWarning, b.Show);
        Assert.Equal("Power low", b.Text[tf]);
        Assert.Equal(s.PowerArmor.WarningAlpha, b.Alpha[HudArt.PowerArmorWarning]);
        s.Instruments.SetGroupVisible(HudGroup.PowerArmor, false);
        Assert.Contains(HudArt.PowerArmorWarning, HudArt.Build(s).Hide);
        s.Instruments.SetGroupVisible(HudGroup.PowerArmor, true);
        s.PowerArmor.Apply(null);
        Assert.Contains(HudArt.PowerArmorWarning, HudArt.Build(s).Hide);
    }

    [Fact]
    public void Followers_honour_the_toggle_of_the_element_they_follow()
    {
        var s = Full();
        s.Settings.SetEnabled(HudElement.Compass, false);
        Assert.Contains(HudArt.Crit, HudArt.Build(s).Hide);
        s.Settings.SetEnabled(HudElement.Compass, true);
        s.Settings.SetEnabled(HudElement.ActionPoints, false);
        var b = HudArt.Build(s);
        Assert.DoesNotContain(b.Sprites, x => x.Parent == HudArt.Effects);
        Assert.DoesNotContain(HudArt.Effects, b.Show);
        s.Settings.SetEnabled(HudElement.ActionPoints, true);
    }

    [Fact]
    public void Power_armour_warning_survives_the_ammo_readout_being_off()
    {
        var s = Full();
        s.Settings.SetEnabled(HudElement.Ammo, false);
        s.Settings.Scale = 1.5;
        var b = HudArt.Build(s, 2560, 1080, null);
        Assert.Contains(HudArt.PowerArmorWarning, b.Show);
        Assert.DoesNotContain(HudArt.PowerArmorWarning, b.Hide);
        Assert.Equal("Power low", b.Text[HudArt.PowerArmorWarning + "/WarningTextHolder_mc/PowerArmorLowBatteryWarning_tf"]);
        Assert.Contains(HudArt.PowerArmorWarning, b.Place.Keys);
        Assert.Contains(HudArt.Ammo, b.Hide);
    }

    [Theory]
    [InlineData("Quicksave complete", 1)]
    [InlineData("", 1)]
    [InlineData("a\nb", 2)]
    [InlineData("a\n\nb", 3)]
    [InlineData("a\n", 2)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa bbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 2)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa bbbbbbbbbbbbbbbbbbbbbbbbbbbbbb cccccccccccccccccccccccccccccc", 3)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 3)]
    public void Toast_line_count_wraps_by_word_at_the_label_width(string text, int lines) => Assert.Equal(lines, HudArt.ToastLines(text));

    [Fact]
    public void A_wrapped_toast_pushes_the_next_toast_down_by_its_line_count()
    {
        var s = new HudState { Toasts = new ToastQueue() };
        s.Toasts.Show("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa bbbbbbbbbbbbbbbbbbbbbbbbbbbbbb cccccccccccccccccccccccccccccc");
        s.Toasts.Show("Short");
        s.Toasts.Tick(1);
        var rows = HudArt.Build(s).Sprites.Where(x => x.Parent == HudArt.Messages).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(0.0, rows[0].Y);
        Assert.Equal(80.0, rows[0].Label!.Height);
        Assert.True(rows[0].Label!.Wrap);
        Assert.Equal(78.0, rows[1].Y);
        Assert.Equal(28.0, rows[1].Label!.Height);
    }

    [Fact]
    public void Power_armour_warning_shows_without_a_weapon()
    {
        var s = Full();
        Assert.False(s.Ammo.HasWeapon);
        Assert.Contains(HudArt.PowerArmorWarning, HudArt.Build(s).Show);
    }

    [Fact]
    public void Hidden_hud_hides_the_groups_that_hold_the_new_widgets()
    {
        var s = Full();
        s.Hidden = true;
        var b = HudArt.Build(s);
        foreach (var group in new[] { "LeftMeters_mc", "RightMeters_mc", "BottomCenterGroup_mc", HudArt.Notifications })
            Assert.Contains(group, b.Hide);
        Assert.Empty(b.Sprites);
    }

    private static void AssertPlace(double scale, double tx, double ty, SwfPlace actual)
    {
        Assert.Equal(scale, actual.Scale, 6);
        Assert.Equal(tx, actual.Tx, 6);
        Assert.Equal(ty, actual.Ty, 6);
    }

    [Fact]
    public void Followers_reuse_the_placement_of_their_group_and_the_toast_slot_is_identity_by_default()
    {
        Assert.True(HudStagePlacement.Compute(HudElement.Toasts, new HudSettings(), 1280, 720).IsIdentity);
        var s = Full();
        s.Settings.Scale = 1.5;
        s.Settings.SafeMargin = 20;
        var b = HudArt.Build(s, 2560, 1080, null);
        AssertPlace(1.5, -11, -352, b.Place[HudArt.Location]);
        AssertPlace(1.5, -320.0 / 3, -367, b.Place[HudArt.Crit]);
        AssertPlace(1.5, -607.0 / 3, -352, b.Place[HudArt.Effects]);
        AssertPlace(1.5, -607.0 / 3, -341, b.Place[HudArt.PowerArmorWarning]);
        AssertPlace(1.5, -11, 2, b.Place[HudArt.Notifications]);
    }

    [Fact]
    public void Signature_changes_with_every_new_binding()
    {
        var s = Full();
        var before = HudArt.Build(s).Signature();
        s.Instruments.SetActionPointPreview(10, null);
        var after = HudArt.Build(s).Signature();
        Assert.NotEqual(before, after);
        s.Instruments.SetEffectIcons(new[] { HudEffectIcon.Disease });
        Assert.NotEqual(after, HudArt.Build(s).Signature());
        Assert.Equal(HudArt.Build(s).Signature(), HudArt.Build(s).Signature());
        Assert.NotEqual(new SwfBindings { Sprites = { new SwfSprite("a", "p", 0, 0, 1) } }.Signature(), new SwfBindings { Sprites = { new SwfSprite("a", "p", 0, 0, 1, null, null, 1.25) } }.Signature());
    }

    [Fact]
    public void New_templates_are_unique_and_named()
    {
        var names = HudArt.Templates().Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Contains(HudArt.CritFullTemplate, names);
        foreach (var frame in HudEffectIcon.Used)
            Assert.Contains(HudArt.EffectTemplate + frame, names);
    }

    private static SwfStage? OpenHud() =>
        Fo4Data.Read(HudArt.Movie) is { } movieBytes ? SwfStage.Read(movieBytes) : null;

    private static void Collect(SwfNode node, HashSet<string> into)
    {
        into.Add(node.Path);
        foreach (var child in node.Children)
            Collect(child, into);
    }

    [Fact]
    public void Every_new_path_and_template_resolves_in_the_real_movie()
    {
        using var stage = OpenHud();
        if (stage is null)
            return;
        var paths = new HashSet<string>();
        Collect(stage.Root, paths);
        var s = Full();
        var b = HudArt.Build(s, 1280, 720, null);
        var bound = b.Hide.Concat(b.Show).Concat(b.Text.Keys).Concat(b.FillX.Keys).Concat(b.Alpha.Keys).Concat(b.Place.Keys).Concat(b.Sprites.Select(x => x.Parent)).Distinct().ToList();
        Assert.Empty(bound.Where(p => !paths.Contains(p)));
        foreach (var sprite in b.Sprites.Where(x => x.Template.Length > 0))
            Assert.Contains(HudArt.Templates(), t => t.Name == sprite.Template);
        foreach (var template in HudArt.Templates().Where(t => t.Name.StartsWith("crit_") || t.Name.StartsWith(HudArt.EffectTemplate)))
        {
            var tree = stage.ReadTemplate(template);
            Assert.NotNull(tree);
            Assert.NotEmpty(tree!.Children);
        }
        Assert.NotNull(stage.ReadTemplate(new SwfTemplate("last", null, null, 58, HudArt.EffectIconCharacter)));
        Assert.Null(stage.ReadTemplate(new SwfTemplate("past", null, null, 59, HudArt.EffectIconCharacter)));
        Assert.Null(stage.ReadTemplate(new SwfTemplate("none", null, null, 0, 65000)));
    }
}
