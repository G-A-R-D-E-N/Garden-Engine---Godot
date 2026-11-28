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

public sealed class SwfFontSourceTests
{
    public sealed record FakeMetrics(IReadOnlyList<double> Advances);

    public sealed class FakeFile
    {
        public List<int> TtfCalls { get; } = new();

        public byte[] FontTtf(int tagIndex)
        {
            TtfCalls.Add(tagIndex);
            return new byte[16].Select((_, i) => (byte)(tagIndex + i)).ToArray();
        }

        public FakeMetrics FontMetrics(int tagIndex) => new([0.5, 0.25, 0.75]);
    }

    public sealed class FailingFile
    {
        public int Calls { get; private set; }

        public byte[] FontTtf(int tagIndex)
        {
            Calls++;
            throw new RadicleException(default, "no such font");
        }

        public FakeMetrics FontMetrics(int tagIndex)
        {
            Calls++;
            throw new RadicleException(default, "no such font");
        }
    }

    public sealed class DeadFile
    {
        public int Calls { get; private set; }

        public byte[] FontTtf(int tagIndex)
        {
            Calls++;
            throw new EntryPointNotFoundException();
        }
    }

    private sealed class Source(string known, byte[]? bytes) : ISwfFontSource
    {
        public List<string> Asked { get; } = new();

        public byte[]? Ttf(string fontClass)
        {
            Asked.Add(fontClass);
            return fontClass == known ? bytes : null;
        }

        public SwfFontMetrics? Metrics(string fontClass) => null;
    }

    private static readonly byte[] Sfnt = new byte[16];

    private static SwfFontLocation? Locate(string cls) => cls == "$MAIN_Font" ? new SwfFontLocation(7, 20480, 16384, ['A', 'B', 'V']) : null;

    [Fact]
    public void Without_a_source_the_resolver_returns_nothing_so_the_stand_in_stays()
    {
        Assert.Null(SwfFontResolver.Ttf(null, "$MAIN_Font", false));
    }

    [Fact]
    public void The_resolver_prefers_the_label_class_then_the_weight_default()
    {
        var own = new Source("$Other", Sfnt);
        Assert.Same(Sfnt, SwfFontResolver.Ttf(own, "$Other", false));
        Assert.Equal(["$Other"], own.Asked);

        var weight = new Source(SwfFontResolver.Bold, Sfnt);
        Assert.Same(Sfnt, SwfFontResolver.Ttf(weight, "", true));
        Assert.Same(Sfnt, SwfFontResolver.Ttf(weight, "$Unknown", true));
        Assert.Null(SwfFontResolver.Ttf(weight, "", false));
        Assert.Equal([SwfFontResolver.Bold, "$Unknown", SwfFontResolver.Bold, SwfFontResolver.Regular], weight.Asked);
    }

    [Fact]
    public void A_throwing_source_means_no_exported_font()
    {
        Assert.Null(SwfFontResolver.Ttf(new ReflectedFontSource(new DeadFile(), Locate), "$MAIN_Font", false));
        Assert.Null(SwfFontResolver.Ttf(new Throwing(), "$MAIN_Font", false));
    }

    private sealed class Throwing : ISwfFontSource
    {
        public byte[]? Ttf(string fontClass) => throw new InvalidOperationException();

        public SwfFontMetrics? Metrics(string fontClass) => throw new InvalidOperationException();
    }

    [Fact]
    public void The_reflected_source_reads_the_sfnt_of_the_located_tag_once()
    {
        var file = new FakeFile();
        var source = new ReflectedFontSource(file, Locate);
        var first = source.Ttf("$MAIN_Font");
        Assert.NotNull(first);
        Assert.Equal(7, first![0]);
        Assert.Same(first, source.Ttf("$MAIN_Font"));
        Assert.Equal([7], file.TtfCalls);
        Assert.Null(source.Ttf("$Missing"));
        Assert.Equal([7], file.TtfCalls);
    }

    [Fact]
    public void The_reflected_source_maps_advances_by_code()
    {
        var source = new ReflectedFontSource(new FakeFile(), Locate);
        var metrics = source.Metrics("$MAIN_Font")!;
        Assert.Equal(20480, metrics.UnitsPerEm);
        Assert.Equal(0.5, metrics.Advances['A']);
        Assert.Equal(0.75, metrics.Advances['V']);
        Assert.Null(source.Metrics("$Missing"));
    }

    [Fact]
    public void A_radicle_error_means_unavailable_is_cached_and_keeps_the_source_alive()
    {
        var file = new FailingFile();
        var source = new ReflectedFontSource(file, Locate);
        Assert.Null(source.Ttf("$MAIN_Font"));
        Assert.Null(source.Ttf("$MAIN_Font"));
        Assert.Null(source.Metrics("$MAIN_Font"));
        Assert.Null(source.Metrics("$MAIN_Font"));
        Assert.Equal(2, file.Calls);
        Assert.True(source.HasTtf);
    }

    [Fact]
    public void A_failing_locate_is_unavailable_not_an_exception()
    {
        var source = new ReflectedFontSource(new FakeFile(), _ => throw new InvalidOperationException());
        Assert.Null(source.Ttf("$MAIN_Font"));
        Assert.Null(source.Metrics("$MAIN_Font"));
    }

    [Fact]
    public void The_reflection_names_match_the_radicle_request()
    {
        Assert.Equal("FontTtf", ReflectedFontSource.TtfMethod);
        Assert.Equal("FontMetrics", ReflectedFontSource.MetricsMethod);
        Assert.Equal("Advances", ReflectedFontSource.AdvancesProperty);
        var request = Path.Combine("E:", "GardenEngine", "Obsidian", "Garden Engine", "Project", "Radicle font export request.md");
        if (!File.Exists(request))
            return;
        var text = File.ReadAllText(request);
        foreach (var name in new[] { ReflectedFontSource.TtfMethod, ReflectedFontSource.MetricsMethod, ReflectedFontSource.AdvancesProperty })
            Assert.Contains("`" + name, text);
    }

    [Fact]
    public void The_pinned_binding_has_the_font_export_the_source_reflects_on()
    {
        Assert.True(ReflectedFontSource.BindingHasTtf);
        Assert.Equal(typeof(byte[]), typeof(SwfFile).GetMethod(ReflectedFontSource.TtfMethod, [typeof(int)])!.ReturnType);
        var metrics = typeof(SwfFile).GetMethod(ReflectedFontSource.MetricsMethod, [typeof(int)]);
        Assert.NotNull(metrics);
        Assert.NotNull(metrics!.ReturnType.GetProperty(ReflectedFontSource.AdvancesProperty));
    }

    [Fact]
    public void A_file_without_the_methods_is_unavailable_and_never_throws()
    {
        var source = new ReflectedFontSource(new object(), Locate);
        Assert.False(source.HasTtf);
        Assert.False(source.HasMetrics);
        Assert.Null(source.Ttf("$MAIN_Font"));
        Assert.Null(source.Metrics("$MAIN_Font"));
    }

    [Fact]
    public void A_missing_native_entry_point_disables_the_source_after_the_first_call()
    {
        var file = new DeadFile();
        var source = new ReflectedFontSource(file, Locate);
        Assert.True(source.HasTtf);
        Assert.Null(source.Ttf("$MAIN_Font"));
        Assert.False(source.HasTtf);
        Assert.Null(source.Ttf("$MAIN_Font"));
        Assert.Equal(1, file.Calls);
    }

    [Fact]
    public void The_glyph_advance_is_the_font_advance_when_known_and_the_outline_estimate_otherwise()
    {
        var commands = new List<SwfShapeCommandRecord>
        {
            new(SwfShapeCommandKind.Move, 0, 0, 0, 0, 0, 0, 0),
            new(SwfShapeCommandKind.StraightEdge, 2048, 0, 0, 0, 0, 0, 0),
            new(SwfShapeCommandKind.StraightEdge, 0, 2048, 0, 0, 0, 0, 0),
        };
        Assert.Equal(2, SwfFontOutlines.Outline(commands, 20480, 0, 20)!.Advance);
        Assert.Equal(13, SwfFontOutlines.Outline(commands, 20480, 0, 20, 0.65)!.Advance, 9);
    }

    [Fact]
    public void The_real_font_movie_uses_the_pinned_radicle_without_failing()
    {
        if (Fo4Data.Read(SwfFontOutlines.FontMovies[0]) is not { } movieBytes)
            return;
        using var file = SwfFile.FromBytes(movieBytes);
        var source = ReflectedFontSource.For(file);
        var exported = typeof(SwfFile).GetMethod(ReflectedFontSource.TtfMethod, [typeof(int)]) is not null;
        var ttf = SwfFontResolver.Ttf(source, "$MAIN_Font", false);
        Assert.Equal(exported, ttf is not null);
        var glyph = SwfFontOutlines.Read(file, "$Controller_Buttons_inverted", 'A', 20, source);
        Assert.NotNull(glyph);
        Assert.InRange(glyph!.Advance, 8, 40);
        Assert.Equal(SwfFontOutlines.Read(file, "$Controller_Buttons_inverted", 'A', 20)!.Advance, SwfFontOutlines.Read(file, "$Controller_Buttons_inverted", 'A', 20, new Throwing())!.Advance);
        if (exported)
            Assert.Equal(true, source.Metrics("$Controller_Buttons_inverted")?.Advances.ContainsKey('A'));
    }

    [Fact]
    public void The_compass_marker_frames_follow_the_movie_frame_labels()
    {
        Assert.Equal(0, HudCompassArt.MarkerFrame(CompassMarkerKind.Objective));
        Assert.Equal(2, HudCompassArt.MarkerFrame(CompassMarkerKind.Custom));
        Assert.Equal(5, HudCompassArt.MarkerFrame(CompassMarkerKind.Hostile));
        var frames = Enum.GetValues<CompassMarkerKind>().Where(k => k != CompassMarkerKind.Location).Select(HudCompassArt.MarkerFrame).ToList();
        Assert.Equal(frames.Count, frames.Distinct().Count());
        Assert.Equal(HudCompassArt.LocationSymbol, HudCompassArt.Templates().Single(t => t.Name == HudCompassArt.MarkerTemplate(CompassMarkerKind.Location)).Symbol);
    }

    [Fact]
    public void The_default_hud_colour_is_the_exe_initial_value()
    {
        Assert.Equal(HudColor.FromBytes(18, 255, 21), HudColor.Default);
    }
    private static double Width(SwfFontMetrics metrics, string text, int px) => text.Sum(c => metrics.Advances[c]) * px;

    [InterfaceArchiveFact]
    public void Text_drawn_at_the_text_scale_is_as_wide_as_the_vanilla_barter_capture()
    {
        if (Fo4Data.Read(SwfFontOutlines.FontMovies[0]) is not { } movie)
            return;
        var metrics = ReflectedFontSource.For(SwfFile.FromBytes(movie)).Metrics(SwfFontResolver.Bold)!;
        Assert.InRange(Width(metrics, "MY INVENTORY", SwfTextScale.Px(25)), 148 * 0.92, 148 * 1.12);
        Assert.InRange(Width(metrics, "2175", SwfTextScale.Px(28 * 0.7)), 36 * 0.92, 36 * 1.12);
    }
}
