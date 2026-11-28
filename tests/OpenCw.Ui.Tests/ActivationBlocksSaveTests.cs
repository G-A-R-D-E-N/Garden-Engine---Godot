using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ActivationBlocksSaveTests
{
    [Fact]
    public void Blocks_round_trip_through_a_save_section_with_their_hidden_text_flag()
    {
        var saved = new ActivationBlocks();
        saved.Set("000800:A.esp", true, true);
        saved.Set("000801:A.esp", true, false);

        var loaded = new ActivationBlocks();
        loaded.Apply(saved.Sections());

        Assert.True(loaded.IsBlocked("000800:A.esp"));
        Assert.True(loaded.HidesText("000800:A.esp"));
        Assert.True(loaded.IsBlocked("000801:A.esp"));
        Assert.False(loaded.HidesText("000801:A.esp"));
    }

    [Fact]
    public void Loading_a_save_without_the_section_clears_blocks_set_since()
    {
        var blocks = new ActivationBlocks();
        blocks.Set("000800:A.esp", true, false);

        blocks.Apply(new Dictionary<string, byte[]>());

        Assert.False(blocks.IsBlocked("000800:A.esp"));
    }

    [Fact]
    public void An_empty_set_writes_no_section_and_a_malformed_one_throws_and_keeps_the_current_blocks()
    {
        var blocks = new ActivationBlocks();
        Assert.Empty(blocks.Sections());
        blocks.Set("000800:A.esp", true, false);

        var bad = new Dictionary<string, byte[]> { [ActivationBlocks.SectionName] = "[{\"ref\":1}]"u8.ToArray() };
        Assert.Throws<FormatException>(() => blocks.Apply(bad));
        Assert.True(blocks.IsBlocked("000800:A.esp"));
    }
}
