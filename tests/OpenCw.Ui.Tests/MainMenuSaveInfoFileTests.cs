using System;
using OpenCommonwealth.Services.Ui.MainMenu;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MainMenuSaveInfoFileTests
{
    static SaveSlotInfo Sample() => new("slot1", "Before the bridge", "Ann", 17, TimeSpan.FromSeconds(7384.5),
        "Town A", new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2)), "user://saves/slot1.png", SaveKind.Quick);

    [Fact]
    public void A_sidecar_round_trips_and_the_thumbnail_is_resolved_against_the_save_directory()
    {
        var json = SaveInfoFile.Serialize(Sample());

        var parsed = SaveInfoFile.TryParse("slot1", json, "user://saves/");

        Assert.NotNull(parsed);
        Assert.Equal("Before the bridge", parsed!.Name);
        Assert.Equal("Ann", parsed.Character);
        Assert.Equal(17, parsed.Level);
        Assert.Equal(TimeSpan.FromSeconds(7384.5), parsed.Playtime);
        Assert.Equal("Town A", parsed.Location);
        Assert.Equal(Sample().Timestamp, parsed.Timestamp);
        Assert.Equal(Sample().Timestamp.Offset, parsed.Timestamp.Offset);
        Assert.Equal("user://saves/slot1.png", parsed.ThumbnailPath);
        Assert.Equal(SaveKind.Quick, parsed.Kind);
    }

    [Fact]
    public void Missing_or_wrongly_typed_fields_fall_back_to_safe_defaults()
    {
        var parsed = SaveInfoFile.TryParse("slot2", "{\"name\":5,\"level\":\"high\",\"playtimeSeconds\":-30,\"timestamp\":\"yesterday\",\"kind\":\"weird\"}", "dir");

        Assert.NotNull(parsed);
        Assert.Equal("slot2", parsed!.Name);
        Assert.Equal(0, parsed.Level);
        Assert.Equal(TimeSpan.Zero, parsed.Playtime);
        Assert.Equal(DateTimeOffset.MinValue, parsed.Timestamp);
        Assert.Equal("", parsed.ThumbnailPath);
        Assert.Equal(SaveKind.Manual, parsed.Kind);
        Assert.Equal("", parsed.Character);
    }

    [Fact]
    public void Negative_levels_and_absurd_playtimes_are_clamped()
    {
        var parsed = SaveInfoFile.TryParse("s", "{\"level\":-4,\"playtimeSeconds\":1e300}", "dir");

        Assert.Equal(0, parsed!.Level);
        Assert.Equal(TimeSpan.FromSeconds(1e9), parsed.Playtime);
    }

    [Fact]
    public void Invalid_json_non_objects_and_unsafe_ids_are_rejected()
    {
        Assert.Null(SaveInfoFile.TryParse("slot", "{ nope", "dir"));
        Assert.Null(SaveInfoFile.TryParse("slot", "[1,2]", "dir"));
        Assert.Null(SaveInfoFile.TryParse("../escape", "{}", "dir"));
        Assert.Null(SaveInfoFile.TryParse("a.b", "{}", "dir"));
        Assert.Null(SaveInfoFile.TryParse("", "{}", "dir"));
        Assert.True(SaveInfoFile.IsValidId("quick_1"));
        Assert.False(SaveInfoFile.IsValidId("a/b"));
        Assert.False(SaveInfoFile.IsValidId(".."));
        Assert.False(SaveInfoFile.IsValidId("a..b"));
        Assert.False(SaveInfoFile.IsValidId("a\\b"));
        Assert.False(SaveInfoFile.IsValidId("  "));
    }

    static string Thumb(string id, string thumbnail) =>
        SaveInfoFile.TryParse(id, "{\"thumbnail\":\"" + thumbnail.Replace("\\", "\\\\") + "\"}", "user://saves")!.ThumbnailPath;

    [Fact]
    public void A_thumbnail_is_accepted_only_as_a_png_named_after_its_own_slot_with_no_directory_part()
    {
        Assert.Equal("user://saves/s.png", Thumb("s", "s.png"));
        Assert.Equal("user://saves/s.shot.PNG", Thumb("s", "s.shot.PNG"));
        Assert.Equal("", Thumb("s", "../../secret/pic.png"));
        Assert.Equal("", Thumb("s", "sub/s.png"));
        Assert.Equal("", Thumb("s", "sub\\s.png"));
        Assert.Equal("", Thumb("A", "B.sav"));
        Assert.Equal("", Thumb("A", "B.png"));
        Assert.Equal("", Thumb("A", "A.sav"));
        Assert.Equal("", Thumb("A", "A.info.json"));
        Assert.Equal("", Thumb("s", ".."));
        Assert.Equal("", Thumb("s", "s..png"));
        Assert.Equal("", Thumb("s", ".png"));
    }

    [Fact]
    public void Serializing_drops_a_thumbnail_that_does_not_belong_to_the_slot()
    {
        var foreign = Sample() with { ThumbnailPath = "user://saves/other.png" };

        var parsed = SaveInfoFile.TryParse("slot1", SaveInfoFile.Serialize(foreign), "user://saves");

        Assert.Equal("", parsed!.ThumbnailPath);
        Assert.Equal("pic.png", SaveInfoFile.LeafName("a\\b\\pic.png"));
        Assert.Equal("s.info.json", SaveInfoFile.FileNameFor("s"));
    }
}
