using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests.Character;

public sealed class LiveProgressionTests
{
    const string Rank1 = "000010:Test.esm", Rank2 = "000011:Test.esm", Single = "000020:Test.esm", HiddenPerk = "000030:Test.esm", Ability = "000040:Test.esm";

    static (LiveProgression Progression, PlayerExperience Xp, PlayerPerks Perks) Make(long xp = 0)
    {
        var reader = new FakeRecordReader();
        var first = reader.Add(Rank1, "PERK", name: "Gunner");
        first.Scalars[LiveProgression.PlayablePath] = "1";
        first.Scalars[LiveProgression.LevelPath] = "1";
        first.Scalars[RecordPlayerStatusSource.NextPerkPath] = Rank2;
        var second = reader.Add(Rank2, "PERK", name: "Gunner");
        second.Scalars[LiveProgression.PlayablePath] = "1";
        second.Scalars[LiveProgression.LevelPath] = "3";
        var one = reader.Add(Single, "PERK", name: "Lockpicker");
        one.Scalars[LiveProgression.PlayablePath] = "true";
        reader.Add(HiddenPerk, "PERK", name: "Hidden").Scalars[LiveProgression.PlayablePath] = "1";
        reader.Records[HiddenPerk].Scalars[LiveProgression.HiddenPath] = "1";
        reader.Add(Ability, "PERK", name: "Ability");
        var experience = new PlayerExperience();
        if (xp > 0) experience.Add(xp);
        var perks = new PlayerPerks();
        return (new LiveProgression(experience, perks, () => reader), experience, perks);
    }

    [Fact]
    public void OnlyPlayableVisibleNonTraitPerkChainsAreListedAndAttributesAreNeverAvailable()
    {
        var (progression, _, _) = Make();
        var entries = progression.Snapshot.Chart.Entries;
        Assert.Equal(new[] { "Gunner", "Lockpicker" }, entries.Where(e => e.Kind == PerkChartKind.Perk).Select(e => e.Name).ToArray());
        Assert.Equal(2, entries.Single(e => e.Id == Rank1).MaxRank);
        Assert.Equal(7, entries.Count(e => e.Kind == PerkChartKind.Attribute));
        Assert.All(entries.Where(e => e.Kind == PerkChartKind.Attribute), e => Assert.False(e.Available));
        Assert.True(progression.Snapshot.IsValid);
    }

    [Fact]
    public void ChoosingARankSpendsAPointHoldsTheRecordAndReplacesTheLowerRank()
    {
        var (progression, xp, perks) = Make(475);
        Assert.Equal(2, xp.PerkPoints);
        var changes = 0;
        progression.Changed += () => changes++;
        Assert.Equal(ChooseResult.Ok, progression.TryChoose(Rank1, 1));
        Assert.True(perks.Has(Rank1));
        Assert.Equal(1, xp.PerkPoints);
        Assert.Equal(ChooseResult.Ok, progression.TryChoose(Rank1, 2));
        Assert.False(perks.Has(Rank1));
        Assert.True(perks.Has(Rank2));
        Assert.Equal(0, xp.PerkPoints);
        Assert.Equal(2, changes);
        Assert.Equal(2, progression.Snapshot.Chart.Entries.Single(e => e.Id == Rank1).CurrentRank);
        Assert.Equal(ChooseResult.AlreadyMax, progression.TryChoose(Rank1, 3));
    }

    [Fact]
    public void ARefusedChoiceChangesNothing()
    {
        var (progression, xp, perks) = Make(200);
        Assert.Equal(1, xp.PerkPoints);
        Assert.Equal(ChooseResult.Unknown, progression.TryChoose(Rank1, 2));
        Assert.Equal(ChooseResult.Unknown, progression.TryChoose("000999:Test.esm", 1));
        Assert.Equal(ChooseResult.Locked, progression.TryChoose("attr0", 2));
        perks.Add(Rank1);
        Assert.Equal(ChooseResult.Locked, progression.TryChoose(Rank1, 2));
        Assert.Equal(new[] { Rank1 }, perks.Ranks.Keys.ToArray());
        Assert.Equal(1, xp.PerkPoints);
        Assert.Equal(ChooseResult.Ok, progression.TryChoose(Single, 1));
        Assert.Equal(ChooseResult.NoPoints, progression.TryChoose(Rank1, 2));
        Assert.Equal(0, xp.PerkPoints);
    }

    [Fact]
    public void ALevelGainNotifiesButALoadDoesNot()
    {
        var (progression, xp, _) = Make();
        var changes = 0;
        progression.Changed += () => changes++;
        xp.Add(475);
        Assert.Equal(1, changes);
        var saved = xp.Sections();
        xp.Apply(new Dictionary<string, byte[]>());
        xp.Apply(saved);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void WithoutARecordReaderTheChartHasOnlyTheAttributes()
    {
        var xp = new PlayerExperience();
        var progression = new LiveProgression(xp, new PlayerPerks(), () => null);
        Assert.Equal(7, progression.Snapshot.Chart.Entries.Count);
        Assert.Equal(ChooseResult.Unknown, progression.TryChoose(Rank1, 1));
    }

    [Fact]
    public void LevelGainsAwardOnePointEachAndLoadsAndCurveChangesAwardNone()
    {
        var xp = new PlayerExperience();
        var gained = new List<int>();
        xp.LevelGained += gained.Add;
        xp.Add(500);
        Assert.Equal((3, 2), (xp.Level, xp.PerkPoints));
        Assert.Equal(new[] { 3 }, gained.ToArray());
        xp.Curve = new XpCurve(100, 0);
        Assert.Equal(2, xp.PerkPoints);
        Assert.True(xp.Spend(1));
        Assert.False(xp.Spend(2));
        Assert.False(xp.Spend(0));
        var loaded = new PlayerExperience();
        loaded.Apply(xp.Sections());
        Assert.Equal(1, loaded.PerkPoints);
        Assert.Equal(1, gained.Count);
        loaded.Apply(new Dictionary<string, byte[]>());
        Assert.True(loaded.IsEmpty);
    }

    [Fact]
    public void ASaveWithoutPointsReadsAsNoneAndOutOfRangePointsAreRejected()
    {
        var xp = new PlayerExperience();
        xp.Apply(new Dictionary<string, byte[]> { [PlayerExperience.SectionName] = Encoding.UTF8.GetBytes("{\"total\":500}") });
        Assert.Equal((500L, 0), (xp.Total, xp.PerkPoints));
        foreach (var bad in new[] { "{\"total\":500,\"points\":-1}", "{\"total\":500,\"points\":256}" })
            Assert.Throws<FormatException>(() => xp.Apply(new Dictionary<string, byte[]> { [PlayerExperience.SectionName] = Encoding.UTF8.GetBytes(bad) }));
        Assert.Equal(0, xp.PerkPoints);
    }

    static (LiveProgression Progression, PlayerExperience Xp) MakeWithSpecial(int baseValue, long xpTotal)
    {
        var xp = new PlayerExperience();
        if (xpTotal > 0) xp.Add(xpTotal);
        IReadOnlyList<SpecialEntry> Rows() => PlayerSpecialRows.Ids.Select(id => new SpecialEntry(id, id, baseValue + xp.RaisedBy(id))).ToArray();
        return (new LiveProgression(xp, new PlayerPerks(), () => null, Rows), xp);
    }

    [Fact]
    public void AnAttributeRaiseSpendsAPointAndShowsInTheNextSnapshot()
    {
        var (progression, xp) = MakeWithSpecial(5, 200);
        Assert.Equal(1, xp.PerkPoints);
        Assert.True(progression.Snapshot.Chart.Entries.First(e => e.Kind == PerkChartKind.Attribute).Available);
        var changes = 0;
        progression.Changed += () => changes++;
        Assert.Equal(ChooseResult.Unknown, progression.TryChoose("attr0", 7));
        Assert.Equal(ChooseResult.Ok, progression.TryChoose("attr0", 6));
        Assert.Equal((1, 0, 1), (changes, xp.PerkPoints, xp.RaisedBy("strength")));
        Assert.Equal(6, progression.Snapshot.Chart.Entries.First(e => e.Id == "attr0").CurrentRank);
        Assert.Equal(ChooseResult.NoPoints, progression.TryChoose("attr1", 6));
        Assert.Equal(ChooseResult.Unknown, progression.TryChoose("attr9", 6));
        Assert.Equal(ChooseResult.Unknown, progression.TryChoose("attrx", 6));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AnAttributeAtTheCeilingOrWithAnUnknownBaseCannotBeRaised()
    {
        var (full, fullXp) = MakeWithSpecial(10, 200);
        Assert.False(full.Snapshot.Chart.Entries.First(e => e.Kind == PerkChartKind.Attribute).Available);
        Assert.Equal(ChooseResult.AlreadyMax, full.TryChoose("attr2", 11));
        Assert.Equal((1, 0), (fullXp.PerkPoints, fullXp.RaisedBy("Endurance")));
        var xp = new PlayerExperience();
        xp.Add(200);
        var unknown = new LiveProgression(xp, new PlayerPerks(), () => null);
        Assert.Equal(ChooseResult.Locked, unknown.TryChoose("attr0", 2));
        Assert.Equal((1, 0), (xp.PerkPoints, xp.RaisedBy("Strength")));
    }

    [Fact]
    public void SpecialRaisesSaveAndLoadAndAnOldSaveLoadsWithNone()
    {
        var (_, xp) = MakeWithSpecial(5, 500);
        Assert.True(xp.RaiseSpecial(3));
        Assert.True(xp.RaiseSpecial(3));
        Assert.False(xp.RaiseSpecial(0));
        var loaded = new PlayerExperience();
        loaded.Apply(xp.Sections());
        Assert.Equal((2, 0, 0), (loaded.RaisedBy("Charisma"), loaded.PerkPoints, loaded.RaisedBy("Luck")));
        Assert.Equal(0, loaded.RaisedBy("Health"));
        loaded.Apply(new Dictionary<string, byte[]> { [PlayerExperience.SectionName] = Encoding.UTF8.GetBytes("{\"total\":500,\"points\":2}") });
        Assert.Equal((0, 2), (loaded.RaisedBy("Charisma"), loaded.PerkPoints));
        loaded.Apply(xp.Sections());
        loaded.Clear();
        Assert.True(loaded.IsEmpty);
        Assert.Equal(0, loaded.RaisedBy("Charisma"));
    }

    [Fact]
    public void MalformedSpecialRaisesAreRejectedAndLeaveTheStoreUntouched()
    {
        var (_, xp) = MakeWithSpecial(5, 500);
        Assert.True(xp.RaiseSpecial(0));
        foreach (var bad in new[] { "{\"total\":9,\"special\":[1,2]}", "{\"total\":9,\"special\":[0,0,0,0,0,0,11]}", "{\"total\":9,\"special\":[0,0,0,0,0,0,-1]}", "{\"total\":9,\"special\":5}", "{\"total\":9,\"special\":[0,0,0,0,0,0,null]}" })
            Assert.Throws<FormatException>(() => xp.Apply(new Dictionary<string, byte[]> { [PlayerExperience.SectionName] = Encoding.UTF8.GetBytes(bad) }));
        Assert.Equal((500L, 1), (xp.Total, xp.RaisedBy("Strength")));
    }
}
