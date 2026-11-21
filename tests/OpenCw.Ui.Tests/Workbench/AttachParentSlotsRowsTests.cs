using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class AttachParentSlotsRowsTests
{
    const string Item = "000800:Fallout4.esm";
    const string Barrel = "000B01:Fallout4.esm";
    const string Sight = "000B02:Fallout4.esm";

    static FakeRecordReader Reader(string signature, string path, params string[] slots)
    {
        var reader = new FakeRecordReader();
        reader.Add(Item, signature).Lists[path] = slots;
        return reader;
    }

    [Fact]
    public void AWeaponReadsItsSlotsFromTheWeaponRowInStoredOrder()
    {
        Assert.Equal(new[] { Sight, Barrel }, RecordRows.AttachParentSlots(Reader("WEAP", "WEAP\\AttachParentSlots", Sight, Barrel), Item));
    }

    [Fact]
    public void ArmorReadsItsSlotsFromTheArmorRow()
    {
        Assert.Equal(new[] { Barrel }, RecordRows.AttachParentSlots(Reader("ARMO", "ARMO\\AttachParentSlots", Barrel), Item));
    }

    [Fact]
    public void ARowOfTheOtherSignatureIsNotRead()
    {
        Assert.Empty(RecordRows.AttachParentSlots(Reader("WEAP", "ARMO\\AttachParentSlots", Barrel), Item));
    }

    [Fact]
    public void NullAndEmptyLinksAreDropped()
    {
        Assert.Equal(new[] { Barrel }, RecordRows.AttachParentSlots(Reader("WEAP", "WEAP\\AttachParentSlots", "", "000000:Fallout4.esm", Barrel), Item));
    }

    [Fact]
    public void AnotherSignatureAnEmptyRowOrAMissingRecordHasNoSlots()
    {
        Assert.Empty(RecordRows.AttachParentSlots(Reader("NPC_", "NPC_\\AttachParentSlots", Barrel), Item));
        Assert.Empty(RecordRows.AttachParentSlots(Reader("WEAP", "WEAP\\AttachParentSlots"), Item));
        Assert.Empty(RecordRows.AttachParentSlots(new FakeRecordReader(), Item));
    }

    [Fact]
    public void ModSlotsResolveRawWordsAndDropNullUnparsableAndUnresolved()
    {
        const string Mod = "000900:Fallout4.esm";
        var reader = new FakeRecordReader();
        var record = reader.Add(Mod, "OMOD");
        record.Lists["OMOD\\AttachParentSlot"] = ["2817", "0", "x", "-1", "9999", "2818"];
        record.RawForms[2817] = Barrel;
        record.RawForms[2818] = Sight;
        record.RawForms[0] = "000000:Fallout4.esm";
        Assert.Equal(new[] { Barrel, Sight }, RecordRows.ModAttachParentSlots(reader, Mod));
        Assert.Empty(RecordRows.ModAttachParentSlots(reader, "000901:Fallout4.esm"));
        Assert.Empty(RecordRows.ModAttachParentSlots(new FakeRecordReader { Records = { [Mod] = new FakeRecord { Signature = "OMOD" } } }, Mod));
    }
}
