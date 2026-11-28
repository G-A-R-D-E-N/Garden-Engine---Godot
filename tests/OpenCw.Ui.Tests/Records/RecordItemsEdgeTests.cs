using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class RecordItemsEdgeTests
{
    const string Id = "000001:a.esm";

    static ItemRecord Scalars(string? value, string? weight, string signature = "WEAP")
    {
        var reader = new FakeRecordReader();
        var record = reader.Add(Id, signature, "Gun");
        if (value is not null) record.Scalars[signature + "\\Value"] = value;
        if (weight is not null) record.Scalars[signature + "\\Weight"] = weight;
        return new RecordItems(reader).Find(Id)!;
    }

    [Fact]
    public void The_item_signature_set_is_exactly_the_eleven_known_ones_and_is_case_sensitive()
    {
        Assert.Equal(11, RecordItems.ItemSignatures.Count);
        Assert.Equal(
            new[] { "ALCH", "AMMO", "ARMO", "BOOK", "CMPO", "INGR", "KEYM", "LIGH", "MISC", "NOTE", "WEAP" },
            RecordItems.ItemSignatures.OrderBy(s => s, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("weap", RecordItems.ItemSignatures);
        Assert.DoesNotContain("COBJ", RecordItems.ItemSignatures);
    }

    [Theory]
    [InlineData("ARMO")]
    [InlineData("WEAP")]
    [InlineData("AMMO")]
    [InlineData("ALCH")]
    [InlineData("BOOK")]
    [InlineData("INGR")]
    [InlineData("KEYM")]
    [InlineData("LIGH")]
    [InlineData("NOTE")]
    [InlineData("MISC")]
    [InlineData("CMPO")]
    public void Every_item_signature_reads_its_value_and_weight_scalars_when_there_is_no_typed_record(string signature)
    {
        Assert.Equal(new ItemRecord(Id, signature, "Gun", 5, 2.5), Scalars("5", "2.5", signature));
    }

    [Theory]
    [InlineData("npc_")]
    [InlineData("weap")]
    [InlineData("COBJ")]
    [InlineData("")]
    public void A_signature_outside_the_set_is_not_an_item_and_is_remembered_as_such(string signature)
    {
        var reader = new FakeRecordReader();
        reader.Add(Id, signature, "Thing");
        var items = new RecordItems(reader);

        Assert.Null(items.Find(Id));
        Assert.Null(items.Find(Id));
        Assert.Equal(1, reader.SignatureCalls);
    }

    [Fact]
    public void A_differently_cased_id_hits_the_cache_and_returns_the_first_spelling()
    {
        var reader = new FakeRecordReader();
        reader.Add("00000A:a.esm", "AMMO", "Round");
        var items = new RecordItems(reader);

        var first = items.Find("00000A:a.esm");
        var second = items.Find("00000a:A.ESM");

        Assert.Same(first, second);
        Assert.Equal("00000A:a.esm", second!.Id);
        Assert.Equal(1, reader.SignatureCalls);
    }

    [Theory]
    [InlineData("48", 48)]
    [InlineData(" 5 ", 5)]
    [InlineData("-5", -5)]
    [InlineData("+7", 7)]
    [InlineData("0", 0)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    [InlineData("2147483648", null)]
    [InlineData("1.5", null)]
    [InlineData("1,000", null)]
    [InlineData("0x10", null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    public void The_value_scalar_is_a_whole_invariant_number_or_unknown(string text, int? expected)
    {
        Assert.Equal(expected, Scalars(text, null).Value);
    }

    [Theory]
    [InlineData("0.5", 0.5)]
    [InlineData(" 2.25 ", 2.25)]
    [InlineData("-1.5", -1.5)]
    [InlineData("1e2", 100.0)]
    [InlineData("3", 3.0)]
    [InlineData("NaN", null)]
    [InlineData("Infinity", null)]
    [InlineData("-Infinity", null)]
    [InlineData("1e999", null)]
    [InlineData("1,5", null)]
    [InlineData("heavy", null)]
    [InlineData("", null)]
    public void The_weight_scalar_is_a_finite_invariant_number_or_unknown(string text, double? expected)
    {
        Assert.Equal(expected, Scalars(null, text).Weight);
    }

    [Fact]
    public void A_scalar_stored_under_another_signature_is_not_read()
    {
        var reader = new FakeRecordReader();
        var record = reader.Add(Id, "WEAP", "Gun");
        record.Scalars["ARMO\\Value"] = "9";
        record.Scalars["Value"] = "9";

        var found = new RecordItems(reader).Find(Id)!;

        Assert.Equal((null, null), (found.Value, found.Weight));
    }

    [Fact]
    public void The_name_prefers_the_display_name_then_the_editor_id_then_the_id_itself()
    {
        var reader = new FakeRecordReader();
        reader.Add("000001:a.esm", "AMMO", "EditorName", "Display Name");
        reader.Add("000002:a.esm", "AMMO", "EditorName");
        reader.Add("000003:a.esm", "AMMO");
        var items = new RecordItems(reader);

        Assert.Equal("Display Name", items.Find("000001:a.esm")!.Name);
        Assert.Equal("EditorName", items.Find("000002:a.esm")!.Name);
        Assert.Equal("000003:a.esm", items.Find("000003:a.esm")!.Name);
    }

    [Fact]
    public void A_misc_record_reads_its_value_and_weight_from_the_misc_rows()
    {
        var reader = new FakeRecordReader();
        var record = reader.Add(Id, "MISC", "Cup");
        record.Scalars["MISC\\Value"] = "12";
        record.Scalars["MISC\\Weight"] = "0.25";

        var found = new RecordItems(reader).Find(Id)!;

        Assert.Equal((12, 0.25), (found.Value, found.Weight));
    }

    [Fact]
    public void A_note_reads_its_value_and_weight_from_the_note_rows()
    {
        Assert.Equal((7, 1.25), (Scalars("7", "1.25", "NOTE").Value, Scalars("7", "1.25", "NOTE").Weight));
    }

    [Fact]
    public void The_rows_of_other_signatures_are_ignored()
    {
        // Constraint: detects an item reading the MISC or NOTE value and weight rows for a record of another signature instead of its own rows.
        var reader = new FakeRecordReader();
        var record = reader.Add(Id, "AMMO", "Round");
        record.Scalars["MISC\\Value"] = "100";
        record.Scalars["MISC\\Weight"] = "100";
        record.Scalars["NOTE\\Value"] = "100";
        record.Scalars["NOTE\\Weight"] = "100";

        var found = new RecordItems(reader).Find(Id)!;

        Assert.Equal((null, null), (found.Value, found.Weight));
    }

    [Fact]
    public void A_missing_reader_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordItems(null!));
    }
}
