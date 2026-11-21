using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VatsChanceTests
{
    static readonly VatsChanceSettings Esm = new(
        NonActorBodyTerm: 50f, WeaponPerceptionOffset: 0.05f, BodyPartMultiplier: 0.075f, PerceptionMultiplier: 0.004f,
        WeaponDistanceMultiplier: 1.25f, DistanceTermMin: 0.05f, DistanceOffset: 0.05f, DistanceMultiplier: 3f, WeaponBaseMultiplier: 0.03f);

    static readonly VatsChanceSettings Exe = new();

    static VatsChanceInput In(float distance, float range, float? cone, float accuracy, float perception, int arms, float visibility, byte? toHit, int type = 8) =>
        new(distance, type, range, cone, accuracy, perception, arms, visibility, toHit);

    static void Near(double expected, float? actual)
    {
        Assert.NotNull(actual);
        Assert.InRange((double)actual.Value, expected - 1e-6, expected + 1e-6);
    }

    [Fact]
    public void GmstRecordsOverrideTheExeDefaults()
    {
        var reader = new FakeRecordReader();
        Gmst(reader, "000001:Fallout4.esm", "fVATSChanceNonActorBodyTerm", "50");
        Gmst(reader, "000002:Fallout4.esm", "fVATSChanceWeaponPerceptionOffset", "0.05");
        Gmst(reader, "000003:Fallout4.esm", "fVATSChanceBodyPartMultiplier", "0.075");
        Gmst(reader, "000004:Fallout4.esm", "fVATSChancePerceptionMultiplier", "0.004");
        Gmst(reader, "000005:Fallout4.esm", "fVATSChanceWeaponDistanceMultiplier", "1.25");
        Gmst(reader, "000006:Fallout4.esm", "fVATSChanceDistanceTermMin", "0.05");
        Gmst(reader, "000007:Fallout4.esm", "fVATSChanceDistanceOffset", "0.05");
        Gmst(reader, "000008:Fallout4.esm", "fVATSChanceDistanceMultiplier", "3");
        Gmst(reader, "000009:Fallout4.esm", "fVATSChanceWeaponBaseMultiplier", "0.03");

        Assert.Equal(Esm, VatsChanceSettings.Read(reader));
    }

    [Fact]
    public void MissingOrUnreadableGmstKeepsTheDefault()
    {
        var reader = new FakeRecordReader();
        Gmst(reader, "000001:Fallout4.esm", "fVATSChanceBodyPartMultiplier", "not a number");
        Gmst(reader, "000002:Fallout4.esm", "fVATSChanceOffset", "NaN");

        Assert.Equal(Exe, VatsChanceSettings.Read(reader));
    }

    [Fact]
    public void DefaultsAreTheExeStaticValues()
    {
        Assert.Equal(0.8f, Exe.PartVisibilityMax);
        Assert.Equal(100f, Exe.NonActorBodyTerm);
        Assert.Equal(0.2f, Exe.ArmConditionBase);
        Assert.Equal(0.8f, Exe.ArmConditionMult);
        Assert.Equal(0.007f, Exe.WeaponBaseMultiplier);
        Assert.Equal(0.035f, Exe.PerceptionMultiplier);
        Assert.Equal(0.031f, Exe.BodyPartMultiplier);
        Assert.Equal(0.001f, Exe.DistanceOffset);
        Assert.Equal(300f, Exe.ConeDistanceOffset);
        Assert.Equal(400f, Exe.ConeDistanceMult);
        Assert.Equal(20f, Exe.ConeDistanceMaxDegrees);
        Assert.Equal(0.8f, Exe.ConeDistanceBonus);
    }

    [Fact]
    public void BaselineMatchesTheTranscribedFormula()
    {
        Near(0.7409250140190125, VatsChance.Fraction(Esm, In(500, 1000, null, 0.5f, 6, 0, 0.8f, 60)));
        Near(0.3056444823741913, VatsChance.Fraction(Exe, In(300, 800, null, 20, 5, 0, 0.8f, 50)));
    }

    [Fact]
    public void PartToHitScalesTheChanceAndNonActorsUseTheSetting()
    {
        Near(0.3704625070095062, VatsChance.Fraction(Esm, In(500, 1000, null, 0.5f, 6, 0, 0.8f, 30)));
        Near(0.6174376010894775, VatsChance.Fraction(Esm, In(500, 1000, null, 0.5f, 6, 0, 0.8f, null)));
    }

    [Fact]
    public void EachCrippledArmHalvesTheArmFactorDownToTheBase()
    {
        Near(0.44455504417419434, VatsChance.Fraction(Esm, In(500, 1000, null, 0.5f, 6, 1, 0.8f, 60)));
        Near(0.14818501472473145, VatsChance.Fraction(Esm, In(500, 1000, null, 0.5f, 6, 2, 0.8f, 60)));
        Near(0.14818501472473145, VatsChance.Fraction(Esm, In(500, 1000, null, 0.5f, 6, 5, 0.8f, 60)));
    }

    [Fact]
    public void DistanceBeyondRangeFloorsAtTheDistanceTermMinimum()
    {
        Near(0.08009999990463257, VatsChance.Fraction(Esm, In(2000, 1000, null, 0.5f, 6, 0, 0.8f, 60)));
    }

    [Fact]
    public void VisibilityIsScaledByTheMaximumAndClamped()
    {
        Near(0.3704625070095062, VatsChance.Fraction(Esm, In(500, 1000, null, 0.5f, 6, 0, 0.4f, 60)));
        Near(0.7409250140190125, VatsChance.Fraction(Esm, In(500, 1000, null, 0.5f, 6, 0, 2f, 60)));
        Near(0, VatsChance.Fraction(Esm, In(500, 1000, null, 0.5f, 6, 0, 0f, 60)));
    }

    [Fact]
    public void AimModelConeAddsADistanceBonusThatFadesWithDistance()
    {
        Near(0.3270750343799591, VatsChance.Fraction(Esm, In(250, 1000, null, 0.5f, 6, 0, 0.8f, 20)));
        Near(0.48727503418922424, VatsChance.Fraction(Esm, In(250, 1000, 10, 0.5f, 6, 0, 0.8f, 20)));
        Near(0.40329447388648987, VatsChance.Fraction(Exe, In(300, 800, 15, 20, 5, 0, 0.8f, 50)));
    }

    [Fact]
    public void NaNOrZeroConeMaximumDegreesDegradesSafely()
    {
        var plain = VatsChance.Fraction(Esm, In(250, 1000, null, 0.5f, 6, 0, 0.8f, 20));

        Assert.Equal(plain, VatsChance.Fraction(Esm with { ConeDistanceMaxDegrees = float.NaN }, In(250, 1000, 10, 0.5f, 6, 0, 0.8f, 20)));
        Assert.Equal(plain, VatsChance.Fraction(Esm with { ConeDistanceMaxDegrees = 0f }, In(250, 1000, 0, 0.5f, 6, 0, 0.8f, 20)));
        var saturated = VatsChance.Fraction(Esm with { ConeDistanceMaxDegrees = 0f }, In(250, 1000, 10, 0.5f, 6, 0, 0.8f, 20));
        Assert.NotNull(saturated);
        Assert.InRange(saturated.Value, 0f, 1f);
        Assert.True(saturated > plain);
    }

    [Fact]
    public void NaNSettingsGiveZeroNotNaN()
    {
        Assert.Equal(0f, VatsChance.Fraction(Esm with { BodyPartMultiplier = float.NaN }, In(250, 1000, null, 0.5f, 6, 0, 0.8f, 20)));
    }

    [Fact]
    public void ResultIsZeroBelowTheFloorAndOneAboveTheCeiling()
    {
        Near(0, VatsChance.Fraction(Esm, In(1250, 1000, null, 0, 1, 0, 0.8f, 1)));
        Near(0, VatsChance.Fraction(Esm, In(500, 1000, null, 0.5f, 6, 0, 0.8f, 0)));
        Near(1, VatsChance.Fraction(Esm, In(0, 1000, null, 10, 10, 0, 0.8f, 100)));
    }

    [Fact]
    public void InfiniteDistanceReadsAsZero()
    {
        Assert.Equal(
            VatsChance.Fraction(Esm, In(0, 1000, null, 0.5f, 6, 0, 0.8f, 60)),
            VatsChance.Fraction(Esm, In(float.PositiveInfinity, 1000, null, 0.5f, 6, 0, 0.8f, 60)));
    }

    [Fact]
    public void ZeroRangeOrZeroMaximumVisibilityTakeTheNeutralBranch()
    {
        var noRange = VatsChance.Fraction(Esm, In(500, 0, null, 0.5f, 6, 0, 0.8f, 60));
        var inRangeAtZeroDistance = VatsChance.Fraction(Esm, In(0, 1000, null, 0.5f, 6, 0, 0.8f, 60));
        Assert.Equal(inRangeAtZeroDistance, noRange);

        var noMax = VatsChance.Fraction(Esm with { PartVisibilityMax = 0f }, In(500, 1000, null, 0.5f, 6, 0, 0f, 60));
        var full = VatsChance.Fraction(Esm, In(500, 1000, null, 0.5f, 6, 0, 0.8f, 60));
        Assert.Equal(full, noMax);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(6)]
    public void MeleeWeaponTypesHaveNoRangedChance(int type)
    {
        Assert.Null(VatsChance.Fraction(Esm, In(100, 1000, null, 0.5f, 6, 0, 0.8f, 60, type)));
    }

    [Fact]
    public void FirstRangedWeaponTypeIsComputed()
    {
        Assert.NotNull(VatsChance.Fraction(Esm, In(100, 1000, null, 0.5f, 6, 0, 0.8f, 60, VatsChance.FirstRangedWeaponType)));
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.7409250140190125f, 74)]
    [InlineData(0.745f, 75)]
    [InlineData(1f, 100)]
    public void PercentRoundsTheFraction(float fraction, int percent)
    {
        Assert.Equal(percent, VatsChance.Percent(fraction));
    }

    static void Gmst(FakeRecordReader reader, string id, string editorId, string value) =>
        reader.Add(id, "GMST", editorId).Scalars[VatsChanceSettings.FloatPath] = value;
}
