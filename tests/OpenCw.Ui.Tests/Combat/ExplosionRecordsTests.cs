using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ExplosionRecordsTests
{
    const string Mine = "000E20:Test.esm";

    static FakeRecordReader Reader(string signature = "EXPL")
    {
        var reader = new FakeRecordReader();
        var record = reader.Add(Mine, signature, "BottleCapMineExplosion");
        record.Scalars["EXPL\\Damage"] = "300";
        record.Scalars["EXPL\\Force"] = "100";
        record.Scalars["EXPL\\InnerRadius"] = "100";
        record.Scalars["EXPL\\OuterRadius"] = "500";
        record.Scalars["EXPL\\Flags"] = "0";
        return reader;
    }

    [Fact]
    public void AReadableExplosionCarriesItsRawFields()
    {
        Assert.Equal(new ExplosionInfo(300f, 100f, 100f, 500f, 0), ExplosionRecords.Read(Reader(), Mine));
    }

    [Theory]
    [InlineData("EXPL\\Damage")]
    [InlineData("EXPL\\Force")]
    [InlineData("EXPL\\InnerRadius")]
    [InlineData("EXPL\\OuterRadius")]
    [InlineData("EXPL\\Flags")]
    public void AnUnreadableFieldMakesTheExplosionNull(string path)
    {
        var reader = Reader();
        reader.Records[Mine].Scalars[path] = null!;
        Assert.Null(ExplosionRecords.Read(reader, Mine));
    }

    [Theory]
    [InlineData("EXPL\\Damage", "lots")]
    [InlineData("EXPL\\OuterRadius", "NaN")]
    [InlineData("EXPL\\Flags", "-4")]
    public void ANonNumericOrNonFiniteFieldMakesTheExplosionNull(string path, string value)
    {
        var reader = Reader();
        reader.Records[Mine].Scalars[path] = value;
        Assert.Null(ExplosionRecords.Read(reader, Mine));
    }

    [Fact]
    public void ARecordOfAnotherSignatureIsNotAnExplosion()
    {
        Assert.Null(ExplosionRecords.Read(Reader(signature: "HAZD"), Mine));
    }

    [Fact]
    public void AnExplosionPlacesTheHazardItsLinkNamesOnlyWhenThatLinkIsAHazardRecord()
    {
        var reader = Reader();
        reader.Add("000E21:Test.esm", "HAZD");
        reader.Add("000E22:Test.esm", "STAT");
        var links = new Dictionary<string, string?> { [Mine] = "000E21:Test.esm", ["000E30:Test.esm"] = "000E22:Test.esm" };
        reader.Add("000E30:Test.esm", "EXPL");
        var asked = new List<string>();
        string? Link(string key) { asked.Add(key); return links.GetValueOrDefault(key); }

        Assert.Equal("000E21:Test.esm", ExplosionRecords.PlacedHazard(reader, Link, Mine));
        Assert.Null(ExplosionRecords.PlacedHazard(reader, Link, "000E30:Test.esm"));
        Assert.Null(ExplosionRecords.PlacedHazard(reader, Link, "000E21:Test.esm"));
        Assert.Null(ExplosionRecords.PlacedHazard(reader, Link, "000E99:Test.esm"));
        Assert.Equal(new[] { Mine, "000E30:Test.esm" }, asked);
    }

    [Fact]
    public void AnExplosionWithNoLinkPlacesNoHazard()
    {
        Assert.Null(ExplosionRecords.PlacedHazard(Reader(), _ => null, Mine));
    }
}
