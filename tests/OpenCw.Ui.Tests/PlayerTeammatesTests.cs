using System;
using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PlayerTeammatesTests
{
    [Fact]
    public void Set_marks_and_clears_one_reference_without_touching_others()
    {
        var teammates = new PlayerTeammates();
        teammates.Set("00AA:A.esm", true);
        teammates.Set("00BB:A.esm", true);
        teammates.Set("00AA:A.esm", false);

        Assert.False(teammates.IsTeammate("00AA:A.esm"));
        Assert.True(teammates.IsTeammate("00BB:A.esm"));
        Assert.Equal(1, teammates.Count);
    }

    [Fact]
    public void Setting_twice_counts_once_and_clearing_an_unknown_key_is_a_no_op()
    {
        var teammates = new PlayerTeammates();
        teammates.Set("00AA:A.esm", true);
        teammates.Set("00AA:A.esm", true);
        teammates.Set("00CC:A.esm", false);

        Assert.Equal(1, teammates.Count);
    }

    [Fact]
    public void An_empty_key_is_never_a_teammate_and_cannot_be_set()
    {
        var teammates = new PlayerTeammates();

        Assert.False(teammates.IsTeammate(""));
        Assert.Throws<ArgumentException>(() => teammates.Set("", true));
    }

    [Fact]
    public void Clear_drops_every_teammate()
    {
        var teammates = new PlayerTeammates();
        teammates.Set("00AA:A.esm", true);
        teammates.Clear();

        Assert.False(teammates.IsTeammate("00AA:A.esm"));
        Assert.Equal(0, teammates.Count);
    }
}
