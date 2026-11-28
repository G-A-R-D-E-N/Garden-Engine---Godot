using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Swf;
using Radicle.Interop;
using OpenCommonwealth.Tests;
using Xunit;

namespace OpenCw.Ui.Tests;

public class HudVanillaArtTests
{
    private const string Ini = "[General]\niHUDColorR=1\n[Interface]\nbShowCompass=1\niHUDColorR=200\niHUDColorG=100\niHUDColorB=50\n[Pipboy]\niHUDColorB=9\n";

    [Fact]
    public void Prefs_read_the_interface_section_only()
    {
        var c = HudPrefs.Parse(Ini);
        Assert.Equal(200 / 255.0, c.R, 6);
        Assert.Equal(100 / 255.0, c.G, 6);
        Assert.Equal(50 / 255.0, c.B, 6);
    }

    [Fact]
    public void Prefs_fall_back_per_component_and_for_empty_input()
    {
        Assert.Equal(HudColor.Default, HudPrefs.Parse(null));
        Assert.Equal(HudColor.Default, HudPrefs.Parse(""));
        Assert.Equal(HudColor.Default, HudPrefs.Parse("[Interface]\nbShowCompass=1\n"));
        var partial = HudPrefs.Parse("[interface]\r\nIHUDCOLORG = 128 \r\niHUDColorB=abc\r\n");
        Assert.Equal(HudColor.Default.R, partial.R);
        Assert.Equal(128 / 255.0, partial.G, 6);
        Assert.Equal(HudColor.Default.B, partial.B);
    }

    [Theory]
    [InlineData(0, HudColor.Min)]
    [InlineData(-40, HudColor.Min)]
    [InlineData(255, 1.0)]
    [InlineData(999, 1.0)]
    public void Prefs_clamp_to_the_game_range(int value, double expected)
    {
        var c = HudPrefs.Parse($"[Interface]\niHUDColorR={value}\niHUDColorG={value}\niHUDColorB={value}\n");
        Assert.Equal(expected, c.R, 9);
        Assert.Equal(expected, c.G, 9);
        Assert.Equal(expected, c.B, 9);
    }

    [Fact]
    public void Load_returns_the_default_for_a_missing_file_and_reads_a_real_one()
    {
        Assert.Equal(HudColor.Default, HudPrefs.Load(Path.Combine(Path.GetTempPath(), "ge-missing-" + Guid.NewGuid().ToString("N") + ".ini")));
        var path = Path.Combine(Path.GetTempPath(), "ge-prefs-" + Guid.NewGuid().ToString("N") + ".ini");
        File.WriteAllText(path, Ini);
        try
        {
            Assert.Equal(200 / 255.0, HudPrefs.Load(path).R, 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Settings_tint_clamps_bumps_the_version_and_resets()
    {
        var settings = new HudSettings();
        Assert.Equal(HudColor.Default, settings.Tint);
        var version = settings.Version;
        settings.Tint = new HudColor(2, -1, 0.5);
        Assert.Equal(new HudColor(1, HudColor.Min, 0.5), settings.Tint);
        Assert.Equal(version + 1, settings.Version);
        settings.Tint = new HudColor(2, -1, 0.5);
        Assert.Equal(version + 1, settings.Version);
        settings.Tint = new HudColor(double.NaN, 0.5, 0.5);
        Assert.Equal(new HudColor(1, HudColor.Min, 0.5), settings.Tint);
        settings.ResetAll();
        Assert.Equal(HudColor.Default, settings.Tint);
    }

    [Fact]
    public void Placement_is_the_identity_at_the_stage_size_and_defaults()
    {
        foreach (var element in HudStagePlacement.Elements.Keys)
            Assert.True(HudStagePlacement.Compute(element, new HudSettings(), 1280, 720).IsIdentity, element.ToString());
    }

    [Fact]
    public void Placement_follows_user_scale_and_safe_margin()
    {
        var settings = new HudSettings { Scale = 2 };
        var health = HudStagePlacement.Compute(HudElement.Health, settings, 1280, 720);
        Assert.Equal(2, health.Scale, 6);
        Assert.Equal(-62, health.Tx, 6);
        Assert.Equal(-664, health.Ty, 6);

        var margin = HudStagePlacement.Compute(HudElement.Health, new HudSettings { SafeMargin = 20 }, 1280, 720);
        Assert.Equal(1, margin.Scale, 6);
        Assert.Equal(20, margin.Tx, 6);
        Assert.Equal(-20, margin.Ty, 6);
        var ap = HudStagePlacement.Compute(HudElement.ActionPoints, new HudSettings { SafeMargin = 20 }, 1280, 720);
        Assert.Equal(-20, ap.Tx, 6);
    }

    [Fact]
    public void Placement_keeps_the_centre_on_a_wide_screen_and_uses_the_stage_fit()
    {
        double fit = 1080 / 720.0;
        var compass = HudStagePlacement.Compute(HudElement.Compass, new HudSettings(), 2560, 1080);
        Assert.Equal(1, compass.Scale, 6);
        Assert.Equal(2560 / 2 / fit - 640, compass.Tx, 6);
        var health = HudStagePlacement.Compute(HudElement.Health, new HudSettings(), 2560, 1080);
        Assert.Equal(0, health.Tx, 6);
        var ap = HudStagePlacement.Compute(HudElement.ActionPoints, new HudSettings(), 2560, 1080);
        Assert.Equal(2560 / fit - 1280, ap.Tx, 6);
    }

    [Fact]
    public void Compass_ticks_become_direction_sprites_scaled_to_the_bar()
    {
        var s = new HudState();
        s.SetHeading(0);
        var b = HudArt.Build(s);
        var north = Assert.Single(b.Sprites, x => x.Label?.Text == "N");
        Assert.Equal(0, north.X, 6);
        Assert.Equal(HudCompassArt.Parent, north.Parent);
        Assert.Equal(HudCompassArt.DirectionTemplate, north.Template);
        Assert.All(b.Sprites, x => Assert.InRange(x.X, -HudCompassArt.HalfWidth, HudCompassArt.HalfWidth));
        s.SetHeading(45);
        var shifted = HudArt.Build(s).Sprites.Single(x => x.Label?.Text == "NE");
        Assert.Equal(0, shifted.X, 6);
    }

    [Fact]
    public void Compass_markers_use_their_kind_template_and_edge_position()
    {
        var s = new HudState();
        s.SetHeading(0);
        s.SetMarkers(new[] { new CompassMarker("q", 45, CompassMarkerKind.Objective), new CompassMarker("h", 350, CompassMarkerKind.Hostile) });
        var sprites = HudArt.Build(s).Sprites;
        var quest = sprites.Single(x => x.Template == HudCompassArt.MarkerTemplate(CompassMarkerKind.Objective));
        Assert.Equal(45.0 / 90 * HudCompassArt.HalfWidth, quest.X, 6);
        var hostile = sprites.Single(x => x.Template == HudCompassArt.MarkerTemplate(CompassMarkerKind.Hostile));
        Assert.Equal(-10.0 / 90 * HudCompassArt.HalfWidth, hostile.X, 6);
        Assert.Equal(HudCompassArt.PixelOffset(7), HudCompassArt.HalfWidth);
    }

    [Fact]
    public void Compass_is_absent_when_hidden_by_settings()
    {
        var s = new HudState();
        s.Settings.SetEnabled(HudElement.Compass, false);
        var b = HudArt.Build(s);
        Assert.Empty(b.Sprites);
        Assert.Contains(HudStagePlacement.Elements[HudElement.Compass].Path, b.Hide);
    }

    [Fact]
    public void Crosshair_states_pick_the_vanilla_pieces()
    {
        var ticks = HudArt.CrosshairBase + "/CrosshairTicks_mc";
        var s = new HudState { Crosshair = CrosshairState.Default };
        var b = HudArt.Build(s);
        Assert.Contains(ticks, b.Show);
        Assert.Contains(ticks + "/Left", b.Show);
        Assert.Contains(HudArt.CrosshairBase + "/CrosshairClips_mc", b.Hide);
        Assert.DoesNotContain(b.Sprites, x => x.Template == HudArt.ActivateTemplate);

        s.Crosshair = CrosshairState.Interactable;
        b = HudArt.Build(s);
        Assert.Contains(ticks, b.Hide);
        Assert.Contains(b.Sprites, x => x.Template == HudArt.ActivateTemplate);

        s.Crosshair = CrosshairState.Sneaking;
        Assert.Contains(HudArt.Build(s).Sprites, x => x.Template == HudArt.DotTemplate);

        s.Crosshair = CrosshairState.Hidden;
        b = HudArt.Build(s);
        Assert.Contains(HudStagePlacement.Elements[HudElement.Crosshair].Path, b.Hide);
        Assert.DoesNotContain(b.Sprites, x => x.Parent == HudArt.CrosshairBase);
    }

    [Fact]
    public void Interaction_prompt_feeds_the_rollover_widget()
    {
        var s = new HudState();
        var b = HudArt.Build(s);
        Assert.Contains(HudArt.Rollover, b.Hide);
        s.SetInteraction("Open", "Metal Footlocker", "E");
        b = HudArt.Build(s);
        Assert.DoesNotContain(HudArt.Rollover, b.Hide);
        Assert.Equal("Metal Footlocker", b.Text[HudArt.Rollover + "/RolloverName_tf"]);
        Assert.Equal(new[] { "E", "Open" }, b.Sprites.Where(x => x.Parent == HudArt.Rollover).Select(x => x.Label!.Text).ToArray());
        s.SetInteraction("Sleep", "", "");
        b = HudArt.Build(s);
        Assert.Equal("Sleep", b.Text[HudArt.Rollover + "/RolloverName_tf"]);
        Assert.DoesNotContain(b.Sprites, x => x.Parent == HudArt.Rollover);
    }

    [Fact]
    public void Interaction_prompt_is_not_drawn_while_a_menu_is_open()
    {
        var s = new HudState();
        s.SetInteraction("Trade", "Charlie", "E");
        s.MenuOpen = true;
        var b = HudArt.Build(s);
        Assert.Contains(HudArt.Rollover, b.Hide);
        Assert.DoesNotContain(b.Sprites, x => x.Parent == HudArt.Rollover);
        s.MenuOpen = false;
        Assert.DoesNotContain(HudArt.Rollover, HudArt.Build(s).Hide);
    }

    [Fact]
    public void Enemy_sneak_radiation_and_secondary_count_are_fed()
    {
        var s = new HudState();
        var b = HudArt.Build(s);
        Assert.Contains(HudArt.Enemy, b.Hide);
        Assert.Contains(HudArt.Stealth, b.Hide);
        Assert.Contains(HudArt.Explosive, b.Hide);
        Assert.Contains(HudArt.Health + "/RadsBar_mc", b.Hide);

        s.Instruments.SetEnemy("Raider", 0.4);
        s.Instruments.Tick(1);
        s.Instruments.SetSneak("Hidden", 20, true);
        s.Instruments.SetRadiation(0.25);
        s.Instruments.SetSecondaryCount(3);
        b = HudArt.Build(s);
        Assert.Contains(HudArt.Enemy, b.Show);
        Assert.Equal("RAIDER", b.Text[HudArt.Enemy + "/DisplayText_tf"]);
        Assert.Equal(0.4, b.FillX[HudArt.Enemy + "/MeterBar_mc/MeterBarInternal_mc"], 6);
        Assert.Contains(HudArt.Enemy + "/LegendaryIcon_mc", b.Hide);
        Assert.Equal("HIDDEN", b.Text[HudArt.Stealth + "/StealthTextInstance"]);
        Assert.True(b.Place[HudArt.Stealth + "/BracketRightInstance"].Tx > 0);
        Assert.Equal(-b.Place[HudArt.Stealth + "/BracketRightInstance"].Tx, b.Place[HudArt.Stealth + "/BracketLeftInstance"].Tx, 6);
        Assert.Equal(new SwfFill(0.25, HudArt.RadsBarWidth), b.FillRight[HudArt.Health + "/RadsBar_mc/MeterBarInternal_mc"]);
        Assert.Equal("03", b.Text[HudArt.Explosive + "/AvailableCount_tf"]);
    }

    [Fact]
    public void Hidden_hud_hides_every_group_and_group_toggles_hide_elements()
    {
        var s = new HudState();
        s.PushHidden();
        var b = HudArt.Build(s);
        Assert.Contains("LeftMeters_mc", b.Hide);
        Assert.Contains("CenterGroup_mc", b.Hide);
        Assert.Empty(b.Sprites);

        var t = new HudState();
        t.Instruments.SetGroupVisible(HudGroup.Vitals, false);
        Assert.Contains(HudArt.Health, HudArt.Build(t).Hide);
    }

    [Fact]
    public void Action_points_dim_and_meters_follow_the_state()
    {
        var s = new HudState();
        s.SetActionPoints(20, 80);
        var b = HudArt.Build(s);
        Assert.Equal(new SwfFill(0.25, 202), b.FillRight[HudArt.ActionPoints + "/MeterBar_mc/MeterBarInternal_mc"]);
        Assert.Equal(s.ActionPointsAlpha, b.Alpha[HudArt.ActionPoints], 6);
    }

    [Fact]
    public void User_scale_and_margin_reach_the_bindings_as_places()
    {
        var s = new HudState();
        s.SetAmmo(5, 50);
        s.Instruments.SetSecondaryCount(2);
        Assert.Empty(HudArt.Build(s).Place);
        s.Settings.Scale = 1.5;
        var b = HudArt.Build(s, 1280, 720, null);
        Assert.Equal(1.5, b.Place[HudArt.Health].Scale, 6);
        Assert.Equal(b.Place[HudArt.Ammo], b.Place[HudArt.Explosive]);
    }

    [Fact]
    public void Signature_changes_with_content_and_not_with_insertion_order()
    {
        var a = new SwfBindings();
        a.Hide.Add("a");
        a.Hide.Add("b");
        var b = new SwfBindings();
        b.Hide.Add("b");
        b.Hide.Add("a");
        Assert.Equal(a.Signature(), b.Signature());
        b.Text["x"] = "1";
        Assert.NotEqual(a.Signature(), b.Signature());
        var c = new SwfBindings();
        c.Sprites.Add(new SwfSprite("t", "p", 1, 2, 1));
        var d = new SwfBindings();
        d.Sprites.Add(new SwfSprite("t", "p", 1, 3, 1));
        Assert.NotEqual(c.Signature(), d.Signature());
    }

    [Fact]
    public void Reset_restores_the_baseline_tint_not_the_compiled_default()
    {
        var settings = new HudSettings();
        var prefs = new HudColor(0.5, 0.25, 0.75);
        settings.BaselineTint = prefs;
        Assert.Equal(prefs, settings.Tint);
        settings.Tint = new HudColor(1, 1, 1);
        settings.ResetAll();
        Assert.Equal(prefs, settings.Tint);
        settings.BaselineTint = new HudColor(double.NaN, 0, 0);
        Assert.Equal(prefs, settings.BaselineTint);
    }

    [Fact]
    public void Top_group_is_placed_with_margin_scale_and_stays_centred_on_a_wide_screen()
    {
        Assert.True(HudStagePlacement.ComputeTop(new HudSettings(), 1280, 720).IsIdentity);
        double fit = 1080 / 720.0;
        var wide = HudStagePlacement.ComputeTop(new HudSettings(), 2560, 1080);
        Assert.Equal(1, wide.Scale, 6);
        Assert.Equal(2560 / 2 / fit - 640, wide.Tx, 6);
        Assert.Equal(0, wide.Ty, 6);
        var margin = HudStagePlacement.ComputeTop(new HudSettings { SafeMargin = 20 }, 1280, 720);
        Assert.Equal(0, margin.Tx, 6);
        Assert.Equal(20, margin.Ty, 6);
        var scaled = HudStagePlacement.ComputeTop(new HudSettings { Scale = 1.5 }, 1280, 720);
        Assert.Equal(1.5, scaled.Scale, 6);
        Assert.Equal(490 - 540 * 1.5, scaled.Tx, 6);
        Assert.Equal(32 - 32 * 1.5, scaled.Ty, 6);
        var s = new HudState();
        s.Settings.Scale = 1.5;
        var b = HudArt.Build(s, 2560, 1080, null);
        Assert.True(b.Place.ContainsKey(HudStagePlacement.TopPath));
        s.Instruments.SetSneak("Hidden", 10, true);
        Assert.True(HudArt.Build(s, 2560, 1080, null).Place.ContainsKey(HudArt.Stealth + "/BracketLeftInstance"));
        Assert.False(HudArt.Build(new HudState(), 1280, 720, null).Place.ContainsKey(HudStagePlacement.TopPath));
    }

    [Fact]
    public void Explosive_count_is_placed_and_shown_without_a_weapon()
    {
        var s = new HudState();
        s.Settings.Scale = 1.2;
        s.Instruments.SetSecondaryCount(3);
        var b = HudArt.Build(s, 1280, 720, null);
        Assert.Contains(HudArt.Ammo, b.Hide);
        Assert.Contains(HudArt.Explosive, b.Show);
        Assert.Equal(HudStagePlacement.Compute(HudElement.Ammo, s.Settings, 1280, 720), b.Place[HudArt.Explosive]);
        Assert.DoesNotContain(HudArt.Ammo, b.Place.Keys);
        s.Instruments.SetGroupVisible(HudGroup.Ammo, false);
        b = HudArt.Build(s, 1280, 720, null);
        Assert.Contains(HudArt.Explosive, b.Hide);
        Assert.DoesNotContain(HudArt.Explosive, b.Place.Keys);
    }

    [Fact]
    public void Interaction_notice_and_hold_progress_are_drawn()
    {
        var s = new HudState();
        s.SetInteraction("Open", "Safe", "E", 0.4, "", "Requires a key");
        var sprites = HudArt.Build(s).Sprites.Where(x => x.Parent == HudArt.Rollover).ToList();
        Assert.Contains(sprites, x => x.Label?.Text == "Requires a key");
        var bar = Assert.Single(sprites, x => x.Bar is not null).Bar!;
        Assert.Equal(0.4, bar.Fraction, 6);
        s.SetInteraction("Open", "Safe", "E");
        sprites = HudArt.Build(s).Sprites.Where(x => x.Parent == HudArt.Rollover).ToList();
        Assert.DoesNotContain(sprites, x => x.Bar is not null);
        Assert.Equal(new[] { "E", "Open" }, sprites.Select(x => x.Label!.Text).ToArray());
        s.SetInteraction("Open", "Safe", "E", 1.7);
        Assert.Equal(1, HudArt.Build(s).Sprites.Single(x => x.Bar is not null).Bar!.Fraction, 6);
    }

    [Fact]
    public void Signature_ignores_sub_step_motion_but_not_real_changes()
    {
        SwfBindings Sprite(double x, double alpha, double fraction)
        {
            var b = new SwfBindings();
            b.Sprites.Add(new SwfSprite("t", "p", x, 3, alpha, null, new SwfSpriteBar(0, 0, 10, 2, fraction)));
            return b;
        }
        Assert.Equal(Sprite(10.0, 1, 0.5).Signature(), Sprite(10.1, 0.98, 0.505).Signature());
        Assert.NotEqual(Sprite(10.0, 1, 0.5).Signature(), Sprite(10.6, 1, 0.5).Signature());
        Assert.NotEqual(Sprite(10.0, 1, 0.5).Signature(), Sprite(10.0, 0.8, 0.5).Signature());
        Assert.NotEqual(Sprite(10.0, 1, 0.5).Signature(), Sprite(10.0, 1, 0.6).Signature());
        var s = new HudState();
        s.SetHeading(30);
        var first = HudArt.Build(s, 1280, 720, new CompassModel()).Signature();
        s.SetHeading(30.001);
        Assert.Equal(first, HudArt.Build(s, 1280, 720, new CompassModel()).Signature());
        s.SetHeading(40);
        Assert.NotEqual(first, HudArt.Build(s, 1280, 720, new CompassModel()).Signature());
    }

    [Fact]
    public void Signature_tells_the_key_and_notice_roles_apart_when_the_text_matches()
    {
        SwfBindings Role(double y, int size)
        {
            var b = new SwfBindings();
            b.Sprites.Add(new SwfSprite("", HudArt.Rollover, 0, 0, 1, new SwfSpriteText("E", -16, y, 32, 28, size, true)));
            return b;
        }
        Assert.NotEqual(Role(20, 20).Signature(), Role(102, 20).Signature());
        Assert.NotEqual(Role(20, 20).Signature(), Role(20, 18).Signature());
        Assert.Equal(Role(20, 20).Signature(), Role(20, 20).Signature());
    }

    [Fact]
    public void Enemy_meter_visibility_comes_from_show_not_frame_zero()
    {
        var s = new HudState();
        s.Instruments.SetEnemy("Deathclaw", 0.5);
        s.Instruments.SetEnemyDetails(null, true);
        s.Instruments.Tick(1);
        var b = HudArt.Build(s);
        Assert.Contains(HudStagePlacement.TopPath, b.Show);
        Assert.Contains(HudArt.Enemy, b.Show);
        Assert.Contains(HudArt.Enemy + "/LegendaryIcon_mc", b.Show);
        Assert.DoesNotContain(HudArt.Enemy + "/LegendaryIcon_mc", b.Hide);
        s.Instruments.SetEnemy("Raider", 0.5);
        s.Instruments.Tick(1);
        b = HudArt.Build(s);
        Assert.Contains(HudArt.Enemy + "/LegendaryIcon_mc", b.Hide);
        Assert.DoesNotContain(HudArt.Enemy + "/LegendaryIcon_mc", b.Show);
        using var stage = OpenHud();
        if (stage is null)
            return;
        var visible = new Dictionary<string, bool>();
        void Walk(SwfNode n)
        {
            visible[n.Path] = n.Visible;
            foreach (var c in n.Children)
                Walk(c);
        }
        Walk(stage.Root);
        foreach (var path in new[] { HudStagePlacement.TopPath, HudArt.Enemy, HudArt.Stealth, HudArt.Enemy + "/LegendaryIcon_mc" })
            Assert.True(visible[path]);
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
    public void Every_bound_path_exists_in_the_real_hud_movie()
    {
        using var stage = OpenHud();
        if (stage is null)
            return;
        var paths = new HashSet<string>();
        Collect(stage.Root, paths);
        var s = new HudState();
        s.SetHeading(30);
        s.SetMarkers(new[] { new CompassMarker("q", 60, CompassMarkerKind.Objective) });
        s.SetInteraction("Open", "Box", "E");
        s.Instruments.SetEnemy("Raider", 0.5);
        s.Instruments.Tick(1);
        s.Instruments.SetSneak("Hidden", 10, true);
        s.Instruments.SetRadiation(0.5);
        s.Instruments.SetSecondaryCount(2);
        s.Settings.Scale = 1.2;
        var b = HudArt.Build(s, 1280, 720, null);
        var bound = b.Hide.Concat(b.Show).Concat(b.Text.Keys).Concat(b.FillX.Keys).Concat(b.FillRight.Keys).Concat(b.Alpha.Keys).Concat(b.Place.Keys)
            .Concat(b.Sprites.Select(x => x.Parent)).Distinct().ToList();
        Assert.NotEmpty(bound);
        Assert.Empty(bound.Where(p => !paths.Contains(p)));
        foreach (var path in HudArt.Build(new HudState { Crosshair = CrosshairState.Interactable }).Hide)
            Assert.Contains(path, paths);
    }

    [Fact]
    public void Templates_read_from_the_real_hud_movie()
    {
        using var stage = OpenHud();
        if (stage is null)
            return;
        foreach (var template in HudArt.Templates())
        {
            var tree = stage.ReadTemplate(template);
            Assert.NotNull(tree);
            Assert.False(tree!.Visible);
            Assert.Equal(SwfStage.TemplatePrefix + template.Name, tree.Path);
            Assert.NotEmpty(tree.Children);
        }
        var direction = stage.ReadTemplate(new SwfTemplate("d", HudCompassArt.DirectionSymbol, null, 0))!;
        Assert.Contains(direction.Children, n => n.Name == "Direction_tf");
        Assert.Null(stage.ReadTemplate(new SwfTemplate("none", "NoSuchSymbol", null, 0)));
        Assert.Null(stage.ReadTemplate(new SwfTemplate("late", HudCompassArt.DirectionSymbol, null, 99)));
    }
}
