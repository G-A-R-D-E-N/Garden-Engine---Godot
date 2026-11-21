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

    [Fact]
    public void Equivalent_spellings_of_one_form_key_are_the_same_teammate()
    {
        var teammates = new PlayerTeammates();
        teammates.Set("0000ab:FALLOUT4.ESM", true);
        teammates.SetGivesXp("0000AB:fallout4.esm", true);

        Assert.True(teammates.IsTeammate("0000AB:Fallout4.esm"));
        Assert.True(teammates.IsTeammate("AB:fallout4.esm"));
        Assert.True(teammates.GivesXp("0000ab:FALLOUT4.ESM"));
        Assert.Equal(1, teammates.Count);

        teammates.Set("0000AB:fallout4.ESM", false);

        Assert.False(teammates.IsTeammate("0000ab:Fallout4.esm"));
        Assert.False(teammates.GivesXp("0000ab:Fallout4.esm"));
    }

    [Fact]
    public void Dynamic_tokens_that_are_not_form_keys_keep_their_exact_spelling()
    {
        var teammates = new PlayerTeammates();
        teammates.Set("Dynamic:Abc", true);

        Assert.True(teammates.IsTeammate("Dynamic:Abc"));
        Assert.False(teammates.IsTeammate("dynamic:abc"));
    }

    [Fact]
    public void A_saved_key_in_another_spelling_loads_as_the_same_teammate()
    {
        var sections = new System.Collections.Generic.Dictionary<string, byte[]>
        {
            [PlayerTeammates.SectionName] = System.Text.Encoding.UTF8.GetBytes("{\"teammates\":[\"0000ab:FALLOUT4.ESM\"],\"xp\":[\"0000AB:fallout4.esm\"]}"),
        };
        var loaded = new PlayerTeammates();

        loaded.Apply(sections);

        Assert.True(loaded.IsTeammate("0000AB:Fallout4.esm"));
        Assert.True(loaded.GivesXp("0000AB:Fallout4.esm"));
    }
}
