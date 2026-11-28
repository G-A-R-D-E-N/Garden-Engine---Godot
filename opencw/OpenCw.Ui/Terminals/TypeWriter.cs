namespace OpenCommonwealth.Services.Ui.Terminals;

public sealed class TypeWriter
{
    private double _carry;

    public TypeWriter(double charsPerSecond = 60)
    {
        if (!double.IsFinite(charsPerSecond) || charsPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(charsPerSecond));
        CharsPerSecond = charsPerSecond;
    }

    public double CharsPerSecond { get; }

    public int Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
            return 0;
        _carry += seconds * CharsPerSecond;
        int whole = (int)Math.Floor(_carry + 1e-9);
        _carry -= whole;
        return Math.Max(whole, 0);
    }

    public void Reset() => _carry = 0;
}
