using System;
using System.Linq;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ItemRecordTypesTests
{
    [Fact]
    public void FormTypeMatchesTheOldLiterals()
    {
        Assert.Equal("1346454871", ItemRecordTypes.FormType("WEAP"));
        Assert.Equal("1330467393", ItemRecordTypes.FormType("ARMO"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("WEA")]
    [InlineData("WEAPS")]
    public void FormTypeRejectsNonFourCharacterSignatures(string signature) =>
        Assert.Throws<ArgumentException>(() => ItemRecordTypes.FormType(signature));

    [Fact]
    public void EveryCoreSignatureHasAPickupBaseType()
    {
        foreach (var signature in ItemRecordTypes.Core)
            Assert.Contains(ItemRecordTypes.PickupBaseType(signature), ItemPickup.ItemBaseTypes);
        foreach (var signature in ItemRecordTypes.Extra)
            Assert.Null(ItemRecordTypes.PickupBaseType(signature));
    }

    [Fact]
    public void CategoryMapsFourTypesAndEverythingElseIsMisc()
    {
        Assert.Equal("Weapons", ItemRecordTypes.Category("WEAP"));
        Assert.Equal("Apparel", ItemRecordTypes.Category("ARMO"));
        Assert.Equal("Aid", ItemRecordTypes.Category("ALCH"));
        Assert.Equal("Ammo", ItemRecordTypes.Category("AMMO"));
        foreach (var signature in new[] { "MISC", "BOOK", "KEYM", "NOTE", "INGR", "LIGH", "CMPO", "COBJ", string.Empty })
            Assert.Equal("Misc", ItemRecordTypes.Category(signature));
        Assert.Equal("Misc", ItemRecordTypes.Category(null));
    }

    // Constraint: detects an item base type list kept beside the table: a dropped Holotape name stops loose holotape pickups, a dropped table name stops that item's pickups and drops.
    [Fact]
    public void ItemPickupHandlesTheTableBaseTypesAndTheHolotapeClassName()
    {
        Assert.Equal(
            new[] { "Ammunition", "Armor", "Book", "Holotape", "Ingestible", "Ingredient", "Key", "MiscItem", "Note", "Weapon" },
            ItemPickup.ItemBaseTypes.OrderBy(t => t, StringComparer.Ordinal));
        Assert.Same(ItemRecordTypes.PickupBaseTypes, ItemPickup.ItemBaseTypes);
        foreach (var signature in ItemRecordTypes.Core)
            Assert.Contains(ItemRecordTypes.PickupBaseType(signature)!, ItemRecordTypes.PickupBaseTypes);
    }

    [Fact]
    public void ConsumersAgreeWithTheTable()
    {
        foreach (var signature in ItemRecordTypes.Core.Concat(ItemRecordTypes.Extra))
        {
            Assert.Equal(ItemRecordTypes.Category(signature), PlayerInventoryItems.CategoryOf(signature));
            Assert.Equal(ItemRecordTypes.PickupBaseType(signature), PlayerInventoryItems.BaseTypeOf(signature));
        }
        Assert.Equal(
            ItemRecordTypes.Core.Concat(ItemRecordTypes.Extra).OrderBy(s => s, StringComparer.Ordinal),
            RecordItems.ItemSignatures.OrderBy(s => s, StringComparer.Ordinal));
    }

    // Constraint: detects a pickup sound table kept beside the item table: a new core item type with no sound row would pick up silently, and a sound row for a non-item type would play for a non-pickup.
    [Fact]
    public void PickupSoundPathsFollowTheItemTableAndTheHolotapeAliasesNote()
    {
        foreach (var signature in ItemRecordTypes.Core)
            Assert.Equal(signature == "WEAP" ? "WEAP\\SoundPickUp" : signature + "\\PickUpSound", ItemRecordTypes.PickupSoundPath(signature));
        foreach (var signature in ItemRecordTypes.Extra.Append("COBJ"))
            Assert.Null(ItemRecordTypes.PickupSoundPath(signature));
        Assert.Null(ItemRecordTypes.PickupSoundPath(null));
        Assert.Equal(
            InteractionSoundMap.FieldPath(InteractionSound.Pickup, "Note"),
            InteractionSoundMap.FieldPath(InteractionSound.Pickup, ItemRecordTypes.HolotapeBaseType));
    }

    [Fact]
    public void PutDownSoundPathsFollowThePickupTableExceptMisc()
    {
        foreach (var signature in ItemRecordTypes.Core.Where(signature => signature != "MISC"))
            Assert.Equal(signature == "WEAP" ? "WEAP\\SoundPutDown" : signature + "\\PutDownSound", ItemRecordTypes.PutDownSoundPath(signature));
        Assert.Null(ItemRecordTypes.PutDownSoundPath("MISC"));
        foreach (var signature in ItemRecordTypes.Extra.Append("COBJ"))
            Assert.Null(ItemRecordTypes.PutDownSoundPath(signature));
        Assert.Null(ItemRecordTypes.PutDownSoundPath(null));
    }
}
