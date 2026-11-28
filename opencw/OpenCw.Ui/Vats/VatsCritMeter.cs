namespace OpenCommonwealth.Services.Ui.Vats;

public readonly record struct VatsCritMeterState(int BankedCrits, double ChargeIntoNext);

public sealed class VatsCritMeter
{
    public const double DefaultThreshold = 100.0;
    public const int DefaultMaxBanked = 1;
    public const int AbsoluteMaxBanked = 4;

    public VatsCritMeter(double threshold = DefaultThreshold, int maxBanked = DefaultMaxBanked, VatsCritMeterState? initialState = null)
    {
        Threshold = double.IsFinite(threshold) && threshold > 0.0 ? threshold : DefaultThreshold;
        MaxBanked = Math.Clamp(maxBanked, 1, AbsoluteMaxBanked);
        if (initialState is { } state)
            Restore(state);
    }

    public double Threshold { get; }

    public int MaxBanked { get; }

    public int BankedCrits { get; private set; }

    public double ChargeIntoNext { get; private set; }

    public double Fraction => BankedCrits >= MaxBanked ? 1.0 : ChargeIntoNext / Threshold;

    public bool HasCrit => BankedCrits > 0;

    public int RecordHit(double amount)
    {
        if (BankedCrits >= MaxBanked)
        {
            ChargeIntoNext = 0.0;
            return 0;
        }
        if (!double.IsFinite(amount) || amount <= 0.0)
            return 0;
        return AddCharge(amount);
    }

    public bool TrySpendCrit()
    {
        if (BankedCrits <= 0)
            return false;
        BankedCrits--;
        return true;
    }

    public VatsCritMeterState Capture() => new(BankedCrits, ChargeIntoNext);

    public void Restore(VatsCritMeterState state)
    {
        BankedCrits = Math.Clamp(state.BankedCrits, 0, MaxBanked);
        ChargeIntoNext = 0.0;
        if (BankedCrits >= MaxBanked)
            return;
        if (double.IsFinite(state.ChargeIntoNext) && state.ChargeIntoNext > 0.0)
            AddCharge(state.ChargeIntoNext);
    }

    private int AddCharge(double amount)
    {
        ChargeIntoNext += amount;
        var gained = 0;
        while (ChargeIntoNext >= Threshold && BankedCrits < MaxBanked)
        {
            ChargeIntoNext -= Threshold;
            BankedCrits++;
            gained++;
        }
        if (BankedCrits >= MaxBanked)
            ChargeIntoNext = 0.0;
        return gained;
    }
}
