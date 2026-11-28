using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyRadioStateTests
{
    static RadioModel Make() => new(new[]
    {
        new RadioStation("s2", "Beta Radio", "Swing", Enabled: false),
        new RadioStation("s3", "Gamma Waves", "Talk", Frequency: 104.7),
        new RadioStation("s1", "alpha FM", "Classical", Frequency: 98),
    });

    [Fact]
    public void Rows_list_enabled_stations_by_name_then_disabled_ones()
    {
        var radio = Make();

        Assert.Equal(new[] { "s1", "s3", "s2" }, radio.View.Visible.Select(s => s.Id));
    }

    [Fact]
    public void A_playing_tape_gets_the_first_row_and_leaves_when_cleared()
    {
        var radio = Make();

        radio.SetTape(new RadioTape("t1", "Tape Title"));

        Assert.Equal(4, radio.View.Visible.Count);
        Assert.True(radio.IsTapeRow(radio.View.Visible[0]));
        Assert.Equal("Tape Title", radio.View.Visible[0].Name);
        Assert.Equal("s1", radio.CurrentKey);

        radio.SetTape(null);

        Assert.Equal(new[] { "s1", "s3", "s2" }, radio.View.Visible.Select(s => s.Id));
    }

    [Fact]
    public void A_disabled_station_is_refused_with_an_error_and_nothing_else_starts()
    {
        var radio = Make();

        Assert.Null(radio.Play("s2"));

        Assert.Equal(RadioModel.Unavailable, radio.Error);
        Assert.Null(radio.PlayingId);

        radio.Play("s1");
        Assert.Null(radio.Play("s2"));
        Assert.Equal("s1", radio.PlayingId);
        Assert.Equal(RadioModel.Unavailable, radio.Error);
    }

    [Fact]
    public void The_error_clears_on_the_next_successful_tune_or_stop()
    {
        var radio = Make();
        radio.Play("s2");

        Assert.Equal(new RadioEvent(RadioEventKind.Play, "s3"), radio.Play("s3"));
        Assert.Null(radio.Error);

        radio.Play("s2");
        Assert.Equal(RadioModel.Unavailable, radio.Error);
        radio.Stop();
        Assert.Null(radio.Error);
    }

    [Fact]
    public void Accepting_a_disabled_row_in_the_view_refuses_and_shows_the_error()
    {
        var radio = Make();
        var view = new RadioListView(radio);
        var events = new List<RadioEvent>();
        view.Command += events.Add;

        Assert.True(view.Select(2));
        Assert.False(view.Handle(MenuInputAction.Accept));

        Assert.Empty(events);
        Assert.Equal(RadioModel.Unavailable, radio.Error);
        Assert.Equal("Beta Radio (unavailable)", view.RowText(2));
        Assert.Equal("Radio off", view.Summary);
    }

    [Fact]
    public void Next_and_prev_skip_disabled_stations_and_wrap()
    {
        var radio = Make();

        Assert.Equal("s1", radio.Next()?.StationId);
        Assert.Equal("s3", radio.Next()?.StationId);
        Assert.Equal("s1", radio.Next()?.StationId);
        Assert.Equal("s3", radio.Prev()?.StationId);

        radio.Stop();
        Assert.Equal("s3", radio.Prev()?.StationId);
    }

    [Fact]
    public void Next_does_nothing_when_no_station_is_enabled()
    {
        var radio = new RadioModel(new[] { new RadioStation("s1", "Alpha FM", Enabled: false) });

        Assert.Null(radio.Next());
        Assert.Null(radio.Prev());
        Assert.Null(radio.PlayingId);
    }

    [Fact]
    public void Accepting_the_tape_row_requests_to_stop_the_tape_and_the_row_reads_stop_tape()
    {
        var radio = Make();
        var view = new RadioListView(radio);
        var events = new List<RadioEvent>();
        view.Command += events.Add;
        radio.SetTape(new RadioTape("t1", "Tape Title"));

        Assert.True(view.Select(0));
        Assert.Contains(new MenuHint(MenuInputAction.Accept, "Stop tape"), view.Hints);
        Assert.Equal("Tape Title (playing)", view.RowText(0));
        Assert.True(view.Handle(MenuInputAction.Accept));

        Assert.Equal(new[] { new RadioEvent(RadioEventKind.StopTape, null, "t1") }, events);
        Assert.Equal("t1", radio.Tape?.Id);
        Assert.Null(radio.PlayingId);
    }

    [Fact]
    public void Stopping_a_tape_with_none_playing_is_not_an_event()
    {
        var radio = Make();

        Assert.Null(radio.StopTape());
        Assert.False(new RadioListView(radio).StopTape());
    }

    [Fact]
    public void Now_playing_lines_cover_off_station_and_tape()
    {
        var radio = Make();
        Assert.Equal(new[] { "RADIO OFF" }, radio.NowPlayingLines);

        radio.Play("s1");
        Assert.Equal(new[] { "NOW PLAYING", "alpha FM", "FREQ 98.0" }, radio.NowPlayingLines);

        radio.SetProgress(new RadioProgress(2, 5, 75));
        radio.SetNowPlaying("Some Song");
        Assert.Equal(new[] { "NOW PLAYING", "alpha FM", "FREQ 98.0", "TRACK 2 / 5", "Some Song", "01:15" }, radio.NowPlayingLines);

        radio.SetTape(new RadioTape("t1", "Tape Title", 2, 4));
        radio.SetProgress(new RadioProgress(0, 0, 5));
        Assert.Equal(new[] { "HOLOTAPE", "Tape Title", "PART 2 / 4", "00:05" }, radio.NowPlayingLines);

        radio.SetTape(null);
        Assert.Equal("NOW PLAYING", radio.NowPlayingLines[0]);
    }

    [Fact]
    public void Progress_resets_when_the_station_changes_or_stops()
    {
        var radio = Make();
        radio.Play("s1");
        radio.SetProgress(new RadioProgress(1, 3, 10));

        radio.Next();
        Assert.Null(radio.Progress);

        radio.SetProgress(new RadioProgress(1, 3, 10));
        radio.Stop();
        Assert.Null(radio.Progress);
    }

    [Fact]
    public void Frequency_is_shown_with_one_decimal_and_skipped_when_unknown()
    {
        var radio = Make();

        radio.Play("s3");
        Assert.Equal("FREQ 104.7", radio.NowPlayingLines[2]);

        var bare = new RadioModel(new[] { new RadioStation("x", "Alpha FM") });
        bare.Play("x");
        Assert.Equal(new[] { "NOW PLAYING", "Alpha FM" }, bare.NowPlayingLines);
    }

    [Fact]
    public void Moving_the_cursor_clears_the_unavailable_error_but_staying_put_keeps_it()
    {
        var radio = Make();
        var view = new RadioListView(radio);
        var disabled = radio.View.Visible.ToList().FindIndex(s => s.Id == "s2");
        view.Select(disabled);
        view.Handle(MenuInputAction.Accept);
        Assert.Equal(RadioModel.Unavailable, radio.Error);

        view.Select(disabled);
        view.Refresh();
        Assert.Equal(RadioModel.Unavailable, radio.Error);

        view.Select(0);
        Assert.Null(radio.Error);
    }

    [Fact]
    public void Moving_with_the_keyboard_clears_the_error_too()
    {
        var radio = Make();
        var view = new RadioListView(radio);
        view.Select(radio.View.Visible.ToList().FindIndex(s => s.Id == "s2"));
        view.Handle(MenuInputAction.Accept);
        Assert.Equal(RadioModel.Unavailable, radio.Error);

        view.Handle(MenuInputAction.Up);

        Assert.Null(radio.Error);
    }
}
