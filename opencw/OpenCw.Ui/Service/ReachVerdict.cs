namespace OpenCommonwealth;

public static class ReachVerdict
{
    public const string Reached = "REACHED";
    public const string NoGoal = "NO GOAL";

    public static string? LegFailure(string result, int stalls) =>
        result != Reached ? result : stalls > 0 ? $"{Reached} only by teleporting past {stalls} stall(s)" : null;

    public static int ExitCode(bool compiled, int failures) => !compiled ? 2 : failures == 0 ? 0 : 1;
}
