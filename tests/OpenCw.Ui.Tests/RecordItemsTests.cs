using System;
using Radicle.Interop;
using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class RecordItemsTests
{
    [Fact]
    public void KeyRoundTripsAndRejectsBadText()
    {
        Assert.Equal("00000F:Fallout4.esm", RecordKey.Format("Fallout4.esm", 0xF));
        Assert.True(RecordKey.TryParse("00000F:Fallout4.esm", out var plugin, out var local));
        Assert.Equal(("Fallout4.esm", 0xFu), (plugin, local));
        Assert.True(RecordKey.TryParse("A:My:Mod.esp", out plugin, out _));
        Assert.Equal("My:Mod.esp", plugin);
        Assert.False(RecordKey.TryParse("Strength", out _, out _));
        Assert.False(RecordKey.TryParse("ZZ:Mod.esp", out _, out _));
        Assert.False(RecordKey.TryParse("0F:", out _, out _));
        Assert.False(RecordKey.TryParse(null, out _, out _));
    }

    [Fact]
    public void ReadsNameValueAndWeight()
    {
        var reader = new FakeRecordReader();
        var item = reader.Add("000001:a.esm", "ALCH", "Stimpak", "Stimpak");
        item.Scalars["ALCH\\Value"] = "48";
        item.Scalars["ALCH\\Weight"] = "0.5";

        var found = new RecordItems(reader).Find("000001:a.esm");

        Assert.Equal(new ItemRecord("000001:a.esm", "ALCH", "Stimpak", 48, 0.5), found);
    }

    [Fact]
    public void MissingScalarsStayUnknownAndNameFallsBackToEditorId()
    {
        var reader = new FakeRecordReader();
        reader.Add("000002:a.esm", "MISC", "Junk_Item");

        var found = new RecordItems(reader).Find("000002:a.esm");

        Assert.NotNull(found);
        Assert.Equal("Junk_Item", found!.Name);
        Assert.Null(found.Value);
        Assert.Null(found.Weight);
    }

    [Fact]
    public void IgnoresNonItemAndUnknownRecordsAndGarbageNumbers()
    {
        var reader = new FakeRecordReader();
        reader.Add("000003:a.esm", "NPC_", "Someone");
        reader.Add("000004:a.esm", "WEAP", "Gun").Scalars["WEAP\\Value"] = "lots";
        var items = new RecordItems(reader);

        Assert.Null(items.Find("000003:a.esm"));
        Assert.Null(items.Find("000009:a.esm"));
        Assert.Null(items.Find("000004:a.esm")!.Value);
    }

    [Fact]
    public void CachesEachLookup()
    {
        var reader = new FakeRecordReader();
        reader.Add("000005:a.esm", "AMMO", "Round");
        var items = new RecordItems(reader);

        items.Find("000005:a.esm");
        items.Find("000005:a.esm");
        items.Find("000008:a.esm");
        items.Find("000008:a.esm");

        Assert.Equal(2, reader.SignatureCalls);
    }

    [Fact]
    public void MiscValueAndWeightComeFromTheScalarRows()
    {
        // Constraint: detects MISC value and weight read through the typed Misc decoder instead of the MISC\Value and MISC\Weight rows, which leaves a scalar-only record unknown.
        var reader = new FakeRecordReader();
        var item = reader.Add("000001:a.esm", "MISC", "Cup", "Cup");
        item.Scalars["MISC\\Value"] = "12";
        item.Scalars["MISC\\Weight"] = "1.5";

        var found = new RecordItems(reader).Find("000001:a.esm");

        Assert.Equal((12, 1.5), (found!.Value, found.Weight));
    }

    [Fact]
    public void MiscBadWeightStaysUnknown()
    {
        // Constraint: detects the NaN weight text being stored as a number, or a bad weight dropping the valid value row beside it.
        var reader = new FakeRecordReader();
        var item = reader.Add("000001:a.esm", "MISC", "Typed");
        item.Scalars["MISC\\Value"] = "7";
        item.Scalars["MISC\\Weight"] = "NaN";

        var found = new RecordItems(reader).Find("000001:a.esm");

        Assert.Equal((7, null), (found!.Value, found.Weight));
    }

    [Fact]
    public void NoteValueAndWeightComeFromTheScalarRows()
    {
        // Constraint: detects NOTE value and weight read through the typed Note decoder instead of the NOTE\Value and NOTE\Weight rows.
        var reader = new FakeRecordReader();
        var item = reader.Add("000001:a.esm", "NOTE", "Tape");
        item.Scalars["NOTE\\Value"] = "25";
        item.Scalars["NOTE\\Weight"] = "0.25";

        var found = new RecordItems(reader).Find("000001:a.esm");

        Assert.Equal((25, 0.25), (found!.Value, found.Weight));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("2147483648", int.MaxValue)]
    [InlineData("4294967295", int.MaxValue)]
    [InlineData("18446744073709551615", int.MaxValue)]
    [InlineData("abc", null)]
    public void NoteValueAboveIntMaxValueIsClampedNotUnknown(string text, int? expected)
    {
        // Constraint: detects a NOTE value parsed as a signed int, which turns an unsigned value above int.MaxValue into unknown instead of clamping it to int.MaxValue.
        var reader = new FakeRecordReader();
        reader.Add("000001:a.esm", "NOTE", "Tape").Scalars["NOTE\\Value"] = text;

        Assert.Equal(expected, new RecordItems(reader).Find("000001:a.esm")!.Value);
    }
}
