using System;
using System.Collections.Generic;
using System.Text;
using OpenCommonwealth.Services.Ui.MainMenu;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MainMenuSavedLocationTests
{
    private static IReadOnlyDictionary<string, byte[]> Json(string json) =>
        new Dictionary<string, byte[]> { [SavedLocationSave.SectionName] = Encoding.UTF8.GetBytes(json) };

    [Fact]
    public void A_location_survives_a_round_trip()
    {
        var location = new SavedLocation("res://scenes/world_play.tscn", "000A1B:Fallout4.esm", 1.5, -2.25, 300, 0.75);
        Assert.Equal(location, SavedLocationSave.Read(SavedLocationSave.Sections(location)));
    }

    [Fact]
    public void An_exterior_location_has_an_empty_cell()
    {
        var location = new SavedLocation("res://scenes/world_play.tscn", "", 0, 0, 0, 0);
        Assert.Equal("", SavedLocationSave.Read(SavedLocationSave.Sections(location))!.Cell);
    }

    [Fact]
    public void A_save_without_the_section_has_no_location_and_a_null_location_writes_nothing()
    {
        Assert.Null(SavedLocationSave.Read(new Dictionary<string, byte[]>()));
        Assert.Empty(SavedLocationSave.Sections(null));
    }

    [Fact]
    public void Writing_a_location_that_is_not_finite_or_has_no_scene_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SavedLocationSave.Sections(new SavedLocation("res://a.tscn", "", double.NaN, 0, 0, 0)));
        Assert.Throws<ArgumentException>(() => SavedLocationSave.Sections(new SavedLocation("", "", 0, 0, 0, 0)));
        Assert.Throws<ArgumentException>(() => SavedLocationSave.Sections(new SavedLocation("res://a.tscn", "", 1e300, 0, 0, 0)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"version\":2,\"scene\":\"a\",\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}")]
    [InlineData("{\"version\":1,\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}")]
    [InlineData("{\"version\":1,\"scene\":\"\",\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}")]
    [InlineData("{\"version\":1,\"scene\":\"a\",\"x\":\"far\",\"y\":0,\"z\":0,\"yaw\":0}")]
    [InlineData("{\"version\":1,\"scene\":\"a\",\"y\":0,\"z\":0,\"yaw\":0}")]
    [InlineData("{\"version\":1,\"scene\":\"a\",\"x\":1e300,\"y\":0,\"z\":0,\"yaw\":0}")]
    public void A_malformed_section_is_refused_as_a_format_error(string json) =>
        Assert.Throws<FormatException>(() => SavedLocationSave.Read(Json(json)));

    [Fact]
    public void A_new_manual_save_gets_a_time_stamped_id_that_is_never_taken()
    {
        var now = new DateTimeOffset(2026, 11, 21, 10, 15, 30, TimeSpan.Zero);
        Assert.Equal("save-20261121-101530", SaveSlotNames.NewManual([], now));
        Assert.Equal("save-20261121-101530-2", SaveSlotNames.NewManual(["save-20261121-101530", "quicksave"], now));
        Assert.Equal("save-20261121-101530-3", SaveSlotNames.NewManual(["SAVE-20261121-101530", "save-20261121-101530-2"], now));
    }
}
