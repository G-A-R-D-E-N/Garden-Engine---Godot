using System.Text;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PlayerFactionsTests
{
    static readonly FormKey Settlers = new("Fallout4.esm", 0x44);
    static readonly FormKey Gunners = new("Fallout4.esm", 0x55);
    static readonly string SettlersKey = RecordKey.Format(Settlers);
    static readonly string GunnersKey = RecordKey.Format(Gunners);

    static IReadOnlyDictionary<FormKey, int> Base(params (FormKey Faction, int Rank)[] rows) =>
        rows.ToDictionary(row => row.Faction, row => row.Rank);

    static IReadOnlyDictionary<string, byte[]> Section(string json) =>
        new Dictionary<string, byte[]> { [PlayerFactions.SectionName] = Encoding.UTF8.GetBytes(json) };

    [Fact]
    public void Without_an_overlay_the_base_memberships_pass_through()
    {
        var merged = new PlayerFactions().Merge(Base((Settlers, 2)));

        Assert.Equal(2, merged[Settlers]);
        Assert.Single(merged);
    }

    [Fact]
    public void Joining_adds_a_faction_and_a_new_rank_replaces_the_base_rank()
    {
        var factions = new PlayerFactions();

        Assert.True(factions.SetRank(GunnersKey, 1));
        Assert.True(factions.SetRank(SettlersKey, 5));
        var merged = factions.Merge(Base((Settlers, 2)));

        Assert.Equal(1, merged[Gunners]);
        Assert.Equal(5, merged[Settlers]);
    }

    [Fact]
    public void Leaving_hides_a_base_faction_and_joining_again_restores_it()
    {
        var factions = new PlayerFactions();

        Assert.True(factions.Leave(SettlersKey));
        Assert.Empty(factions.Merge(Base((Settlers, 2))));
        Assert.Equal(PlayerFactions.NotMember, factions.Rank(SettlersKey, Base((Settlers, 2))));

        factions.SetRank(SettlersKey, 0);
        Assert.Equal(0, factions.Rank(SettlersKey, Base((Settlers, 2))));
    }

    [Fact]
    public void Rank_is_minus_one_for_a_faction_neither_list_has_and_ignores_key_case()
    {
        var factions = new PlayerFactions();
        factions.SetRank(GunnersKey.ToLowerInvariant(), 3);

        Assert.Equal(3, factions.Rank(GunnersKey.ToUpperInvariant(), null));
        Assert.Equal(-1, factions.Rank(SettlersKey, null));
    }

    [Fact]
    public void A_negative_rank_is_refused_and_leaves_the_overlay_empty()
    {
        var factions = new PlayerFactions();

        Assert.False(factions.SetRank(SettlersKey, -1));
        Assert.False(factions.SetRank("", 1));
        Assert.True(factions.IsEmpty);
    }

    [Fact]
    public void A_null_base_with_an_overlay_still_lists_the_joined_factions()
    {
        var factions = new PlayerFactions();
        factions.SetRank(GunnersKey, 1);

        Assert.Equal(1, factions.Merge(null)[Gunners]);
    }

    [Fact]
    public void An_empty_overlay_writes_no_section()
    {
        Assert.Empty(new PlayerFactions().Sections());
    }

    [Fact]
    public void The_overlay_round_trips_through_its_section_including_a_left_faction()
    {
        var saved = new PlayerFactions();
        saved.SetRank(GunnersKey, 4);
        saved.Leave(SettlersKey);

        var loaded = new PlayerFactions();
        loaded.Apply(saved.Sections());

        Assert.Equal(saved.Overrides.OrderBy(pair => pair.Key), loaded.Overrides.OrderBy(pair => pair.Key));
        Assert.Equal(-1, loaded.Overrides[SettlersKey]);
        Assert.Equal(4, loaded.Overrides[GunnersKey]);
    }

    [Fact]
    public void Applying_a_save_without_the_section_clears_the_overlay()
    {
        var factions = new PlayerFactions();
        factions.SetRank(GunnersKey, 4);

        factions.Apply(new Dictionary<string, byte[]>());

        Assert.True(factions.IsEmpty);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"version\":2,\"factions\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"factions\":[{\"faction\":\"\",\"rank\":1}]}")]
    [InlineData("{\"version\":1,\"factions\":[{\"faction\":\"000044:Fallout4.esm\",\"rank\":-2}]}")]
    [InlineData("{\"version\":1,\"factions\":[{\"faction\":\"000044:Fallout4.esm\"}]}")]
    public void A_malformed_or_unknown_version_section_throws_and_keeps_the_current_overlay(string json)
    {
        var factions = new PlayerFactions();
        factions.SetRank(GunnersKey, 4);

        Assert.Throws<FormatException>(() => factions.Apply(Section(json)));

        Assert.Equal(4, factions.Overrides[GunnersKey]);
    }

    [Fact]
    public void An_unavailable_base_list_leaves_the_rank_unknown_unless_an_override_answers()
    {
        var factions = new PlayerFactions();
        Assert.Null(factions.RankOrUnknown(SettlersKey, null));
        Assert.Equal(PlayerFactions.NotMember, factions.RankOrUnknown(SettlersKey, Base()));
        Assert.Equal(3, factions.RankOrUnknown(SettlersKey, Base((Settlers, 3))));
        factions.SetRank(SettlersKey, 1);
        Assert.Equal(1, factions.RankOrUnknown(SettlersKey, null));
        factions.Leave(GunnersKey);
        Assert.Equal(PlayerFactions.NotMember, factions.RankOrUnknown(GunnersKey, null));
    }
}
