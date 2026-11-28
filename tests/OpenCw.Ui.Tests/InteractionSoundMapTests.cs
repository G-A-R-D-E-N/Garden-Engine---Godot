using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class InteractionSoundMapTests
{
    [Theory]
    [InlineData(InteractionSound.DoorOpen, "", "DOOR\\OpenSound")]
    [InlineData(InteractionSound.DoorClose, "", "DOOR\\CloseSound")]
    [InlineData(InteractionSound.ContainerOpen, "", "CONT\\OpenSound")]
    [InlineData(InteractionSound.ContainerClose, "", "CONT\\CloseSound")]
    [InlineData(InteractionSound.ContainerTakeAll, "", "CONT\\TakeAllSound")]
    [InlineData(InteractionSound.Pickup, "Weapon", "WEAP\\SoundPickUp")]
    [InlineData(InteractionSound.Pickup, "Armor", "ARMO\\PickUpSound")]
    [InlineData(InteractionSound.Pickup, "Ammunition", "AMMO\\PickUpSound")]
    [InlineData(InteractionSound.Pickup, "Ingestible", "ALCH\\PickUpSound")]
    [InlineData(InteractionSound.Pickup, "Ingredient", "INGR\\PickUpSound")]
    [InlineData(InteractionSound.Pickup, "MiscItem", "MISC\\PickUpSound")]
    [InlineData(InteractionSound.Pickup, "Book", "BOOK\\PickUpSound")]
    [InlineData(InteractionSound.Pickup, "Key", "KEYM\\PickUpSound")]
    [InlineData(InteractionSound.Pickup, "Holotape", "NOTE\\PickUpSound")]
    [InlineData(InteractionSound.Pickup, "Note", "NOTE\\PickUpSound")]
    [InlineData(InteractionSound.PutDown, "Weapon", "WEAP\\SoundPutDown")]
    [InlineData(InteractionSound.PutDown, "Armor", "ARMO\\PutDownSound")]
    [InlineData(InteractionSound.PutDown, "Ammunition", "AMMO\\PutDownSound")]
    [InlineData(InteractionSound.PutDown, "Ingestible", "ALCH\\PutDownSound")]
    [InlineData(InteractionSound.PutDown, "Ingredient", "INGR\\PutDownSound")]
    [InlineData(InteractionSound.PutDown, "Book", "BOOK\\PutDownSound")]
    [InlineData(InteractionSound.PutDown, "Key", "KEYM\\PutDownSound")]
    [InlineData(InteractionSound.PutDown, "Holotape", "NOTE\\PutDownSound")]
    [InlineData(InteractionSound.PutDown, "Note", "NOTE\\PutDownSound")]
    public void FieldPathPerSound(InteractionSound sound, string baseType, string path) =>
        Assert.Equal(path, InteractionSoundMap.FieldPath(sound, baseType));

    [Theory]
    [InlineData("Flora")]
    [InlineData("")]
    public void PickupWithoutAReadableFieldStaysSilent(string baseType) =>
        Assert.Null(InteractionSoundMap.FieldPath(InteractionSound.Pickup, baseType));

    [Theory]
    [InlineData("MiscItem")]
    [InlineData("Flora")]
    [InlineData("")]
    public void PutDownWithoutAReadableFieldStaysSilent(string baseType) =>
        Assert.Null(InteractionSoundMap.FieldPath(InteractionSound.PutDown, baseType));

    [Fact]
    public void EveryDroppableBaseTypeButMiscHasAPutDownField()
    {
        foreach (var type in ItemPickup.ItemBaseTypes)
            Assert.Equal(type != "MiscItem", InteractionSoundMap.FieldPath(InteractionSound.PutDown, type) is not null);
    }

    [Fact]
    public void UnknownLockpickCueHasNoSound() => Assert.Null(InteractionSoundMap.LockpickEditorId("nope"));

    [Fact]
    public void EveryPickupBaseTypeHasAField()
    {
        foreach (var type in ItemPickup.ItemBaseTypes)
            Assert.NotNull(InteractionSoundMap.FieldPath(InteractionSound.Pickup, type));
    }

    [Fact]
    public void PickupReportsTheBaseTypeToTheSoundPlayer()
    {
        var pickup = new ItemPickup(new OpenCommonwealth.Services.Ui.Trade.PlayerItems(), _ => true, _ => { });
        ItemTaken? taken = null;
        pickup.Taken += t => taken = t;

        pickup.Activate(new ActivationTarget("Ref:1", "Base:2", "Weapon", new System.Collections.Generic.HashSet<string>()));

        Assert.Equal("Weapon", taken!.BaseType);
    }
}
