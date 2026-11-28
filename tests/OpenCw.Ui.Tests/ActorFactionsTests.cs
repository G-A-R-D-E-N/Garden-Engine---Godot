using System.Text;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ActorFactionsTests
{
    const string Preston = "02A8A7:Fallout4.esm";
    const string Piper = "02A8A8:Fallout4.esm";
    static readonly FormKey Settlers = new("Fallout4.esm", 0x44);
    static readonly FormKey Gunners = new("Fallout4.esm", 0x55);
    static readonly string SettlersKey = RecordKey.Format(Settlers);
    static readonly string GunnersKey = RecordKey.Format(Gunners);

    static IReadOnlyDictionary<FormKey, int> Base(params (FormKey Faction, int Rank)[] rows) =>
        rows.ToDictionary(row => row.Faction, row => row.Rank);

    static IReadOnlyDictionary<string, byte[]> Section(string json) =>
        new Dictionary<string, byte[]> { [ActorFactions.SectionName] = Encoding.UTF8.GetBytes(json) };

    [Fact]
    public void Without_an_overlay_the_rank_is_the_base_rank_or_not_a_member()
    {
        var factions = new ActorFactions();

        Assert.Equal(2, factions.Rank(Preston, SettlersKey, Base((Settlers, 2))));
        Assert.Equal(ActorFactions.NotMember, factions.Rank(Preston, GunnersKey, Base((Settlers, 2))));
    }

    [Fact]
    public void An_overlay_is_per_actor()
    {
        var factions = new ActorFactions();
        factions.SetRank(Preston, GunnersKey, 1);

        Assert.Equal(1, factions.Rank(Preston, GunnersKey, Base()));
        Assert.Equal(ActorFactions.NotMember, factions.Rank(Piper, GunnersKey, Base()));
    }

    [Fact]
    public void Leaving_hides_a_base_faction_and_joining_again_restores_it()
    {
        var factions = new ActorFactions();

        Assert.True(factions.Leave(Preston, SettlersKey));
        Assert.Equal(ActorFactions.NotMember, factions.Rank(Preston, SettlersKey, Base((Settlers, 2))));
        Assert.Equal(2, factions.Rank(Piper, SettlersKey, Base((Settlers, 2))));

        factions.SetRank(Preston, SettlersKey, 0);
        Assert.Equal(0, factions.Rank(Preston, SettlersKey, Base((Settlers, 2))));
    }

    [Fact]
    public void Keys_are_compared_ignoring_case_and_a_negative_rank_is_refused()
    {
        var factions = new ActorFactions();

        Assert.False(factions.SetRank(Preston, SettlersKey, -1));
        Assert.False(factions.SetRank("", SettlersKey, 1));
        Assert.True(factions.IsEmpty);
        factions.SetRank(Preston.ToLowerInvariant(), GunnersKey.ToLowerInvariant(), 3);

        Assert.Equal(3, factions.Rank(Preston.ToLowerInvariant(), GunnersKey.ToUpperInvariant(), Base()));
        Assert.Equal(3, factions.Changed(Preston.ToLowerInvariant(), GunnersKey));
    }

    [Fact]
    public void An_actor_key_written_without_leading_zeros_is_the_same_actor()
    {
        var factions = new ActorFactions();

        factions.SetRank("2A8A7:Fallout4.esm", GunnersKey, 3);

        Assert.Equal(3, factions.Changed(Preston, GunnersKey));
        Assert.Equal(3, factions.Rank("2a8a7:fallout4.esm", GunnersKey, Base()));
    }

    [Fact]
    public void An_empty_overlay_writes_no_section()
    {
        Assert.Empty(new ActorFactions().Sections());
    }

    [Fact]
    public void The_overlay_round_trips_through_its_section_including_a_left_faction()
    {
        var saved = new ActorFactions();
        saved.SetRank(Preston, GunnersKey, 4);
        saved.Leave(Preston, SettlersKey);
        saved.SetRank(Piper, SettlersKey, 1);

        var loaded = new ActorFactions();
        loaded.Apply(saved.Sections());

        Assert.Equal(4, loaded.Changed(Preston, GunnersKey));
        Assert.Equal(ActorFactions.NotMember, loaded.Changed(Preston, SettlersKey));
        Assert.Equal(1, loaded.Changed(Piper, SettlersKey));
        Assert.Null(loaded.Changed(Piper, GunnersKey));
    }

    [Fact]
    public void Applying_a_save_without_the_section_clears_the_overlay()
    {
        var factions = new ActorFactions();
        factions.SetRank(Preston, GunnersKey, 4);

        factions.Apply(new Dictionary<string, byte[]>());

        Assert.True(factions.IsEmpty);
        Assert.Empty(ActorFactions.Parse(new Dictionary<string, byte[]>()));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"version\":2,\"rows\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"rows\":[{\"actor\":\"\",\"faction\":\"000044:Fallout4.esm\",\"rank\":1}]}")]
    [InlineData("{\"version\":1,\"rows\":[{\"actor\":\"02A8A7:Fallout4.esm\",\"faction\":\"\",\"rank\":1}]}")]
    [InlineData("{\"version\":1,\"rows\":[{\"actor\":\"02A8A7:Fallout4.esm\",\"faction\":\"000044:Fallout4.esm\",\"rank\":-2}]}")]
    [InlineData("{\"version\":1,\"rows\":[{\"actor\":\"02A8A7:Fallout4.esm\",\"faction\":\"000044:Fallout4.esm\"}]}")]
    public void A_malformed_or_unknown_version_section_throws_and_keeps_the_current_overlay(string json)
    {
        var factions = new ActorFactions();
        factions.SetRank(Preston, GunnersKey, 4);

        Assert.Throws<FormatException>(() => factions.Apply(Section(json)));
        Assert.Throws<FormatException>(() => ActorFactions.Parse(Section(json)));

        Assert.Equal(4, factions.Changed(Preston, GunnersKey));
    }
}
