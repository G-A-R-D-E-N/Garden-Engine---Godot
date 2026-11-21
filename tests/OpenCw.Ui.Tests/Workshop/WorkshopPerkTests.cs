using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using OpenCommonwealth.Services.Ui.Workshop;
using Radicle.Interop;
using Xunit;
using FormKey = Radicle.Interop.FormKey;
using RadicleConditionFunctions = Radicle.Interop.ConditionFunctions;

namespace OpenCw.Ui.Tests;

public class WorkshopPerkTests
{
    const string Bench = "000010:a.esm";
    const string Recipe = "000050:a.esm";
    const string Built = "000040:a.esm";
    const string Steel = "000030:a.esm";
    const string Tin = "000031:a.esm";
    const string PerkId = "000F01:a.esm";

    sealed class Facts : IRecipeFacts
    {
        public double? Level { get; set; }
        public bool Perk { get; set; }

        public bool? HasPerk(FormKey perk) => Perk;
        public bool? HasKeyword(FormKey keyword) => null;
        public double? Global(FormKey global) => null;
        public int ItemCount(FormKey item) => 0;
    }

    static RecordCondition Row(string function, FormKey? param1, float value, byte flags = 0) =>
        new(new RadicleCondition(flags, BitConverter.SingleToUInt32Bits(value), RadicleConditionFunctions.Id(function)!.Value, 0, 0, 0, 0, 0, 0, null, null),
            null, param1, null, null);

    static FakeRecordReader Data(params RecordCondition[] conditions)
    {
        var reader = new FakeRecordReader();
        reader.Add(Bench, "KYWD", "WorkshopWorkbenchTypeSettlement");
        reader.Add(Steel, "CMPO", "c_Steel", "Steel");
        reader.Add(Built, "STAT", "Gen01", "Generator");
        reader.Add(PerkId, "PERK", "Scrounger01", "Scrounger");
        var record = reader.AddRecipe(Recipe, Built, Bench, [], (Steel, 2));
        record.Recipe = record.Recipe! with { Conditions = conditions };
        return reader;
    }

    [Fact]
    public void ARecipeGatedOnAPerkListsAsPerkRequiredWhileThePerkIsMissing()
    {
        var catalog = new RecordWorkshopCatalog(Data(Row("HasPerk", FakeRecordReader.Key(PerkId), 1f)), new FakeStock { Counts = { [Steel] = 9 } }, [Bench], false, new Facts());

        var leaf = Assert.Single(catalog.Roots.SelectMany(Leaves));
        Assert.Equal(WorkshopItemStatus.PerkRequired, catalog.StatusOf(Recipe));
        var perk = Assert.Single(leaf.Item!.Perks);
        Assert.Equal("Scrounger", perk.Name);
        Assert.False(perk.Met);
    }

    [Fact]
    public void TheSameRecipeIsAvailableOnceThePerkIsHeld()
    {
        var catalog = new RecordWorkshopCatalog(Data(Row("HasPerk", FakeRecordReader.Key(PerkId), 1f)), new FakeStock { Counts = { [Steel] = 9 } }, [Bench], false, new Facts { Perk = true });

        Assert.Single(catalog.Roots.SelectMany(Leaves));
        Assert.Equal(WorkshopItemStatus.Available, catalog.StatusOf(Recipe));
    }

    [Fact]
    public void ARecipeGatedOnSomethingElseThePlayerLacksIsHidden()
    {
        var catalog = new RecordWorkshopCatalog(Data(Row("GetLevel", null, 5f, 0x60)), new FakeStock(), [Bench], false, new Facts { Level = 2 });

        Assert.Empty(catalog.Roots);
    }

    [Fact]
    public void ARecipeGatedOnAnInputTheFactsCannotAnswerStillLists()
    {
        var catalog = new RecordWorkshopCatalog(Data(Row("HasKeyword", FakeRecordReader.Key(PerkId), 1f)), new FakeStock { Counts = { [Steel] = 9 } }, [Bench], false, new Facts());

        Assert.Single(catalog.Roots.SelectMany(Leaves));
        Assert.Equal(WorkshopItemStatus.Available, catalog.StatusOf(Recipe));
    }

    [Fact]
    public void ARecipeWhoseConditionsCouldNotBeDecodedStillLists()
    {
        var reader = Data();
        reader.Records[Recipe].Recipe = reader.Records[Recipe].Recipe! with { Conditions = null };
        var catalog = new RecordWorkshopCatalog(reader, new FakeStock { Counts = { [Steel] = 9 } }, [Bench], false, new Facts());

        Assert.Single(catalog.Roots.SelectMany(Leaves));
        Assert.Equal(WorkshopItemStatus.Available, catalog.StatusOf(Recipe));
    }

    [Fact]
    public void WithoutFactsConditionsAreIgnoredAsBefore()
    {
        var catalog = new RecordWorkshopCatalog(Data(Row("HasPerk", FakeRecordReader.Key(PerkId), 1f)), new FakeStock { Counts = { [Steel] = 9 } }, [Bench]);

        Assert.Equal(WorkshopItemStatus.Available, catalog.StatusOf(Recipe));
    }

    [Fact]
    public void TheCatalogCountsJunkComponentsThroughTheComponentPool()
    {
        var reader = Data();
        reader.Add(Tin, "MISC", "Tin", "Tin").Parts((Steel, 3));
        var items = new PlayerItems();
        items.Add(Tin, 2);
        var catalog = new RecordWorkshopCatalog(reader, new ComponentPool(reader, items), [Bench]);

        Assert.Equal(6, catalog.AvailableCount(Steel));
        Assert.Equal(3, catalog.BuildableCount(Recipe));
        Assert.Equal(WorkshopItemStatus.Available, catalog.StatusOf(Recipe));
    }

    static IEnumerable<WorkshopNode> Leaves(WorkshopNode node) =>
        node.IsCategory ? node.Children.SelectMany(Leaves) : [node];
}
