using System.Linq;
using OpenCommonwealth.Services.Ui.Swf;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class SpinnerArtTests
{
    [Fact]
    public void The_frame_loops_over_the_clip_at_the_movie_rate()
    {
        Assert.Equal(0, SpinnerArt.Frame(0, 30));
        Assert.Equal(0, SpinnerArt.Frame(1.0 / 30 * 0.5, 30));
        Assert.Equal(1, SpinnerArt.Frame(1.0 / 30 + 0.001, 30));
        Assert.Equal(56, SpinnerArt.Frame(56.5 / 30, 30));
        Assert.Equal(0, SpinnerArt.Frame(57.2 / 30, 30));
        Assert.Equal(3, SpinnerArt.Frame(60.5 / 30, 30));
        Assert.Equal(0, SpinnerArt.Frame(5, 0));
        Assert.Equal(0, SpinnerArt.Frame(-1, 30));
    }

    [Fact]
    public void There_is_one_template_per_frame_on_the_clip_path()
    {
        var templates = SpinnerArt.Templates("A/B").ToList();
        Assert.Equal(SpinnerArt.Frames, templates.Count);
        Assert.Equal(Enumerable.Range(0, SpinnerArt.Frames), templates.Select(t => t.Frame));
        Assert.All(templates, t => Assert.Equal("A/B", t.Path));
        Assert.Equal(SpinnerArt.Frames, templates.Select(t => t.Name).Distinct().Count());
    }

    [Fact]
    public void Driving_a_clip_empties_it_and_places_the_frame_template_in_it()
    {
        var b = new SwfBindings();
        SpinnerArt.Drive(b, "A/B", 7);
        Assert.Contains("A/B", b.Hollow);
        var sprite = Assert.Single(b.Sprites);
        Assert.Equal(SpinnerArt.Template(7), sprite.Template);
        Assert.Equal("A/B", sprite.Parent);

        var late = new SwfBindings();
        SpinnerArt.Drive(late, "A/B", 999);
        Assert.Equal(SpinnerArt.Template(SpinnerArt.Frames - 1), Assert.Single(late.Sprites).Template);
    }

    [Fact]
    public void A_clip_is_emptied_only_when_its_frame_template_exists()
    {
        var b = new SwfBindings();
        SpinnerArt.Drive(b, "A/B", 7);
        Assert.Contains("A/B", b.HollowWithTemplate(name => name == SpinnerArt.Template(7)));
        Assert.Empty(b.HollowWithTemplate(_ => false));
        Assert.Empty(b.HollowWithTemplate(name => name == SpinnerArt.Template(0)));
        Assert.Empty(new SwfBindings().HollowWithTemplate(_ => true));
    }

    [Fact]
    public void The_clock_restarts_at_frame_zero_each_time_the_spinner_is_shown()
    {
        var clock = new SpinnerClock();
        Assert.Equal(0, clock.Tick(0.5, true, 30));
        Assert.Equal(15, clock.Tick(0.5, true, 30));
        Assert.Equal(0, clock.Tick(0.5, false, 30));
        Assert.Equal(0, clock.Tick(0.5, true, 30));
        Assert.Equal(15, clock.Tick(0.5, true, 30));
        Assert.Equal(0, new SpinnerClock().Tick(-1, true, 30));
    }

    [Fact]
    public void A_new_frame_or_a_hollow_clip_changes_the_signature()
    {
        string Sign(int? frame)
        {
            var b = new SwfBindings();
            if (frame is { } f)
                SpinnerArt.Drive(b, "A/B", f);
            return b.Signature();
        }
        Assert.NotEqual(Sign(null), Sign(0));
        Assert.NotEqual(Sign(0), Sign(1));
        Assert.Equal(Sign(4), Sign(4));
    }

    [Fact]
    public void The_fade_and_loading_art_drive_the_spinner_only_when_given_a_frame()
    {
        Assert.Empty(FadeArt.Build(1, true).Hollow);
        var fade = FadeArt.Build(1, true, 3);
        Assert.Contains(FadeArt.SpinnerClip, fade.Hollow);
        Assert.Equal(SpinnerArt.Template(3), Assert.Single(fade.Sprites).Template);
        Assert.Empty(FadeArt.Build(0.5, false, 3).Sprites);

        Assert.Empty(LoadingArt.Build("tip", 1, 1).Hollow);
        var loading = LoadingArt.Build("tip", 1, 1, 5);
        Assert.Contains(LoadingArt.Spinner, loading.Hollow);
        Assert.Contains(loading.Sprites, s => s.Template == SpinnerArt.Template(5));
        Assert.Contains(LoadingArt.Spinner, LoadingArt.Build("", 0, 1, 5).Hollow);
    }
}
