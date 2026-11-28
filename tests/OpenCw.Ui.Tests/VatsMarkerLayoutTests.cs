using System;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VatsMarkerLayoutTests
{
    const double Width = 1920;
    const double Height = 1000;

    static double Distance(VatsPoint a, VatsPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    [Fact]
    public void Coincident_markers_are_pushed_to_the_minimum_spacing()
    {
        var placed = VatsMarkerLayout.Relax(new[] { new VatsPoint(900, 500), new VatsPoint(900, 500) }, Width, Height);

        Assert.True(Distance(placed[0], placed[1]) >= VatsMarkerLayout.MinimumSpacing * Height - 1e-6);
    }

    [Fact]
    public void Markers_already_far_enough_apart_do_not_move()
    {
        var points = new[] { new VatsPoint(100, 100), new VatsPoint(100, 400), new VatsPoint(900, 900) };

        Assert.Equal(points, VatsMarkerLayout.Relax(points, Width, Height));
    }

    [Fact]
    public void No_marker_moves_more_than_the_cap_even_in_a_crowd()
    {
        var points = new[] { new VatsPoint(900, 500), new VatsPoint(900, 500), new VatsPoint(901, 500), new VatsPoint(900, 501) };

        var placed = VatsMarkerLayout.Relax(points, Width, Height);

        for (int i = 0; i < points.Length; i++)
            Assert.True(Distance(points[i], placed[i]) <= VatsMarkerLayout.MaximumMove * Height + 1e-6);
    }

    [Fact]
    public void Markers_stay_inside_the_view()
    {
        var placed = VatsMarkerLayout.Relax(new[] { new VatsPoint(0, 0), new VatsPoint(0, 0) }, Width, Height);

        foreach (VatsPoint p in placed)
        {
            Assert.InRange(p.X, 0, Width);
            Assert.InRange(p.Y, 0, Height);
        }
    }

    [Fact]
    public void Empty_single_or_unmeasured_input_is_returned_unchanged()
    {
        Assert.Empty(VatsMarkerLayout.Relax(Array.Empty<VatsPoint>(), Width, Height));

        var single = new[] { new VatsPoint(5, 5) };
        Assert.Equal(single, VatsMarkerLayout.Relax(single, Width, Height));

        var pair = new[] { new VatsPoint(5, 5), new VatsPoint(5, 5) };
        Assert.Equal(pair, VatsMarkerLayout.Relax(pair, 0, 0));
    }
}
