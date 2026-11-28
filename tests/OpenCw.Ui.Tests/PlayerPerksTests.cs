using System.Text;
using OpenCommonwealth.Services.Ui.Character;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class PlayerPerksTests
{
    const string Perk = "000F01:a.esm";

    [Fact]
    public void AddingAPerkGivesRankOneAndRemovingItClearsIt()
    {
        var perks = new PlayerPerks();

        Assert.True(perks.Add(Perk));
        Assert.True(perks.Has(Perk));
        Assert.Equal(1, perks.RankOf(Perk));
        Assert.True(perks.Remove(Perk));
        Assert.False(perks.Has(Perk));
        Assert.False(perks.Remove(Perk));
    }

    [Fact]
    public void KeysCompareIgnoringCase()
    {
        var perks = new PlayerPerks();
        perks.Add("000f01:A.ESM");

        Assert.True(perks.Has(Perk));
        Assert.False(perks.Add(Perk));
    }

    [Fact]
    public void AHeldPerkKeepsItsRankWhenAddedAgain()
    {
        var perks = new PlayerPerks();
        perks.Apply(new Dictionary<string, byte[]> { [PlayerPerks.SectionName] = "[{\"perk\":\"000F01:a.esm\",\"rank\":3}]"u8.ToArray() });

        Assert.False(perks.Add(Perk));
        Assert.Equal(3, perks.RankOf(Perk));
    }

    [Fact]
    public void PerksRoundTripThroughASaveSection()
    {
        var saved = new PlayerPerks();
        saved.Add(Perk);
        saved.Add("000F02:a.esm");

        var loaded = new PlayerPerks();
        loaded.Apply(saved.Sections());

        Assert.True(loaded.Has(Perk));
        Assert.True(loaded.Has("000F02:a.esm"));
        Assert.Equal(2, loaded.Ranks.Count);
    }

    [Fact]
    public void LoadingASaveWithoutTheSectionClearsPerksGainedSince()
    {
        var perks = new PlayerPerks();
        perks.Add(Perk);

        perks.Apply(new Dictionary<string, byte[]>());

        Assert.True(perks.IsEmpty);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{\"perk\":\"\",\"rank\":1}]")]
    [InlineData("[{\"perk\":\"000F01:a.esm\",\"rank\":0}]")]
    [InlineData("[{\"perk\":\"000F01:a.esm\"}]")]
    [InlineData("not json")]
    public void AMalformedSectionThrowsAndKeepsTheCurrentPerks(string payload)
    {
        var perks = new PlayerPerks();
        perks.Add(Perk);

        Assert.Throws<FormatException>(() => perks.Apply(new Dictionary<string, byte[]> { [PlayerPerks.SectionName] = Encoding.UTF8.GetBytes(payload) }));
        Assert.True(perks.Has(Perk));
    }

    [Fact]
    public void AnEmptyStoreWritesNoSection()
    {
        Assert.Empty(new PlayerPerks().Sections());
    }
}
