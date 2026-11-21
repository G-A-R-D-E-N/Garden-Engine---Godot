using System.Text;
using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ScriptStateSaveTests
{
    [Fact]
    public void Teammates_and_xp_sharing_round_trip_and_an_empty_set_writes_nothing()
    {
        var saved = new PlayerTeammates();
        Assert.Empty(saved.Sections());
        saved.Set("000800:A.esp", true);
        saved.Set("000801:A.esp", true);
        saved.SetGivesXp("000800:A.esp", true);

        var loaded = new PlayerTeammates();
        loaded.Apply(saved.Sections());

        Assert.True(loaded.IsTeammate("000800:A.esp"));
        Assert.True(loaded.IsTeammate("000801:A.esp"));
        Assert.True(loaded.GivesXp("000800:A.esp"));
        Assert.False(loaded.GivesXp("000801:A.esp"));
    }

    [Fact]
    public void Loading_a_save_without_the_section_replaces_the_current_flags()
    {
        var live = new PlayerTeammates();
        live.Set("000800:A.esp", true);
        live.SetGivesXp("000800:A.esp", true);

        live.Apply(new Dictionary<string, byte[]>());

        Assert.False(live.IsTeammate("000800:A.esp"));
        Assert.False(live.GivesXp("000800:A.esp"));
    }

    [Fact]
    public void A_malformed_teammate_section_throws_and_keeps_the_current_flags()
    {
        var live = new PlayerTeammates();
        live.Set("000800:A.esp", true);
        var bad = new Dictionary<string, byte[]> { [PlayerTeammates.SectionName] = Encoding.UTF8.GetBytes("{\"xp\":[]}") };

        Assert.Throws<FormatException>(() => live.Apply(bad));

        Assert.True(live.IsTeammate("000800:A.esp"));
    }

    [Fact]
    public void An_xp_giver_who_is_not_a_teammate_is_dropped_on_load()
    {
        var sections = new Dictionary<string, byte[]> { [PlayerTeammates.SectionName] = Encoding.UTF8.GetBytes("{\"teammates\":[\"a\"],\"xp\":[\"a\",\"b\"]}") };
        var loaded = new PlayerTeammates();

        loaded.Apply(sections);

        Assert.True(loaded.GivesXp("a"));
        Assert.False(loaded.GivesXp("b"));
    }

    [Fact]
    public void Enable_overrides_round_trip_including_disabled_references()
    {
        var saved = new EnableOverrides();
        saved.Set("000800:A.esp", false);
        saved.Set("000801:A.esp", true);

        var loaded = new EnableOverrides();
        loaded.Set("000802:A.esp", true);
        loaded.Apply(saved.Sections());

        Assert.False(loaded.StateOf("000800:A.esp"));
        Assert.True(loaded.StateOf("000801:A.esp"));
        Assert.Null(loaded.StateOf("000802:A.esp"));
    }

    [Fact]
    public void Dropped_lists_the_keys_that_lost_their_override_once_and_in_order()
    {
        var now = new Dictionary<string, bool> { ["b"] = true };

        Assert.Equal(new[] { "c", "a" }, EnableOverrides.Dropped(new[] { "c", "b", "a", "c" }, now));
        Assert.Empty(EnableOverrides.Dropped(Array.Empty<string>(), now));
    }

    [Fact]
    public void A_malformed_enable_section_throws_and_keeps_the_current_overrides()
    {
        var live = new EnableOverrides();
        live.Set("000800:A.esp", false);
        var bad = new Dictionary<string, byte[]> { [EnableOverrides.SectionName] = Encoding.UTF8.GetBytes("[{\"ref\":\"x\"}]") };

        Assert.Throws<FormatException>(() => live.Apply(bad));

        Assert.False(live.StateOf("000800:A.esp"));
    }
}
