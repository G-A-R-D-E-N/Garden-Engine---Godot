namespace OpenCommonwealth.Services.Ui.Terminals;

public static class HackingRules
{
    public const int MaxWordLength = 12;
    public const int MaxWords = 20;
    public const int MinWords = 6;
    public const int BaseGuesses = 4;

    public static int WordLength(int hackLevel, int refIdBit) =>
        Math.Min(MaxWordLength, hackLevel * 2 + 4 + (refIdBit & 1));

    public static int WordCount(float intelligence, int minWords = MinWords, int maxWords = MaxWords)
    {
        int max = Math.Min(maxWords, MaxWords);
        int min = Math.Min(minWords, max);
        float x = Math.Clamp(intelligence, 1f, 10f);
        float f = (1f - (x - 1f) * 0.1111111f) * (max - min);
        int whole = (int)f;
        return min + whole + (f - whole >= 0.5f ? 1 : 0);
    }

    public static int Guesses(float adjustedBase = BaseGuesses) => Math.Clamp((int)adjustedBase, 1, 5);
}
