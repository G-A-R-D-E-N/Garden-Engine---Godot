using System.Text;
using OpenCommonwealth.Services.Ui.Character;
using Xunit;

namespace OpenCw.Ui.Tests;

public class GlobalVariablesTests
{
    const string Counter = "000123:Fallout4.esm";

    static IReadOnlyDictionary<string, byte[]> Section(string json) =>
        new Dictionary<string, byte[]> { [GlobalVariables.SectionName] = Encoding.UTF8.GetBytes(json) };

    [Fact]
    public void A_global_that_was_never_set_has_no_value()
    {
        Assert.Null(new GlobalVariables().Get(Counter));
        Assert.True(new GlobalVariables().IsEmpty);
    }

    [Fact]
    public void A_set_value_is_read_back_ignoring_case_and_rejects_non_finite_numbers()
    {
        var globals = new GlobalVariables();

        Assert.True(globals.Set(Counter, 4.5));
        Assert.False(globals.Set(Counter, double.NaN));
        Assert.False(globals.Set("", 1));

        Assert.Equal(4.5, globals.Get(Counter.ToLowerInvariant()));
    }

    [Fact]
    public void The_overlay_round_trips_through_its_save_section()
    {
        var globals = new GlobalVariables();
        globals.Set(Counter, 2);

        var restored = new GlobalVariables();
        restored.Apply(globals.Sections());

        Assert.Equal(2, restored.Get(Counter));
    }

    [Fact]
    public void An_empty_overlay_writes_no_section_and_a_save_without_one_reads_as_no_overlay()
    {
        Assert.Empty(new GlobalVariables().Sections());
        Assert.Empty(GlobalVariables.Parse(new Dictionary<string, byte[]>()));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":2,\"rows\":[]}")]
    [InlineData("{\"version\":1,\"rows\":[{\"global\":\"000123:Fallout4.esm\"}]}")]
    [InlineData("{\"version\":1,\"rows\":[{\"global\":\"\",\"value\":1}]}")]
    public void A_malformed_section_throws_and_leaves_the_held_values_alone(string json)
    {
        var globals = new GlobalVariables();
        globals.Set(Counter, 9);

        Assert.Throws<FormatException>(() => globals.Apply(Section(json)));
        Assert.Throws<FormatException>(() => GlobalVariables.Parse(Section(json)));

        Assert.Equal(9, globals.Get(Counter));
    }
}
