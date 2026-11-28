using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Ui.Swf;
using Radicle.Interop;
using OpenCommonwealth.Tests;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class InterfaceArchiveFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> Reason = new(Probe);

    public InterfaceArchiveFactAttribute() => Skip = Reason.Value;

    // Constraint: must not throw; an exception in an attribute constructor aborts discovery of the whole assembly.
    private static string? Probe()
    {
        try
        {
            if (Fo4Data.Folder is not { } data)
                return $"needs {Fo4Data.Variable} set to the Fallout 4 Data folder";
            using var assets = Fo4Data.Assets();
            foreach (var movie in new[] { MainMenuArt.Movie, LoadingArt.Movie, FadeArt.Movie, SwfFontOutlines.FontMovies[0] })
                if (assets?.Read(movie) is null) return $"{movie} not found in {data}";
            return null;
        }
        catch (Exception e)
        {
            return $"could not read the Fallout 4 archives: {e.GetType().Name}";
        }
    }
}

public sealed class MainMenuArtTests
{
    private static List<MainMenuRow> Rows(int selected, params (string Label, bool Enabled)[] rows) =>
        rows.Select((r, i) => new MainMenuRow(r.Label, r.Enabled, i == selected)).ToList();

    private static SwfSprite[] Under(SwfBindings b) => b.Sprites.Where(s => s.Parent == MainMenuArt.List).ToArray();

    [Fact]
    public void Rows_sit_on_the_vanilla_pitch_and_labels_are_upper_case()
    {
        var b = MainMenuArt.Build(Rows(0, ("New Game", true), ("Quit", true)), false);
        var labels = Under(b).Where(s => s.Label is not null).ToArray();

        Assert.Equal(["NEW GAME", "QUIT"], labels.Select(s => s.Label!.Text));
        Assert.Equal(0, labels[0].Y);
        Assert.Equal(29.2, labels[1].Y, 6);
        Assert.Equal(new[] { 12.0, 2.6, 121.0 }, new[] { labels[0].Label!.X, labels[0].Label!.Y, labels[0].Label!.Width });
        Assert.Equal(19, labels[0].Label!.FontSize);
        Assert.True(labels[0].Label!.Shrink);
        Assert.Equal(SwfAlign.Left, labels[0].Label!.Align);
    }

    [Fact]
    public void The_selected_row_is_a_solid_white_bar_emitted_before_its_black_label()
    {
        var b = MainMenuArt.Build(Rows(1, ("A", true), ("B", true)), false);
        var sprites = Under(b);

        var barIndex = Array.FindIndex(sprites, s => s.Bar is not null);
        var labelIndex = Array.FindIndex(sprites, s => s.Label?.Text == "B");
        Assert.True(barIndex >= 0 && barIndex < labelIndex);
        Assert.Equal(0, sprites[barIndex].Bar!.Track);
        Assert.Equal(1, sprites[barIndex].Alpha);
        Assert.Equal(29.2, sprites[barIndex].Y, 6);
        Assert.Equal((134.12, 28.0), (sprites[barIndex].Bar!.Width, sprites[barIndex].Bar!.Height));
        Assert.Equal(MainMenuArt.Selected, sprites[labelIndex].Label!.Rgb);
        Assert.Equal(MainMenuArt.Enabled, sprites.First(s => s.Label?.Text == "A").Label!.Rgb);
        Assert.Single(sprites, s => s.Bar is not null);
    }

    [Fact]
    public void Disabled_rows_use_the_vanilla_greys_and_a_faint_bar_when_selected()
    {
        var rest = MainMenuArt.Build(Rows(0, ("A", true), ("B", false)), false);
        Assert.Equal(4473924u, Under(rest).Single(s => s.Label?.Text == "B").Label!.Rgb);

        var selected = MainMenuArt.Build(Rows(1, ("A", true), ("B", false)), false);
        var bar = Under(selected).Single(s => s.Bar is not null);
        Assert.Equal(0.35, bar.Alpha);
        Assert.Equal(2236962u, Under(selected).Single(s => s.Label?.Text == "B").Label!.Rgb);
    }

    [Fact]
    public void Only_the_title_menu_shows_the_logo_and_every_other_panel_is_hidden()
    {
        var title = MainMenuArt.Build(Rows(0, ("A", true)), false);
        var pause = MainMenuArt.Build(Rows(0, ("A", true)), true);

        Assert.Contains(MainMenuArt.Root + MainMenuArt.Logo, title.Show);
        Assert.DoesNotContain(MainMenuArt.Root + MainMenuArt.Logo, title.Hide);
        Assert.Contains(MainMenuArt.Root + MainMenuArt.Logo, pause.Hide);
        foreach (var panel in MainMenuArt.Panels)
            Assert.Contains(MainMenuArt.Root + panel, title.Hide);
        Assert.DoesNotContain(MainMenuArt.Root + "MainPanel_mc", title.Hide);
    }

    [Fact]
    public void More_than_nine_rows_scroll_with_the_selection_and_row_rects_follow_the_window()
    {
        var rows = Enumerable.Range(0, 12).Select(i => new MainMenuRow("Row" + i, true, i == 11)).ToList();
        var scroll = new SwfScroll();
        var b = MainMenuArt.Build(rows, true, "", scroll);

        var labels = Under(b).Where(s => s.Label is not null).Select(s => s.Label!.Text).ToArray();
        Assert.Equal(9, labels.Length);
        Assert.Equal("ROW3", labels[0]);
        Assert.Equal("ROW11", labels[^1]);
        Assert.Equal(3, MainMenuArt.Window(scroll, rows).First);
        Assert.Equal((303.0, 302.0 + 29.2, 134.12, 28.0), MainMenuArt.RowRect(1));
    }

    [Fact]
    public void A_notice_is_a_wrapped_label_under_the_list_and_part_of_the_signature()
    {
        var rows = Rows(0, ("A", true));
        var quiet = MainMenuArt.Build(rows, false);
        var noisy = MainMenuArt.Build(rows, false, "Quicksave failed");

        Assert.Contains(Under(noisy), s => s.Label is { Wrap: true, Text: "Quicksave failed" });
        Assert.NotEqual(quiet.Signature(), noisy.Signature());
    }

    [Fact]
    public void Sprite_text_colour_alignment_and_bar_track_change_the_signature()
    {
        SwfBindings One(SwfSpriteText t, SwfSpriteBar? bar = null)
        {
            var b = new SwfBindings();
            b.Sprites.Add(new SwfSprite("", "p", 0, 0, 1, t, bar));
            return b;
        }
        var baseline = One(new SwfSpriteText("x", 0, 0, 10, 10, 10, false)).Signature();

        Assert.NotEqual(baseline, One(new SwfSpriteText("x", 0, 0, 10, 10, 10, false, 0)).Signature());
        Assert.NotEqual(baseline, One(new SwfSpriteText("x", 0, 0, 10, 10, 10, false, Align: 0)).Signature());
        Assert.NotEqual(baseline, One(new SwfSpriteText("x", 0, 0, 10, 10, 10, false, Shrink: true)).Signature());
        Assert.NotEqual(baseline, One(new SwfSpriteText("x", 0, 0, 10, 10, 10, false, Wrap: true)).Signature());
        Assert.NotEqual(One(new SwfSpriteText("x", 0, 0, 10, 10, 10, false), new SwfSpriteBar(0, 0, 5, 5, 1)).Signature(),
            One(new SwfSpriteText("x", 0, 0, 10, 10, 10, false), new SwfSpriteBar(0, 0, 5, 5, 1, 0)).Signature());
    }

    [Fact]
    public void Loading_text_grows_upward_one_line_height_per_extra_line_and_the_border_follows()
    {
        Assert.Equal(81.35, LoadingArt.TextTop(1), 6);
        Assert.Equal(81.35 - 22.8 * 2, LoadingArt.TextTop(3), 6);
        Assert.Equal(81.35, LoadingArt.TextTop(0), 6);

        var b = LoadingArt.Build("tip", 7, 3);
        var tip = b.Sprites.Single(s => s.Label?.Text == "tip");
        var number = b.Sprites.Single(s => s.Label?.Text == "VDSG No. 7");
        Assert.Equal(LoadingArt.TextTop(3), tip.Y, 6);
        Assert.Equal(LoadingArt.TextTop(3) - 29, number.Y, 6);
        Assert.True(tip.Label!.Wrap);
        Assert.Equal(4, b.Sprites.Count(s => s.Bar is { Track: 0 }));
        var (x, y, w, h) = LoadingArt.Border(3);
        Assert.Equal(13 - 8, x);
        Assert.Equal(LoadingArt.TextTop(3) - 3, y, 6);
        Assert.Equal(504.95 + 13, w, 6);
        Assert.Equal(2 + 3 * 22.8 + 13, h, 6);
    }

    [Fact]
    public void The_loading_slide_number_is_the_tip_index_plus_one_and_zero_without_tips()
    {
        var model = new OpenCommonwealth.Services.Ui.Hud.LoadingModel();
        model.Begin("x", ["a", "b", "c"], 1);
        Assert.Equal(2, model.TipNumber);
        model.Begin("x");
        Assert.Equal(0, model.TipNumber);
    }

    [Fact]
    public void Loading_without_a_tip_is_minimal_mode_and_the_xp_meter_is_never_fed()
    {
        var minimal = LoadingArt.Build("", 0, 1);
        Assert.Empty(minimal.Sprites);
        foreach (var path in LoadingArt.Hidden)
            Assert.Contains(path, minimal.Hide);
        Assert.Contains(LoadingArt.Left + "/LevelMeter_mc", LoadingArt.Build("tip", 1, 1).Hide);
        Assert.DoesNotContain(LoadingArt.Black, minimal.Hide);
    }

    [Fact]
    public void Fade_shows_the_spinner_with_the_fade_alpha_only_while_black_and_never_the_rect()
    {
        var clear = FadeArt.Build(0.5, false);
        Assert.Contains(FadeArt.Spinner, clear.Hide);
        Assert.Contains(FadeArt.Rect, clear.Hide);

        var black = FadeArt.Build(1, true);
        Assert.DoesNotContain(FadeArt.Spinner, black.Hide);
        Assert.Equal(1, black.Alpha[FadeArt.Spinner]);
        Assert.Contains(FadeArt.Rect, black.Hide);
        Assert.Contains(FadeArt.Spinner, FadeArt.Build(0, true).Hide);
    }

    private static SwfStage Movie(string entry)
    {
        var bytes = Fo4Data.Read(entry);
        Assert.NotNull(bytes);
        return SwfStage.Read(bytes);
    }

    private static void Collect(SwfNode node, HashSet<string> into)
    {
        into.Add(node.Path);
        foreach (var child in node.Children)
            Collect(child, into);
    }

    [InterfaceArchiveFact]
    public void Every_top_level_clip_of_the_real_main_menu_is_hidden_or_shown_and_every_bound_path_exists()
    {
        var stage = Movie(MainMenuArt.Movie);
        using (stage)
        {
            var paths = new HashSet<string>();
            Collect(stage.Root, paths);
            var top = stage.Root.Children.Single(c => c.Name == "Menu_mc").Children.Select(c => c.Name).ToArray();
            var accounted = MainMenuArt.Panels.Concat(MainMenuArt.Shown).ToHashSet();
            Assert.Empty(top.Where(n => !accounted.Contains(n)));
            var b = MainMenuArt.Build(Rows(0, ("A", true)), false);
            foreach (var path in b.Hide.Concat(b.Show).Append(MainMenuArt.List))
                Assert.Contains(path, paths);
        }
    }

    [InterfaceArchiveFact]
    public void The_real_loading_and_fade_movies_have_every_bound_path_and_no_unaccounted_top_level_clip()
    {
        var loading = Movie(LoadingArt.Movie);
        using (loading)
        {
            var paths = new HashSet<string>();
            Collect(loading.Root, paths);
            var b = LoadingArt.Build("tip", 1, 1);
            foreach (var path in b.Hide.Append(LoadingArt.Left).Append(LoadingArt.Black))
                Assert.Contains(path, paths);
            var menu = loading.Root.Children.Single(c => c.Name == "FilterHolder_mc").Children.Single(c => c.Name == "Menu_mc");
            Assert.Equal(["BlackRect_mc", "LeftText_mc", "VaultTecLogo_mc"], menu.Children.Select(c => c.Name).OrderBy(n => n));
        }
        var fade = Movie(FadeArt.Movie);
        using (fade)
        {
            var paths = new HashSet<string>();
            Collect(fade.Root, paths);
            Assert.Contains(FadeArt.Rect, paths);
            Assert.Contains(FadeArt.Spinner, paths);
            Assert.Equal(["FadeRect_mc", "SpinnerIcon_mc"], fade.Root.Children.Single(c => c.Name == "Menu_mc").Children.Select(c => c.Name).OrderBy(n => n));
        }
    }

    private static IEnumerable<SwfNode> Flatten(SwfNode? node) =>
        node is null ? [] : new[] { node }.Concat(node.Children.SelectMany(Flatten));

    [InterfaceArchiveFact]
    public void The_real_spinner_clips_resolve_every_frame_template_and_the_morphs_change_ratio()
    {
        foreach (var (entry, clip, templates) in new[] { (LoadingArt.Movie, LoadingArt.Spinner, LoadingArt.Templates()), (FadeArt.Movie, FadeArt.SpinnerClip, FadeArt.Templates()) })
        {
            using var stage = Movie(entry);
            var paths = new HashSet<string>();
            Collect(stage.Root, paths);
            Assert.Contains(clip, paths);
            var ratios = new HashSet<ushort>();
            foreach (var template in templates)
            {
                var root = stage.ReadTemplate(template);
                Assert.NotNull(root);
                foreach (var node in Flatten(root).Where(n => n.Ratio is not null))
                    ratios.Add(node.Ratio!.Value);
            }
            Assert.True(ratios.Count > 1);

            var morph = templates.Select(stage.ReadTemplate).SelectMany(Flatten).First(n => n.Ratio is > 0);
            var middle = stage.ShapeSvg(morph.CharacterId, morph.Ratio);
            Assert.StartsWith("<svg", middle);
            Assert.NotEqual(middle, stage.ShapeSvg(morph.CharacterId, 0));
            Assert.NotEqual(middle, stage.ShapeSvg(morph.CharacterId, 65535));
            Assert.Same(stage.ShapeSvg(morph.CharacterId, 0), stage.ShapeSvg(morph.CharacterId));
            Assert.False(stage.MorphExportFailed);
        }
    }
}
