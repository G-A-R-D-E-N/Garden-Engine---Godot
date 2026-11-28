using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyRadioTests
{
    static RadioModel Make() => new(new[]
    {
        new RadioStation("s1", "Alpha FM", "Classical"),
        new RadioStation("s2", "Beta Radio", "Swing"),
        new RadioStation("s3", "Gamma Waves", "Talk"),
    });

    [Fact]
    public void Next_and_prev_step_through_stations_starting_from_either_end_and_wrapping()
    {
        var radio = Make();

        Assert.Equal(new RadioEvent(RadioEventKind.Play, "s1"), radio.Next());
        Assert.Equal("s2", radio.Next()?.StationId);
        Assert.Equal("s3", radio.Next()?.StationId);
        Assert.Equal("s1", radio.Next()?.StationId);
        Assert.Equal("s3", radio.Prev()?.StationId);
        Assert.Equal("s3", radio.PlayingId);

        radio.Stop();
        Assert.Equal("s3", radio.Prev()?.StationId);
    }

    [Fact]
    public void Playing_the_same_or_an_unknown_station_is_not_an_event()
    {
        var radio = Make();
        radio.Play("s2");

        Assert.Null(radio.Play("s2"));
        Assert.Null(radio.Play("nope"));
        Assert.Equal("Beta Radio", radio.Playing?.Name);
    }

    [Fact]
    public void Stop_reports_the_station_it_stopped_and_is_silent_when_nothing_plays()
    {
        var radio = Make();

        Assert.Null(radio.Stop());
        radio.Play("s1");

        Assert.Equal(new RadioEvent(RadioEventKind.Stop, "s1"), radio.Stop());
        Assert.Null(radio.Playing);
        Assert.Equal("s1", radio.Next()?.StationId);
    }

    [Fact]
    public void Muting_survives_station_changes_and_controls_audibility()
    {
        var radio = Make();
        radio.Play("s1");

        Assert.Equal(new RadioEvent(RadioEventKind.Mute, "s1"), radio.ToggleMute());
        Assert.True(radio.Muted);

        radio.Next();
        Assert.True(radio.Muted);
        Assert.Equal("s2", radio.PlayingId);

        Assert.Equal(new RadioEvent(RadioEventKind.Unmute, "s2"), radio.ToggleMute());
        Assert.False(radio.Muted);
        Assert.Null(radio.SetMuted(false));
    }

    [Fact]
    public void Now_playing_text_belongs_to_the_current_station_and_clears_on_change_or_stop()
    {
        var radio = Make();

        radio.SetNowPlaying("ignored");
        Assert.Null(radio.NowPlayingText);

        radio.Play("s1");
        radio.SetNowPlaying("Some Song");
        Assert.Equal("Some Song", radio.NowPlayingText);

        radio.Next();
        Assert.Null(radio.NowPlayingText);

        radio.SetNowPlaying("Another");
        radio.Stop();
        Assert.Null(radio.NowPlayingText);
    }

    [Fact]
    public void Activating_the_cursor_station_plays_it_and_activating_the_playing_one_stops_it()
    {
        var radio = Make();

        Assert.Equal(new RadioEvent(RadioEventKind.Play, "s1"), radio.PlayCurrent());
        Assert.Equal(new RadioEvent(RadioEventKind.Stop, "s1"), radio.PlayCurrent());

        radio.PlayCurrent();
        radio.View.Move(CursorMove.Down);
        Assert.Equal(new RadioEvent(RadioEventKind.Play, "s2"), radio.PlayCurrent());
        Assert.Equal("s2", radio.PlayingId);
    }

    [Fact]
    public void The_station_cursor_wraps_and_is_independent_of_what_is_playing()
    {
        var radio = Make();
        radio.Play("s2");

        radio.View.Move(CursorMove.Up);

        Assert.Equal("s3", radio.CurrentKey);
        Assert.Equal("s2", radio.PlayingId);
    }

    [Fact]
    public void Removing_the_playing_station_stops_playback_and_replacing_one_updates_its_name()
    {
        var radio = Make();
        radio.Play("s2");

        radio.Upsert(new RadioStation("s2", "Beta Radio 2", "Swing"));
        Assert.Equal("Beta Radio 2", radio.Playing?.Name);

        Assert.True(radio.Remove("s2"));
        Assert.Null(radio.PlayingId);
        Assert.Equal(new[] { "s1", "s3" }, radio.View.Visible.Select(s => s.Id));
        Assert.False(radio.Remove("s2"));
    }

    [Fact]
    public void Search_and_select_support_global_search_jumps_even_through_a_text_filter()
    {
        var radio = Make();

        var hit = Assert.Single(radio.Search(ListSearch.Tokenize("swing")));
        Assert.Equal(new PipBoySearchHit(PipBoyTab.Radio, "s2", "Beta Radio", "Swing"), hit);

        radio.SetSearch("alpha");
        Assert.True(radio.Select("s3"));
        Assert.Equal("s3", radio.CurrentKey);
        Assert.Equal(3, radio.View.Visible.Count);
        Assert.False(radio.Select("nope"));
    }

    static FakeRecordReader Feed()
    {
        var records = new FakeRecordReader();
        records.Add("0001:Q.esm", "QUST").Aliases.AddRange(new[]
        {
            new AliasDisplay("0010:Q.esm", "0020:Q.esm"),
            new AliasDisplay("0011:Q.esm", "0021:Q.esm"),
            new AliasDisplay(null, "0022:Q.esm"),
            new AliasDisplay("0012:Q.esm", "0023:Q.esm"),
        });
        records.Add("0002:Q.esm", "QUST").Aliases.AddRange(new[]
        {
            new AliasDisplay("0010:Q.esm", "0024:Q.esm"),
            new AliasDisplay("0013:Q.esm", "0021:Q.esm"),
        });
        records.Add("0010:Q.esm", "REFR").Scalars[RadioStations.FrequencyPath] = "98";
        records.Add("0011:Q.esm", "REFR").Scalars[RadioStations.FrequencyPath] = "91.5";
        records.Add("0012:Q.esm", "REFR");
        records.Add("0013:Q.esm", "REFR").Scalars[RadioStations.FrequencyPath] = "86.1";
        records.Add("0020:Q.esm", "MESG", name: "Diamond City Radio");
        records.Add("0021:Q.esm", "MESG", name: "Distress Signal");
        records.Add("0022:Q.esm", "MESG", name: "No Reference");
        records.Add("0023:Q.esm", "MESG", name: "Dad");
        records.Add("0024:Q.esm", "MESG", name: "Second Name");
        return records;
    }

    // Constraint: detects a feed that lists every display-named alias (people, intercoms) instead of only those forcing a transmitter reference.
    [Fact]
    public void Stations_are_the_aliases_that_force_a_transmitter_with_a_frequency_and_a_named_message()
    {
        var stations = RadioStations.Build(Feed(), new[] { "0001:Q.esm" });

        Assert.Equal(new[] { "Diamond City Radio", "Distress Signal" }, stations.Select(s => s.Name));
        Assert.Equal(new[] { 98.0, 91.5 }, stations.Select(s => s.Frequency!.Value));
        Assert.Equal(new[] { "0010:Q.esm", "0011:Q.esm" }, stations.Select(s => s.Id));
    }

    // Constraint: detects a feed that lets a later quest rename a transmitter or collapses stations that share a display name.
    [Fact]
    public void The_first_quest_naming_a_transmitter_wins_and_equal_names_stay_separate_rows()
    {
        var stations = RadioStations.Build(Feed(), new[] { "0001:Q.esm", "0002:Q.esm" });

        Assert.Equal(new[] { "0010:Q.esm", "0011:Q.esm", "0013:Q.esm" }, stations.Select(s => s.Id));
        Assert.Equal("Diamond City Radio", stations[0].Name);
        Assert.Equal(2, stations.Count(s => s.Name == "Distress Signal"));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("not a number")]
    [InlineData("")]
    [InlineData(null)]
    public void A_transmitter_without_a_readable_frequency_is_not_listed(string? frequency)
    {
        var records = Feed();
        records.Records["0010:Q.esm"].Scalars.Remove(RadioStations.FrequencyPath);
        if (frequency is not null) records.Records["0010:Q.esm"].Scalars[RadioStations.FrequencyPath] = frequency;

        Assert.DoesNotContain(RadioStations.Build(records, new[] { "0001:Q.esm" }), s => s.Id == "0010:Q.esm");
    }

    [Fact]
    public void A_quest_that_is_not_in_the_data_or_a_message_without_text_gives_no_station()
    {
        var records = Feed();
        records.Add("0003:Q.esm", "QUST").Aliases.Add(new AliasDisplay("0014:Q.esm", "0025:Q.esm"));
        records.Add("0014:Q.esm", "REFR").Scalars[RadioStations.FrequencyPath] = "90";
        records.Add("0025:Q.esm", "MESG");

        Assert.Empty(RadioStations.Build(records, new[] { "9999:Q.esm" }));
        Assert.Empty(RadioStations.Build(records, new[] { "0003:Q.esm" }));
    }

    [Fact]
    public void A_built_station_shows_its_name_and_frequency_in_the_now_playing_lines()
    {
        var radio = new RadioModel(RadioStations.Build(Feed(), new[] { "0001:Q.esm" }));

        radio.Play("0010:Q.esm");

        Assert.Equal(new[] { "NOW PLAYING", "Diamond City Radio", "FREQ 98.0" }, radio.NowPlayingLines);
    }
}
