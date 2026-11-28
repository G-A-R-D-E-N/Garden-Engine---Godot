using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class LinkedRefsRowsTests
{
    private static FakeRecordReader Reader(string signature, string keywordPath, string referencePath, string[] keywords, string[] references)
    {
        var reader = new FakeRecordReader();
        var record = reader.Add("000800:Fallout4.esm", signature);
        record.Lists[keywordPath] = keywords;
        record.Lists[referencePath] = references;
        return reader;
    }

    [Fact]
    public void An_actor_reads_its_pairs_from_the_ACHR_rows_in_link_order()
    {
        var reader = Reader("ACHR", "ACHR\\LinkedReferenceKeyword", "ACHR\\LinkedReference",
            ["1102EE:Fallout4.esm", "000F00:Fallout4.esm"], ["000A01:Fallout4.esm", "000A02:Fallout4.esm"]);
        Assert.Equal(new[] { ("1102EE:Fallout4.esm", "000A01:Fallout4.esm"), ("000F00:Fallout4.esm", "000A02:Fallout4.esm") }, RecordRows.LinkedRefs(reader, "000800:Fallout4.esm").ToArray());
    }

    [Fact]
    public void A_placed_object_reads_the_REFR_rows_where_the_keyword_row_is_named_differently()
    {
        var reader = Reader("REFR", "REFR\\LinkedKeyword", "REFR\\LinkedReference", ["1102EE:Fallout4.esm"], ["000A01:Fallout4.esm"]);
        Assert.Equal(new[] { ("1102EE:Fallout4.esm", "000A01:Fallout4.esm") }, RecordRows.LinkedRefs(reader, "000800:Fallout4.esm").ToArray());
    }

    [Fact]
    public void A_keyword_only_entry_is_dropped_and_the_pairs_around_it_keep_their_index()
    {
        var reader = Reader("ACHR", "ACHR\\LinkedReferenceKeyword", "ACHR\\LinkedReference",
            ["1102EE:Fallout4.esm", "1102EF:Fallout4.esm", "1102FD:Fallout4.esm"], ["000A01:Fallout4.esm", "", "000A03:Fallout4.esm"]);
        Assert.Equal(["1102EE:Fallout4.esm", "1102FD:Fallout4.esm"], RecordRows.LinkedRefs(reader, "000800:Fallout4.esm").Select(link => link.Keyword));
        Assert.Equal(["000A01:Fallout4.esm", "000A03:Fallout4.esm"], RecordRows.LinkedRefs(reader, "000800:Fallout4.esm").Select(link => link.Reference));
    }

    [Fact]
    public void A_null_keyword_link_is_dropped()
    {
        var reader = Reader("ACHR", "ACHR\\LinkedReferenceKeyword", "ACHR\\LinkedReference", ["000000:Fallout4.esm"], ["000A01:Fallout4.esm"]);
        Assert.Empty(RecordRows.LinkedRefs(reader, "000800:Fallout4.esm"));
    }

    [Fact]
    public void Lists_of_different_length_or_another_signature_or_a_missing_record_give_no_links()
    {
        var uneven = Reader("ACHR", "ACHR\\LinkedReferenceKeyword", "ACHR\\LinkedReference", ["1102EE:Fallout4.esm"], []);
        Assert.Empty(RecordRows.LinkedRefs(uneven, "000800:Fallout4.esm"));
        var other = Reader("NPC_", "ACHR\\LinkedReferenceKeyword", "ACHR\\LinkedReference", ["1102EE:Fallout4.esm"], ["000A01:Fallout4.esm"]);
        Assert.Empty(RecordRows.LinkedRefs(other, "000800:Fallout4.esm"));
        Assert.Empty(RecordRows.LinkedRefs(new FakeRecordReader(), "000800:Fallout4.esm"));
    }

    [Fact]
    public void Default_links_are_the_null_keyword_entries_with_a_reference_in_link_order()
    {
        var reader = Reader("REFR", "REFR\\LinkedKeyword", "REFR\\LinkedReference",
            ["000000:Fallout4.esm", "1102EE:Fallout4.esm", "", "1102EF:Fallout4.esm", "000000:Fallout4.esm"],
            ["000A01:Fallout4.esm", "000A02:Fallout4.esm", "000A03:Fallout4.esm", "", ""]);
        Assert.Equal(["000A01:Fallout4.esm", "000A03:Fallout4.esm"], RecordRows.DefaultLinkedRefs(reader, "000800:Fallout4.esm"));
        Assert.Equal(new[] { ("1102EE:Fallout4.esm", "000A02:Fallout4.esm") }, RecordRows.LinkedRefs(reader, "000800:Fallout4.esm").ToArray());
    }

    [Fact]
    public void Default_links_are_empty_for_uneven_lists_another_signature_or_a_missing_record()
    {
        var uneven = Reader("REFR", "REFR\\LinkedKeyword", "REFR\\LinkedReference", ["000000:Fallout4.esm"], []);
        Assert.Empty(RecordRows.DefaultLinkedRefs(uneven, "000800:Fallout4.esm"));
        var other = Reader("NPC_", "REFR\\LinkedKeyword", "REFR\\LinkedReference", ["000000:Fallout4.esm"], ["000A01:Fallout4.esm"]);
        Assert.Empty(RecordRows.DefaultLinkedRefs(other, "000800:Fallout4.esm"));
        Assert.Empty(RecordRows.DefaultLinkedRefs(new FakeRecordReader(), "000800:Fallout4.esm"));
    }

    [Fact]
    public void An_actor_reads_its_default_links_from_the_ACHR_rows_as_the_complement_of_its_pairs()
    {
        var reader = Reader("ACHR", "ACHR\\LinkedReferenceKeyword", "ACHR\\LinkedReference",
            ["000000:Fallout4.esm", "1102EE:Fallout4.esm", ""],
            ["000A01:Fallout4.esm", "000A02:Fallout4.esm", "000A03:Fallout4.esm"]);
        Assert.Equal(["000A01:Fallout4.esm", "000A03:Fallout4.esm"], RecordRows.DefaultLinkedRefs(reader, "000800:Fallout4.esm"));
        Assert.Equal(["000A02:Fallout4.esm"], RecordRows.LinkedRefs(reader, "000800:Fallout4.esm").Select(link => link.Reference));
    }

    [Fact]
    public void A_default_link_with_a_zero_local_or_unparsable_reference_is_dropped()
    {
        var reader = Reader("REFR", "REFR\\LinkedKeyword", "REFR\\LinkedReference",
            ["000000:Fallout4.esm", "", "1102EE:Fallout4.esm"],
            ["000000:Fallout4.esm", "not-a-form", "000A01:Fallout4.esm"]);
        Assert.Empty(RecordRows.DefaultLinkedRefs(reader, "000800:Fallout4.esm"));
    }

    [Fact]
    public void A_keyworded_entry_is_never_a_default_link_even_with_an_unparsable_reference()
    {
        var reader = Reader("REFR", "REFR\\LinkedKeyword", "REFR\\LinkedReference",
            ["1102EE:Fallout4.esm"], ["not-a-form"]);
        Assert.Empty(RecordRows.DefaultLinkedRefs(reader, "000800:Fallout4.esm"));
        Assert.Empty(RecordRows.LinkedRefs(reader, "000800:Fallout4.esm"));
    }

    [Fact]
    public void Default_links_keep_link_order_across_plugins()
    {
        var reader = Reader("REFR", "REFR\\LinkedKeyword", "REFR\\LinkedReference",
            ["000000:Fallout4.esm", "1102EE:Fallout4.esm", "000000:Fallout4.esm"],
            ["000A01:Fallout4.esm", "000A02:Fallout4.esm", "000a01:a.esp"]);
        Assert.Equal(["000A01:Fallout4.esm", "000A01:a.esp"], RecordRows.DefaultLinkedRefs(reader, "000800:Fallout4.esm"));
    }

    [Fact]
    public void A_record_without_the_two_columns_gives_no_default_links()
    {
        var reader = new FakeRecordReader();
        reader.Add("000800:Fallout4.esm", "REFR");
        Assert.Empty(RecordRows.DefaultLinkedRefs(reader, "000800:Fallout4.esm"));
    }
}
