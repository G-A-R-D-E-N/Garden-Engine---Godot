using System;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class LightRowsTests
{
    private const string Id = "0A1B2C:Fallout4.esm";

    private static FakeRecord Light(FakeRecordReader reader) => reader.Add(Id, "LIGH");

    private static LightRow Read(FakeRecordReader reader) => LightRows.Read(path => reader.Scalar(Id, path), _ => false);

    [Fact]
    public void A_full_DATA_block_reads_every_field_from_its_own_row()
    {
        var reader = new FakeRecordReader();
        var light = Light(reader);
        light.Scalars["LIGH\\Radius"] = "512";
        light.Scalars["LIGH\\ColorRed"] = "255";
        light.Scalars["LIGH\\ColorGreen"] = "128";
        light.Scalars["LIGH\\ColorBlue"] = "1";
        light.Scalars["LIGH\\Flags"] = "1040";
        light.Scalars["LIGH\\FalloffExponent"] = "1.5";
        light.Scalars["LIGH\\FOV"] = "60";
        light.Scalars["LIGH\\NearClip"] = "4.25";
        light.Scalars["LIGH\\FlickerPeriod"] = "0.5";
        light.Scalars["LIGH\\FlickerIntensityAmplitude"] = "0.25";
        light.Scalars["LIGH\\FlickerMovementAmplitude"] = "3";
        light.Scalars["LIGH\\Constant"] = "0.125";
        light.Scalars["LIGH\\Scalar"] = "2";
        light.Scalars["LIGH\\Exponent"] = "3";
        light.Scalars["LIGH\\FadeValue"] = "0.75";
        light.Scalars["LIGH\\Gobo"] = "Textures\\Effects\\Gobos\\A.dds";
        Assert.Equal(new LightRow(512, 255, 128, 1, 1040, 1.5f, 60f, 4.25f, 0.5f, 0.25f, 3f, 0.125f, 2f, 3f, 0.75f, "Textures\\Effects\\Gobos\\A.dds", false), Read(reader));
    }

    [Fact]
    public void A_light_with_no_readable_rows_takes_the_defaults_of_an_absent_DATA_and_keeps_the_exponent_null()
    {
        Assert.Equal(new LightRow(0, 0, 0, 0, 0, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, null, 1f, null, false), Read(new FakeRecordReader()));
    }

    [Fact]
    public void HasData_is_false_for_a_LIGH_with_no_DATA_so_a_census_skips_it_instead_of_counting_a_zero_radius_light()
    {
        Assert.False(LightRows.HasData(path => new FakeRecordReader().Scalar(Id, path)));
        Assert.False(LightRows.HasData(_ => throw new RadicleException(default, "unreadable")));
    }

    [Fact]
    public void HasData_is_true_for_a_short_DATA_that_stops_before_the_exponent_row()
    {
        var reader = new FakeRecordReader();
        Light(reader).Scalars["LIGH\\Radius"] = "0";
        Assert.True(LightRows.HasData(path => reader.Scalar(Id, path)));
    }

    [Fact]
    public void A_short_DATA_has_no_exponent_row_so_the_constant_and_scalar_rows_are_still_read_but_the_exponent_stays_null()
    {
        var reader = new FakeRecordReader();
        var light = Light(reader);
        light.Scalars["LIGH\\Constant"] = "0.5";
        light.Scalars["LIGH\\Scalar"] = "4";
        var row = Read(reader);
        Assert.Null(row.Exponent);
        Assert.Equal(0.5f, row.Constant);
        Assert.Equal(4f, row.Scalar);
    }

    [Fact]
    public void Float_text_in_exponent_notation_parses_with_the_invariant_culture()
    {
        var reader = new FakeRecordReader();
        var light = Light(reader);
        light.Scalars["LIGH\\NearClip"] = "1e-05";
        light.Scalars["LIGH\\Constant"] = "-2.5E+01";
        var row = Read(reader);
        Assert.Equal(1e-05f, row.NearClip);
        Assert.Equal(-25f, row.Constant);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void A_float_row_that_is_empty_unparsable_or_not_finite_reads_as_its_default(string text)
    {
        var reader = new FakeRecordReader();
        var light = Light(reader);
        light.Scalars["LIGH\\FOV"] = text;
        light.Scalars["LIGH\\Scalar"] = text;
        light.Scalars["LIGH\\Exponent"] = text;
        light.Scalars["LIGH\\FadeValue"] = text;
        var row = Read(reader);
        Assert.Equal(0f, row.Fov);
        Assert.Equal(1f, row.Scalar);
        Assert.Null(row.Exponent);
        Assert.Equal(1f, row.Fade);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("256")]
    [InlineData("1.5")]
    [InlineData("")]
    public void A_colour_row_outside_a_byte_reads_as_zero(string text)
    {
        var reader = new FakeRecordReader();
        var light = Light(reader);
        light.Scalars["LIGH\\ColorRed"] = text;
        light.Scalars["LIGH\\ColorGreen"] = "7";
        Assert.Equal((byte)0, Read(reader).Red);
        Assert.Equal((byte)7, Read(reader).Green);
    }

    [Fact]
    public void A_flags_row_keeps_every_bit_of_an_unsigned_32_bit_value()
    {
        var reader = new FakeRecordReader();
        Light(reader).Scalars["LIGH\\Flags"] = "4294967295";
        Assert.Equal(uint.MaxValue, Read(reader).Flags);
    }

    [Fact]
    public void A_row_that_throws_a_Radicle_error_reads_as_its_default_and_the_other_rows_still_read()
    {
        var reader = new FakeRecordReader();
        var light = Light(reader);
        light.Scalars["LIGH\\Radius"] = "300";
        light.Scalars["LIGH\\FOV"] = "60";
        var row = LightRows.Read(path => path == "LIGH\\FOV" ? throw new RadicleException(default, "bad width") : reader.Scalar(Id, path), _ => false);
        Assert.Equal(0f, row.Fov);
        Assert.Equal(300u, row.Radius);
    }

    [Fact]
    public void A_header_flag_true_sets_RandomAnimStart_from_the_LIGH_RandomAnimStart_path()
    {
        var reader = new FakeRecordReader();
        Light(reader);
        Assert.True(LightRows.Read(path => reader.Scalar(Id, path), path => path == "LIGH\\RandomAnimStart").RandomAnimStart);
    }

    [Fact]
    public void A_header_flag_false_leaves_RandomAnimStart_false()
    {
        var reader = new FakeRecordReader();
        Light(reader);
        Assert.False(LightRows.Read(path => reader.Scalar(Id, path), _ => false).RandomAnimStart);
    }

    [Fact]
    public void A_header_flag_getter_that_throws_a_Radicle_error_reads_RandomAnimStart_as_false_and_the_rows_still_read()
    {
        var reader = new FakeRecordReader();
        Light(reader).Scalars["LIGH\\Radius"] = "300";
        var row = LightRows.Read(path => reader.Scalar(Id, path), _ => throw new RadicleException(default, "bad flag"));
        Assert.False(row.RandomAnimStart);
        Assert.Equal(300u, row.Radius);
    }

    [Fact]
    public void Only_a_Radicle_error_is_swallowed()
    {
        Assert.Throws<InvalidOperationException>(() => LightRows.Read(_ => throw new InvalidOperationException("not a row failure"), _ => false));
    }
}
