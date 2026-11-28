using OpenCommonwealth.Services.Ui.Swf;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class SwfBracketLinesTests
{
    [Fact]
    public void The_three_polylines_follow_the_script_drawn_lines()
    {
        var lines = SwfBracketLines.Polylines(new SwfBracket("p", "p/h", 320, 350), 13, 100);

        Assert.Equal(new[] { (0.0, 31.5), (0.0, 27.5), (13.0, 27.5) }, lines[0]);
        Assert.Equal(new[] { (13 + 100 + SwfBracketLines.ArrowWidth, 27.5), (320.0, 27.5), (320.0, 31.5) }, lines[1]);
        Assert.Equal(new[] { (0.0, 346.0), (0.0, 350.0), (320.0, 350.0), (320.0, 346.0) }, lines[2]);
    }

    [Fact]
    public void The_header_gap_widens_with_the_arrow_position()
    {
        var narrow = SwfBracketLines.Polylines(new SwfBracket("p", "p/h", 320, 350), 13, 60)[1][0].X;
        var wide = SwfBracketLines.Polylines(new SwfBracket("p", "p/h", 320, 350), 13, 120)[1][0].X;

        Assert.Equal(60, wide - narrow, 6);
    }

    [Fact]
    public void Brackets_are_part_of_the_apply_signature()
    {
        var a = new SwfBindings();
        var b = new SwfBindings();
        b.Brackets.Add(new SwfBracket("p", "p/h", 320, 350));

        Assert.NotEqual(a.Signature(), b.Signature());
    }
}
