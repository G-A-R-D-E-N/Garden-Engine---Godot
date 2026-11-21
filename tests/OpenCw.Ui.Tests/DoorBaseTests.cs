using System.Linq;
using OpenCommonwealth.Services.Interaction;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class DoorBaseTests
{
    private static UiText Text(string? text) => new(default, text, 0);

    private static DoorRecord Door(string? open, string? close, params FormKey[] random) =>
        new(new FormKey("Fallout4.esm", 0x100), Text(null), 1, null, null, null, Text(open), Text(close), random);

    [Fact]
    public void Alternate_open_and_close_text_come_from_ONAM_and_CNAM_with_padding_trimmed()
    {
        var door = DoorBase.From(Door("  Pull\0", "Push"));
        Assert.Equal("Pull", door.OpenText);
        Assert.Equal("Push", door.CloseText);
    }

    [Fact]
    public void Missing_or_blank_text_is_null_so_the_default_verb_stays()
    {
        var door = DoorBase.From(Door(null, " \0"));
        Assert.Null(door.OpenText);
        Assert.Null(door.CloseText);
    }

    [Fact]
    public void Random_destinations_keep_the_stored_order_as_record_keys()
    {
        var door = DoorBase.From(Door(null, null, new FormKey("Fallout4.esm", 0x2A), new FormKey("Mod.esp", 0x01000005)));
        Assert.Equal(new[] { "00002A:Fallout4.esm", "1000005:Mod.esp" }, door.RandomDestinations.ToArray());
        Assert.Empty(DoorBase.From(Door(null, null)).RandomDestinations);
    }
}
