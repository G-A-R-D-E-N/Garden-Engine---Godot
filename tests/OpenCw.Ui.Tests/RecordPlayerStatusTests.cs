using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class RecordPlayerStatusTests
{
    sealed class Live : IPlayerStatusSource
    {
        public string? PlayerName => "Nora";

        public double? Health => 40;

        public double? MaxHealth => 100;

        public double? PendingHealthGain => 5;

        public double? Radiation => 2;

        public IReadOnlyList<DamageTotal>? DamageTotals => null;

        public IReadOnlyList<DamageTotal>? ResistTotals => null;

        public AidSupply? Stimpak => new("Stimpak", 3);

        public AidSupply? RadAway => null;

        public IReadOnlyList<EffectSource> Effects => [];

        public List<SpecialEntry> SpecialList { get; } = [];

        public List<OwnedPerk> PerkList { get; } = [];

        public IReadOnlyList<SpecialEntry> Special => SpecialList;

        public IReadOnlyList<OwnedPerk> Perks => PerkList;

        public bool ReadOnly => true;

        public LimbCondition? Limb(LimbKind limb) => new(0.5, limb == LimbKind.Head);
    }

    internal static FakeRecord PerkOf(FakeRecord record, string description, string? next)
    {
        record.Scalars["PERK\\Description"] = description;
        if (next is not null) record.Scalars["PERK\\NextPerk"] = next;
        return record;
    }

    static FakeRecordReader Data()
    {
        var reader = new FakeRecordReader();
        reader.Add("000001:a.esm", "AVIF", "Strength", "Strength");
        PerkOf(reader.Add("000002:a.esm", "PERK", "Gunslinger01", "Gunslinger"), "More damage.", "000003:a.esm");
        PerkOf(reader.Add("000003:a.esm", "PERK", "Gunslinger02", "Gunslinger"), "Even more damage.", "000002:a.esm");
        return reader;
    }

    [Fact]
    public void ForwardsVitalsAndLiveState()
    {
        var status = new RecordPlayerStatusSource(new Live(), Data());

        Assert.Equal(("Nora", 40.0, 100.0, 5.0, 2.0, true), (status.PlayerName, status.Health, status.MaxHealth, status.PendingHealthGain, status.Radiation, status.ReadOnly));
        Assert.Equal(3, status.Stimpak!.Count);
        Assert.True(status.Limb(LimbKind.Head)!.Crippled);
    }

    [Fact]
    public void SpecialNamesComeFromRecordsByEditorIdOrKeyAndDescriptionsStayLive()
    {
        var live = new Live();
        live.SpecialList.Add(new SpecialEntry("Strength", "STR", 6, 1));
        live.SpecialList.Add(new SpecialEntry("000001:a.esm", "x", 3));
        live.SpecialList.Add(new SpecialEntry("Nothing", "Unknown stat", 2, 0, "kept"));

        var special = new RecordPlayerStatusSource(live, Data()).Special;

        Assert.Equal(new SpecialEntry("Strength", "Strength", 6, 1), special[0]);
        Assert.Equal(new SpecialEntry("000001:a.esm", "Strength", 3), special[1]);
        Assert.Equal(new SpecialEntry("Nothing", "Unknown stat", 2, 0, "kept"), special[2]);
    }

    [Fact]
    public void KeyOfTheWrongRecordTypeIsLeftAlone()
    {
        var live = new Live();
        live.SpecialList.Add(new SpecialEntry("000002:a.esm", "Perk-ish", 1));

        Assert.Equal("Perk-ish", new RecordPlayerStatusSource(live, Data()).Special[0].Name);
    }

    [Fact]
    public void PerkRanksFollowTheNextPerkChainWithoutLooping()
    {
        var live = new Live();
        live.PerkList.Add(new OwnedPerk("Gunslinger01", "old", 2, ["fallback"]));

        var perk = new RecordPlayerStatusSource(live, Data()).Perks.Single();

        Assert.Equal("Gunslinger", perk.Name);
        Assert.Equal(2, perk.Rank);
        Assert.Equal(["More damage.", "Even more damage."], perk.RankDescriptions);
    }

    [Fact]
    public void PerkWithoutDescriptionsKeepsTheLiveOnes()
    {
        var reader = new FakeRecordReader();
        PerkOf(reader.Add("000007:a.esm", "PERK", "Bare", "Bare perk"), "", null);
        var live = new Live();
        live.PerkList.Add(new OwnedPerk("Bare", "old", 1, ["fallback"]));

        var perk = new RecordPlayerStatusSource(live, reader).Perks.Single();

        Assert.Equal("Bare perk", perk.Name);
        Assert.Equal(["fallback"], perk.RankDescriptions);
    }

    [Fact]
    public void PerkWithUnknownIdIsUntouched()
    {
        var live = new Live();
        var original = new OwnedPerk("Gone", "Gone perk", 1, ["x"]);
        live.PerkList.Add(original);

        Assert.Equal(original, new RecordPlayerStatusSource(live, Data()).Perks.Single());
    }

    [Fact]
    public void DescribedListsAreCachedUntilTheLiveListChanges()
    {
        var reader = Data();
        var live = new Live();
        live.SpecialList.Add(new SpecialEntry("000001:a.esm", "x", 3));
        live.PerkList.Add(new OwnedPerk("Gunslinger01", "old", 1, ["f"]));
        var status = new RecordPlayerStatusSource(live, reader);

        var first = status.Special;
        _ = status.Perks;
        var calls = reader.SignatureCalls;
        Assert.Same(first, status.Special);
        _ = status.Perks;

        Assert.Equal(calls, reader.SignatureCalls);

        live.SpecialList.Add(new SpecialEntry("000001:a.esm", "x", 4));

        Assert.Equal(2, status.Special.Count);
        Assert.NotSame(first, status.Special);
    }
}
