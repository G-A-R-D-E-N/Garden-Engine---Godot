using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class GmstSettingsTests
{
    private static FakeRecordReader With(string editorId, string path, string value)
    {
        var reader = new FakeRecordReader();
        reader.Add("000001:a.esm", "GMST", editorId).Scalars[path] = value;
        return reader;
    }

    [Fact]
    public void FloatReadsTheFloatRowAndKeepsTheFallbackWhenItCannot()
    {
        Assert.Equal(2.5f, GmstSettings.Float(With("fThing", GmstSettings.FloatPath, "2.5"), "fThing", 9f));
        Assert.Equal(9f, GmstSettings.Float(new FakeRecordReader(), "fThing", 9f));
        Assert.Equal(9f, GmstSettings.Float(With("fThing", GmstSettings.FloatPath, "not a number"), "fThing", 9f));
        Assert.Equal(9f, GmstSettings.Float(With("fThing", GmstSettings.FloatPath, "NaN"), "fThing", 9f));
        Assert.Equal(9f, GmstSettings.Float(With("fThing", GmstSettings.FloatPath, "Infinity"), "fThing", 9f));
        Assert.Equal(9f, GmstSettings.Float(With("fThing", GmstSettings.IntegerPath, "4"), "fThing", 9f));
    }

    [Fact]
    public void FloatIgnoresARecordOfAnotherSignature()
    {
        var reader = new FakeRecordReader();
        reader.Add("000001:a.esm", "GLOB", "fThing").Scalars[GmstSettings.FloatPath] = "2.5";
        Assert.Equal(9f, GmstSettings.Float(reader, "fThing", 9f));
    }

    [Fact]
    public void IntIsNullWithoutARecordOrAParsableValue()
    {
        Assert.Equal(-7, GmstSettings.Int(With("iThing", GmstSettings.IntegerPath, "-7"), "iThing"));
        Assert.Null(GmstSettings.Int(new FakeRecordReader(), "iThing"));
        Assert.Null(GmstSettings.Int(With("iThing", GmstSettings.IntegerPath, "1.5"), "iThing"));
        Assert.Null(GmstSettings.Int(With("iThing", GmstSettings.FloatPath, "3"), "iThing"));
    }

    [Fact]
    public void TextTrimsTrailingNulsAndWhitespaceAndIsNullWhenAbsent()
    {
        Assert.Equal("Stash", GmstSettings.Text(With("sThing", GmstSettings.StringPath, " Stash \0"), "sThing"));
        Assert.Equal(string.Empty, GmstSettings.Text(With("sThing", GmstSettings.StringPath, "  "), "sThing"));
        Assert.Null(GmstSettings.Text(new FakeRecordReader(), "sThing"));
    }
}
