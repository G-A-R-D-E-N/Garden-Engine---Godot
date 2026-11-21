using System;
using OpenCommonwealth.Services.Ui.MainMenu;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SaveSlotTextTests
{
    static SaveSlotInfo Slot(string name = "n", string character = "c", int level = 1, TimeSpan? playtime = null,
                             string location = "", SaveKind kind = SaveKind.Manual) =>
        new("id", name, character, level, playtime ?? TimeSpan.Zero, location, DateTimeOffset.MinValue, "", kind);

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(59.999, "0m")]
    [InlineData(60, "1m")]
    [InlineData(300, "5m")]
    [InlineData(3599, "59m")]
    [InlineData(3600, "1h 00m")]
    [InlineData(3660, "1h 01m")]
    [InlineData(86399, "23h 59m")]
    [InlineData(86400, "1d 0h 00m")]
    [InlineData(90000, "1d 1h 00m")]
    [InlineData(907800, "10d 12h 10m")]
    public void Playtime_floors_to_whole_minutes_and_pads_minutes_only_after_hours(double seconds, string expected)
    {
        Assert.Equal(expected, SaveSlotText.Playtime(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Playtime_never_goes_negative()
    {
        Assert.Equal("0m", SaveSlotText.Playtime(TimeSpan.FromSeconds(-30)));
        Assert.Equal("0m", SaveSlotText.Playtime(TimeSpan.MinValue));
    }

    [Fact]
    public void A_timestamp_is_shown_in_its_own_offset_with_zero_padded_fields()
    {
        Assert.Equal("2026-01-02 03:04", SaveSlotText.Timestamp(new DateTimeOffset(2026, 1, 2, 3, 4, 59, TimeSpan.Zero)));
        Assert.Equal("2026-12-31 23:59", SaveSlotText.Timestamp(new DateTimeOffset(2026, 12, 31, 23, 59, 0, TimeSpan.FromHours(5))));
        Assert.Equal("Unknown date", SaveSlotText.Timestamp(DateTimeOffset.MinValue));
    }

    [Theory]
    [InlineData("abcd", 4, "abcd")]
    [InlineData("abcde", 4, "abc…")]
    [InlineData("abc", 2, "a…")]
    [InlineData("ab", 2, "ab")]
    [InlineData("", 5, "")]
    public void Clip_keeps_text_that_fits_and_ends_longer_text_with_an_ellipsis_inside_the_limit(string text, int max, string expected)
    {
        Assert.Equal(expected, SaveSlotText.Clip(text, max));
    }

    [Fact]
    public void Location_clips_at_thirty_and_character_names_at_twenty_characters()
    {
        Assert.Equal(new string('L', 30), SaveSlotText.Location(Slot(location: new string('L', 30))));
        Assert.Equal(new string('L', 29) + "…", SaveSlotText.Location(Slot(location: new string('L', 31))));
        Assert.Equal(new string('N', 20), SaveSlotText.CharacterName(Slot(character: new string('N', 20))));
        Assert.Equal(new string('N', 19) + "…", SaveSlotText.CharacterName(Slot(character: new string('N', 21))));
        Assert.Equal("", SaveSlotText.CharacterName(Slot(character: "")));
    }

    [Fact]
    public void The_summary_lists_level_then_the_location_when_there_is_one_then_playtime()
    {
        Assert.Equal("Level 0, 0m", SaveSlotText.Summary(Slot(level: 0)));
        Assert.Equal("Level 3, Town, 1d 2h 03m", SaveSlotText.Summary(Slot(level: 3, playtime: TimeSpan.FromMinutes(1563), location: "Town")));
        Assert.Equal("Level 1234567, 5m", SaveSlotText.Summary(Slot(level: 1234567, playtime: TimeSpan.FromMinutes(5))));
    }

    [Theory]
    [InlineData(SaveKind.Manual, "Bridge")]
    [InlineData(SaveKind.Quick, "Quick: Bridge")]
    [InlineData(SaveKind.Auto, "Auto: Bridge")]
    [InlineData((SaveKind)9, "Bridge")]
    public void The_row_text_prefixes_quick_and_auto_saves_only(SaveKind kind, string expected)
    {
        Assert.Equal(expected, SaveSlotText.RowText(Slot(name: "Bridge", kind: kind)));
    }

    [Fact]
    public void The_row_detail_is_the_level_without_grouping_separators()
    {
        Assert.Equal("Lv 0", SaveSlotText.RowDetail(Slot(level: 0)));
        Assert.Equal("Lv 12", SaveSlotText.RowDetail(Slot(level: 12)));
        Assert.Equal("Lv 1234567", SaveSlotText.RowDetail(Slot(level: 1234567)));
    }
}
