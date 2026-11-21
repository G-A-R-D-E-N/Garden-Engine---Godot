using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests;

public class HazardRecordsTests
{
    const string Hazard = "000E10:Test.esm", Spell = "000E11:Test.esm", Ench = "000E12:Test.esm";

    static FakeRecordReader Reader(string signature = "HAZD", string? spell = Spell)
    {
        var reader = new FakeRecordReader();
        reader.Add(Spell, "SPEL", "TrapOilFireHazard");
        reader.Add(Ench, "ENCH", "MirelurkAcid");
        var record = reader.Add(Hazard, signature, "TrapOilHazard01");
        record.Scalars["HAZD\\Radius"] = "4";
        record.Scalars["HAZD\\Lifetime"] = "9";
        record.Scalars["HAZD\\TargetInterval"] = "0.2";
        record.Scalars["HAZD\\Limit"] = "8";
        record.Scalars["HAZD\\Flags"] = "16";
        record.Scalars["HAZD\\Spell"] = spell!;
        return reader;
    }

    [Fact]
    public void AReadableHazardCarriesItsFieldsAndSpellKey()
    {
        var info = HazardRecords.Read(Reader(), Hazard);
        Assert.Equal(new HazardInfo(4f, 9f, 0.2f, 8, 16, Spell), info);
    }

    [Fact]
    public void AHazardWhoseSpellLinkIsAnEnchantmentIsReadTheSameWay()
    {
        Assert.Equal(Ench, HazardRecords.Read(Reader(spell: Ench), Hazard)!.SpellKey);
    }

    [Fact]
    public void AHazardWithAnEmptySpellLinkIsReadableWithANullSpellKey()
    {
        var info = HazardRecords.Read(Reader(spell: string.Empty), Hazard);
        Assert.Equal(new HazardInfo(4f, 9f, 0.2f, 8, 16, null), info);
    }

    [Fact]
    public void AHazardWhoseSpellRowIsNullKeepsASpelllessHazard()
    {
        var info = HazardRecords.Read(Reader(spell: null), Hazard);
        Assert.Equal(new HazardInfo(4f, 9f, 0.2f, 8, 16, null), info);
    }

    [Theory]
    [InlineData("HAZD\\Radius")]
    [InlineData("HAZD\\Lifetime")]
    [InlineData("HAZD\\TargetInterval")]
    [InlineData("HAZD\\Limit")]
    [InlineData("HAZD\\Flags")]
    public void AnUnreadableFieldMakesTheHazardNull(string path)
    {
        var reader = Reader();
        reader.Records[Hazard].Scalars[path] = null!;
        Assert.Null(HazardRecords.Read(reader, Hazard));
    }

    [Theory]
    [InlineData("HAZD\\Radius", "soon")]
    [InlineData("HAZD\\TargetInterval", "NaN")]
    [InlineData("HAZD\\Lifetime", "Infinity")]
    [InlineData("HAZD\\Limit", "-1")]
    [InlineData("HAZD\\Flags", "1.5")]
    public void ANonNumericOrNonFiniteFieldMakesTheHazardNull(string path, string value)
    {
        var reader = Reader();
        reader.Records[Hazard].Scalars[path] = value;
        Assert.Null(HazardRecords.Read(reader, Hazard));
    }

    [Theory]
    [InlineData("-4")]
    [InlineData("-0.5")]
    public void ANegativeRadiusMakesTheHazardNull(string radius)
    {
        var reader = Reader();
        reader.Records[Hazard].Scalars["HAZD\\Radius"] = radius;
        Assert.Null(HazardRecords.Read(reader, Hazard));
    }

    [Fact]
    public void AZeroRadiusIsStillReadable()
    {
        var reader = Reader();
        reader.Records[Hazard].Scalars["HAZD\\Radius"] = "0";
        Assert.Equal(0f, HazardRecords.Read(reader, Hazard)!.Radius);
    }

    [Fact]
    public void ARecordOfAnotherSignatureIsNotAHazard()
    {
        Assert.Null(HazardRecords.Read(Reader(signature: "SPEL"), Hazard));
    }

    [Fact]
    public void AnUnknownKeyIsNotAHazard()
    {
        Assert.Null(HazardRecords.Read(Reader(), "000E99:Test.esm"));
    }
}
