namespace OpenCommonwealth.Services.Combat;

public static class DifficultyScale
{
    public static double DamageTaken(string? level) => level switch
    {
        "easy" => 0.5,
        "hard" => 1.5,
        "survival" => 2,
        _ => 1,
    };
}
