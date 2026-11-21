using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Terminals;
using Xunit;

namespace OpenCw.Ui.Tests;

public class HackingInputsTests
{
    [Fact]
    public void A_dictionary_that_is_missing_at_first_is_read_again_and_then_kept()
    {
        string? text = null;
        var reads = 0;
        var words = new LazyHackingWords(() => { reads++; return text; });

        Assert.Empty(words.OfLength(4));
        Assert.Empty(words.OfLength(4));
        Assert.Equal(2, reads);

        text = "cold cord hold";
        Assert.Equal(["COLD", "CORD", "HOLD"], words.OfLength(4));

        text = null;
        Assert.Equal(3, words.OfLength(4).Count);
        Assert.Equal(3, reads);
    }

    [Fact]
    public void Text_with_no_usable_words_is_not_kept()
    {
        string? text = "123 !!!";
        var words = new LazyHackingWords(() => text);

        Assert.Empty(words.OfLength(3));

        text = "cat";
        Assert.Equal(["CAT"], words.OfLength(3));
    }

    [Fact]
    public void Intelligence_is_the_base_value_plus_the_live_modifier()
    {
        var rows = new List<SpecialEntry> { new("Strength", "Strength", 9), new("intelligence", "Intelligence", 6, 2) };

        Assert.Equal(8f, HackingIntelligence.Of(rows));
        Assert.Null(HackingIntelligence.Of([]));
        Assert.Null(HackingIntelligence.Of([new SpecialEntry("Intelligence", "Intelligence", double.NaN)]));
    }
}
