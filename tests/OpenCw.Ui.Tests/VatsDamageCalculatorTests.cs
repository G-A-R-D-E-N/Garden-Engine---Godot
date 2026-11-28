using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VatsDamageCalculatorTests
{
    static readonly VatsBodyPart Head = new("Head", "Head1", "Head", 1, 30, 2f, 20, VatsPartFlags.None);
    static readonly VatsBodyPart Torso = new("Torso", "Torso", "Chest", 0, 60, 1f, 75, VatsPartFlags.None);

    static VatsBodyPart Part(float damageMult) => new("Part", "Part", "Part", 0, 50, damageMult, 50, VatsPartFlags.None);

    [Fact]
    public void PartMultiplierScalesTheBaseDamage()
    {
        var result = VatsDamageCalculator.Compute(100.0, Head);

        Assert.Equal(new VatsDamageBreakdown(100.0, 200.0, 0.0, 200.0), result);
    }

    [Fact]
    public void UnitPartMultiplierLeavesTheBaseDamage()
    {
        var result = VatsDamageCalculator.Compute(50.0, Torso);

        Assert.Equal(new VatsDamageBreakdown(50.0, 50.0, 0.0, 50.0), result);
    }

    [Fact]
    public void MissingPartUsesOnlyTheExplicitPartMultiplier()
    {
        var result = VatsDamageCalculator.Compute(100.0, null, new VatsShotModifiers(PartMultiplier: 1.5));

        Assert.Equal(new VatsDamageBreakdown(100.0, 150.0, 0.0, 150.0), result);
    }

    [Fact]
    public void ExplicitPartMultiplierStacksOnThePartMultiplier()
    {
        var result = VatsDamageCalculator.Compute(100.0, Head, new VatsShotModifiers(PartMultiplier: 1.5));

        Assert.Equal(300.0, result.PartDamage);
        Assert.Equal(300.0, result.FinalDamage);
    }

    [Fact]
    public void DefaultModifiersLeaveTheShotUnchanged()
    {
        var result = VatsDamageCalculator.Compute(100.0, Torso, new VatsShotModifiers());

        Assert.Equal(new VatsDamageBreakdown(100.0, 100.0, 0.0, 100.0), result);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidPartDamageMultiplierFallsBackToOne(float damageMult)
    {
        var result = VatsDamageCalculator.Compute(100.0, Part(damageMult));

        Assert.Equal(100.0, result.PartDamage);
    }

    [Theory]
    [InlineData(2f, 2.0)]
    [InlineData(1f, 1.0)]
    [InlineData(0f, 1.0)]
    [InlineData(-3f, 1.0)]
    [InlineData(float.NaN, 1.0)]
    [InlineData(float.PositiveInfinity, 1.0)]
    public void EffectivePartMultiplierKeepsOnlyFinitePositiveValues(float input, double expected)
    {
        Assert.Equal(expected, VatsDamageCalculator.EffectivePartMultiplier(input));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidExplicitPartMultiplierFallsBackToOne(double partMultiplier)
    {
        var result = VatsDamageCalculator.Compute(100.0, null, new VatsShotModifiers(PartMultiplier: partMultiplier));

        Assert.Equal(100.0, result.PartDamage);
    }

    [Fact]
    public void CriticalDoublesByDefaultAndAddsTheFlatBonus()
    {
        var result = VatsDamageCalculator.Compute(100.0, Torso, new VatsShotModifiers(IsCritical: true, BonusCriticalDamage: 25.0));

        Assert.Equal(new VatsDamageBreakdown(100.0, 100.0, 125.0, 225.0), result);
    }

    [Fact]
    public void CriticalMultiplierScalesTheCriticalBonus()
    {
        var result = VatsDamageCalculator.Compute(100.0, Torso, new VatsShotModifiers(IsCritical: true, CriticalMultiplier: 1.5));

        Assert.Equal(new VatsDamageBreakdown(100.0, 100.0, 50.0, 150.0), result);
    }

    [Fact]
    public void UnitCriticalMultiplierAllowsFlatOnlyCriticalBonus()
    {
        var result = VatsDamageCalculator.Compute(100.0, Torso,
            new VatsShotModifiers(IsCritical: true, CriticalMultiplier: 1.0, BonusCriticalDamage: 25.0));

        Assert.Equal(new VatsDamageBreakdown(100.0, 100.0, 25.0, 125.0), result);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(-2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CriticalMultiplierBelowOneFallsBackToTwo(double criticalMultiplier)
    {
        var result = VatsDamageCalculator.Compute(100.0, Torso, new VatsShotModifiers(IsCritical: true, CriticalMultiplier: criticalMultiplier));

        Assert.Equal(100.0, result.CriticalBonusDamage);
        Assert.Equal(200.0, result.FinalDamage);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(-10.0, 1.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(100.0, 0.0)]
    public void ZeroOrZeroedShotDealsNoBonusCriticalDamage(double baseDamage, double bonusMultiplier)
    {
        var result = VatsDamageCalculator.Compute(baseDamage, Torso,
            new VatsShotModifiers(IsCritical: true, BonusCriticalDamage: 25.0, BonusMultiplier: bonusMultiplier));

        Assert.Equal(0.0, result.PartDamage);
        Assert.Equal(0.0, result.CriticalBonusDamage);
        Assert.Equal(0.0, result.FinalDamage);
    }

    [Theory]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidBonusCriticalDamageIsIgnored(double bonus)
    {
        var result = VatsDamageCalculator.Compute(100.0, Torso, new VatsShotModifiers(IsCritical: true, BonusCriticalDamage: bonus));

        Assert.Equal(100.0, result.CriticalBonusDamage);
        Assert.Equal(200.0, result.FinalDamage);
    }

    [Fact]
    public void NoCriticalLeavesNoCriticalBonus()
    {
        var result = VatsDamageCalculator.Compute(100.0, Torso, new VatsShotModifiers(IsCritical: false, BonusCriticalDamage: 50.0));

        Assert.Equal(0.0, result.CriticalBonusDamage);
        Assert.Equal(100.0, result.FinalDamage);
    }

    [Fact]
    public void SneakMultiplierAppliesOnlyForSneakAttacks()
    {
        var sneak = VatsDamageCalculator.Compute(100.0, Torso, new VatsShotModifiers(IsSneakAttack: true, SneakMultiplier: 2.5));
        var open = VatsDamageCalculator.Compute(100.0, Torso, new VatsShotModifiers(IsSneakAttack: false, SneakMultiplier: 2.5));

        Assert.Equal(250.0, sneak.PartDamage);
        Assert.Equal(100.0, open.PartDamage);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSneakMultiplierIsIgnored(double sneakMultiplier)
    {
        var result = VatsDamageCalculator.Compute(100.0, Torso, new VatsShotModifiers(IsSneakAttack: true, SneakMultiplier: sneakMultiplier));

        Assert.Equal(100.0, result.PartDamage);
    }

    [Fact]
    public void BonusMultiplierScalesTheShot()
    {
        var result = VatsDamageCalculator.Compute(100.0, Torso, new VatsShotModifiers(BonusMultiplier: 1.5));

        Assert.Equal(150.0, result.PartDamage);
        Assert.Equal(150.0, result.FinalDamage);
    }

    [Fact]
    public void ZeroBonusMultiplierZeroesTheShot()
    {
        var result = VatsDamageCalculator.Compute(100.0, Torso, new VatsShotModifiers(BonusMultiplier: 0.0));

        Assert.Equal(new VatsDamageBreakdown(100.0, 0.0, 0.0, 0.0), result);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidBonusMultiplierIsIgnored(double bonusMultiplier)
    {
        var result = VatsDamageCalculator.Compute(100.0, Torso, new VatsShotModifiers(BonusMultiplier: bonusMultiplier));

        Assert.Equal(100.0, result.PartDamage);
    }

    [Theory]
    [InlineData(-10.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidBaseDamageBecomesZero(double baseDamage)
    {
        var result = VatsDamageCalculator.Compute(baseDamage, Head, new VatsShotModifiers(IsCritical: true));

        Assert.Equal(new VatsDamageBreakdown(0.0, 0.0, 0.0, 0.0), result);
    }

    [Fact]
    public void ZeroBaseDamageStaysZero()
    {
        var result = VatsDamageCalculator.Compute(0.0, Head);

        Assert.Equal(new VatsDamageBreakdown(0.0, 0.0, 0.0, 0.0), result);
    }

    [Fact]
    public void PartSneakBonusAndCriticalCompose()
    {
        var modifiers = new VatsShotModifiers(
            PartMultiplier: 1.5, IsCritical: true, CriticalMultiplier: 2.0, BonusCriticalDamage: 10.0,
            IsSneakAttack: true, SneakMultiplier: 2.0, BonusMultiplier: 1.0);

        var result = VatsDamageCalculator.Compute(100.0, Head, modifiers);

        Assert.Equal(new VatsDamageBreakdown(100.0, 600.0, 610.0, 1210.0), result);
    }

    [Fact]
    public void OverflowingDamageStaysFiniteAndNonNegative()
    {
        var result = VatsDamageCalculator.Compute(
            double.MaxValue, Head, new VatsShotModifiers(IsCritical: true, BonusCriticalDamage: double.MaxValue));

        Assert.True(double.IsFinite(result.BaseDamage));
        Assert.True(double.IsFinite(result.PartDamage));
        Assert.True(double.IsFinite(result.CriticalBonusDamage));
        Assert.True(double.IsFinite(result.FinalDamage));
        Assert.True(result.PartDamage >= 0.0);
        Assert.True(result.CriticalBonusDamage >= 0.0);
        Assert.True(result.FinalDamage >= result.PartDamage);
    }
}

public class VatsCritMeterTests
{
    [Fact]
    public void PartialChargeAccumulatesWithoutBanking()
    {
        var meter = new VatsCritMeter();

        Assert.Equal(0, meter.RecordHit(40.0));
        Assert.Equal(40.0, meter.ChargeIntoNext);
        Assert.Equal(0.4, meter.Fraction, 12);
        Assert.False(meter.HasCrit);
    }

    [Fact]
    public void AFullThresholdBanksOneCritical()
    {
        var meter = new VatsCritMeter();

        Assert.Equal(1, meter.RecordHit(100.0));
        Assert.Equal(1, meter.BankedCrits);
        Assert.Equal(0.0, meter.ChargeIntoNext);
        Assert.Equal(1.0, meter.Fraction, 12);
        Assert.True(meter.HasCrit);
    }

    [Fact]
    public void OvershootIsCarriedUntilTheBankIsFull()
    {
        var meter = new VatsCritMeter(maxBanked: 3);

        Assert.Equal(2, meter.RecordHit(250.0));
        Assert.Equal(2, meter.BankedCrits);
        Assert.Equal(50.0, meter.ChargeIntoNext);
    }

    [Fact]
    public void BankIsCappedAndOvershootIsDroppedOnceFull()
    {
        var meter = new VatsCritMeter(maxBanked: 2);

        Assert.Equal(2, meter.RecordHit(350.0));
        Assert.Equal(2, meter.BankedCrits);
        Assert.Equal(0.0, meter.ChargeIntoNext);

        Assert.Equal(0, meter.RecordHit(500.0));
        Assert.Equal(2, meter.BankedCrits);
        Assert.Equal(0.0, meter.ChargeIntoNext);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidHitsAddNothing(double amount)
    {
        var meter = new VatsCritMeter();

        Assert.Equal(0, meter.RecordHit(amount));
        Assert.Equal(0.0, meter.ChargeIntoNext);
        Assert.Equal(0, meter.BankedCrits);
    }

    [Fact]
    public void SpendingACriticalDecrementsTheBank()
    {
        var meter = new VatsCritMeter();

        Assert.False(meter.TrySpendCrit());

        meter.RecordHit(100.0);

        Assert.True(meter.TrySpendCrit());
        Assert.Equal(0, meter.BankedCrits);
        Assert.False(meter.TrySpendCrit());
    }

    [Fact]
    public void FractionTracksChargeUntilTheBankIsFull()
    {
        var meter = new VatsCritMeter(maxBanked: 2);

        meter.RecordHit(50.0);
        Assert.Equal(0.5, meter.Fraction, 12);

        meter.RecordHit(50.0);
        Assert.Equal(1, meter.BankedCrits);
        Assert.Equal(0.0, meter.Fraction, 12);

        meter.RecordHit(50.0);
        Assert.Equal(0.5, meter.Fraction, 12);

        meter.RecordHit(50.0);
        Assert.Equal(2, meter.BankedCrits);
        Assert.Equal(1.0, meter.Fraction, 12);
    }

    [Fact]
    public void CaptureAndRestoreRoundTrip()
    {
        var meter = new VatsCritMeter(maxBanked: 2);
        meter.RecordHit(150.0);

        var state = meter.Capture();
        var restored = new VatsCritMeter(maxBanked: 2, initialState: state);

        Assert.Equal(state, restored.Capture());
        Assert.Equal(1, restored.BankedCrits);
        Assert.Equal(50.0, restored.ChargeIntoNext);
    }

    [Fact]
    public void RestoreClampsOutOfRangeState()
    {
        var meter = new VatsCritMeter(maxBanked: 2);

        meter.Restore(new VatsCritMeterState(99, double.NaN));
        Assert.Equal(2, meter.BankedCrits);
        Assert.Equal(0.0, meter.ChargeIntoNext);

        meter.Restore(new VatsCritMeterState(-3, -10.0));
        Assert.Equal(0, meter.BankedCrits);
        Assert.Equal(0.0, meter.ChargeIntoNext);

        meter.Restore(new VatsCritMeterState(0, 500.0));
        Assert.Equal(2, meter.BankedCrits);
        Assert.Equal(0.0, meter.ChargeIntoNext);
        Assert.Equal(1.0, meter.Fraction);
    }

    [Fact]
    public void ConstructorSanitisesThresholdAndBankSize()
    {
        Assert.Equal(VatsCritMeter.DefaultThreshold, new VatsCritMeter(threshold: 0.0).Threshold);
        Assert.Equal(VatsCritMeter.DefaultThreshold, new VatsCritMeter(threshold: double.NaN).Threshold);
        Assert.Equal(VatsCritMeter.AbsoluteMaxBanked, new VatsCritMeter(maxBanked: 99).MaxBanked);
        Assert.Equal(1, new VatsCritMeter(maxBanked: 0).MaxBanked);
        Assert.Equal(1, new VatsCritMeter(maxBanked: -4).MaxBanked);
        Assert.Equal(3, new VatsCritMeter(maxBanked: 3).MaxBanked);
    }

    [Fact]
    public void CustomThresholdControlsBanking()
    {
        var meter = new VatsCritMeter(threshold: 40.0, maxBanked: 2);

        Assert.Equal(1, meter.RecordHit(45.0));
        Assert.Equal(1, meter.BankedCrits);
        Assert.Equal(5.0, meter.ChargeIntoNext);
    }
}
