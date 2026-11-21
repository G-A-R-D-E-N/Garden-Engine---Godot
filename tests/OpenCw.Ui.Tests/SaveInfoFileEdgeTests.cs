using System;
using System.Linq;
using System.Text.Json;
using OpenCommonwealth.Services.Ui.MainMenu;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SaveInfoFileEdgeTests
{
    static SaveSlotInfo Sample(string thumbnail = "user://saves/slot1.png") => new("slot1", "Before the bridge", "Ann", 17,
        TimeSpan.FromSeconds(7384.5), "Town A", new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2)), thumbnail, SaveKind.Quick);

    static SaveSlotInfo Parse(string json, string id = "s", string directory = "dir") => SaveInfoFile.TryParse(id, json, directory)!;

    [Fact]
    public void A_serialized_sidecar_has_exactly_the_documented_fields_in_order()
    {
        using var doc = JsonDocument.Parse(SaveInfoFile.Serialize(Sample()));
        var root = doc.RootElement;

        Assert.Equal(
            new[] { "version", "name", "character", "level", "playtimeSeconds", "location", "timestamp", "thumbnail", "kind" },
            root.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("Before the bridge", root.GetProperty("name").GetString());
        Assert.Equal("Ann", root.GetProperty("character").GetString());
        Assert.Equal(17, root.GetProperty("level").GetInt32());
        Assert.Equal(7384.5, root.GetProperty("playtimeSeconds").GetDouble());
        Assert.Equal("Town A", root.GetProperty("location").GetString());
        Assert.Equal("2026-03-04T05:06:07.0000000+02:00", root.GetProperty("timestamp").GetString());
        Assert.Equal("slot1.png", root.GetProperty("thumbnail").GetString());
        Assert.Equal("quick", root.GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData("C:\\saves\\slot1.png", "slot1.png")]
    [InlineData("user://saves/slot1.png", "slot1.png")]
    [InlineData("slot1.png", "slot1.png")]
    [InlineData("", "")]
    [InlineData("user://saves/other.png", "")]
    [InlineData("user://saves/", "")]
    public void Serializing_stores_only_the_leaf_of_a_thumbnail_that_belongs_to_the_slot(string path, string expected)
    {
        using var doc = JsonDocument.Parse(SaveInfoFile.Serialize(Sample(path)));

        Assert.Equal(expected, doc.RootElement.GetProperty("thumbnail").GetString());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(".", "")]
    [InlineData("..", "")]
    [InlineData("...", "")]
    [InlineData("dir/", "")]
    [InlineData("dir\\", "")]
    [InlineData("a/..", "")]
    [InlineData("x.png", "x.png")]
    [InlineData("a/b\\c/x.png", "x.png")]
    [InlineData("dir/.hidden", ".hidden")]
    public void The_leaf_name_is_what_follows_the_last_separator_unless_it_is_only_dots(string path, string expected)
    {
        Assert.Equal(expected, SaveInfoFile.LeafName(path));
    }

    [Theory]
    [InlineData("slot", "slot.png", true)]
    [InlineData("slot", "slot.PNG", true)]
    [InlineData("slot", "slot.png.png", true)]
    [InlineData("slot", "slot.a.png", true)]
    [InlineData("slot", "slot1.png", false)]
    [InlineData("slot", "SLOT.png", false)]
    [InlineData("slot", "slot.pn", false)]
    [InlineData("slot", "slot.", false)]
    [InlineData("slot", "slot.a/b.png", false)]
    [InlineData("slot", "slot.a\\b.png", false)]
    [InlineData("slot", "slot.a:b.png", false)]
    [InlineData("slot", "slot..png", false)]
    [InlineData("slot", "slot.a..b.png", false)]
    [InlineData("slot", "", false)]
    public void A_thumbnail_name_must_start_with_the_slot_id_and_a_dot_end_in_png_and_stay_inside_the_directory(
        string id, string name, bool expected)
    {
        Assert.Equal(expected, SaveInfoFile.IsOwnThumbnail(id, name));
    }

    [Theory]
    [InlineData("quick_1", true)]
    [InlineData("-", true)]
    [InlineData("é_1", true)]
    [InlineData("a:b", false)]
    [InlineData("a.b", false)]
    [InlineData("\t", false)]
    [InlineData("", false)]
    public void Slot_ids_may_not_contain_separators_dots_or_colons_or_be_blank(string id, bool expected)
    {
        Assert.Equal(expected, SaveInfoFile.IsValidId(id));
    }

    [Fact]
    public void An_empty_object_parses_to_the_defaults_with_the_id_as_the_name()
    {
        var parsed = Parse("{}", "slot9");

        Assert.Equal(new SaveSlotInfo("slot9", "slot9", "", 0, TimeSpan.Zero, "", DateTimeOffset.MinValue, "", SaveKind.Manual), parsed);
    }

    [Theory]
    [InlineData("{\"level\":3.7}", 0)]
    [InlineData("{\"level\":3000000000}", 0)]
    [InlineData("{\"level\":\"7\"}", 0)]
    [InlineData("{\"level\":null}", 0)]
    [InlineData("{\"level\":0}", 0)]
    [InlineData("{\"level\":42}", 42)]
    [InlineData("{\"level\":2147483647}", int.MaxValue)]
    public void The_level_must_be_a_whole_number_that_fits_an_int(string json, int expected)
    {
        Assert.Equal(expected, Parse(json).Level);
    }

    [Theory]
    [InlineData("{\"playtimeSeconds\":59.5}", 59.5)]
    [InlineData("{\"playtimeSeconds\":0}", 0.0)]
    [InlineData("{\"playtimeSeconds\":-0.5}", 0.0)]
    [InlineData("{\"playtimeSeconds\":1000000000}", 1e9)]
    [InlineData("{\"playtimeSeconds\":2000000000}", 1e9)]
    [InlineData("{\"playtimeSeconds\":\"12\"}", 0.0)]
    [InlineData("{\"playtimeSeconds\":true}", 0.0)]
    public void Playtime_seconds_are_clamped_to_zero_through_a_billion_and_must_be_a_number(string json, double expected)
    {
        Assert.Equal(TimeSpan.FromSeconds(expected), Parse(json).Playtime);
    }

    [Theory]
    [InlineData("quick", SaveKind.Quick)]
    [InlineData("QUICK", SaveKind.Quick)]
    [InlineData("Auto", SaveKind.Auto)]
    [InlineData("manual", SaveKind.Manual)]
    [InlineData("7", SaveKind.Manual)]
    [InlineData("-1", SaveKind.Manual)]
    [InlineData("", SaveKind.Manual)]
    [InlineData("autosave", SaveKind.Manual)]
    public void The_kind_is_matched_ignoring_case_and_anything_undefined_is_manual(string kind, SaveKind expected)
    {
        Assert.Equal(expected, Parse("{\"kind\":\"" + kind + "\"}").Kind);
        Assert.Equal(SaveKind.Manual, Parse("{\"kind\":2}").Kind);
    }

    [Fact]
    public void A_timestamp_keeps_its_offset_and_a_date_without_one_parses_to_that_day_and_junk_is_unknown()
    {
        var withOffset = Parse("{\"timestamp\":\"2026-03-04T05:06:07+02:00\"}");
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2)), withOffset.Timestamp);
        Assert.Equal(TimeSpan.FromHours(2), withOffset.Timestamp.Offset);

        var dateOnly = Parse("{\"timestamp\":\"2026-03-04\"}").Timestamp;
        Assert.Equal((2026, 3, 4), (dateOnly.Year, dateOnly.Month, dateOnly.Day));

        Assert.Equal(DateTimeOffset.MinValue, Parse("{\"timestamp\":\"not a date\"}").Timestamp);
        Assert.Equal(DateTimeOffset.MinValue, Parse("{\"timestamp\":20260304}").Timestamp);
        Assert.Equal(DateTimeOffset.MinValue, Parse("{\"timestamp\":null}").Timestamp);
    }

    [Theory]
    [InlineData("dir", "dir/s.png")]
    [InlineData("dir/", "dir/s.png")]
    [InlineData("dir\\", "dir/s.png")]
    [InlineData("a/b///", "a/b/s.png")]
    [InlineData("user://saves/", "user://saves/s.png")]
    public void The_thumbnail_is_joined_to_the_directory_with_one_forward_slash(string directory, string expected)
    {
        Assert.Equal(expected, Parse("{\"thumbnail\":\"s.png\"}", "s", directory).ThumbnailPath);
    }

    [Fact]
    public void A_non_string_thumbnail_or_text_field_falls_back_to_its_default()
    {
        var parsed = Parse("{\"thumbnail\":5,\"character\":false,\"location\":[1],\"name\":null}", "slot2");

        Assert.Equal("", parsed.ThumbnailPath);
        Assert.Equal("", parsed.Character);
        Assert.Equal("", parsed.Location);
        Assert.Equal("slot2", parsed.Name);
    }

    [Fact]
    public void Unknown_properties_are_ignored_and_literals_empty_text_and_very_deep_json_are_rejected()
    {
        Assert.Equal("Keep", Parse("{\"extra\":{\"a\":[1,2,3]},\"name\":\"Keep\"}").Name);
        Assert.Null(SaveInfoFile.TryParse("s", "null", "dir"));
        Assert.Null(SaveInfoFile.TryParse("s", "5", "dir"));
        Assert.Null(SaveInfoFile.TryParse("s", "\"text\"", "dir"));
        Assert.Null(SaveInfoFile.TryParse("s", "", "dir"));
        Assert.Null(SaveInfoFile.TryParse("s", "{\"name\":\"x\",}", "dir"));
        Assert.Null(SaveInfoFile.TryParse("s", string.Concat(Enumerable.Repeat("{\"a\":", 100)) + "1" + new string('}', 100), "dir"));
    }
}
