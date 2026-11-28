namespace OpenCommonwealth.Services.Ui.Streaming;

public enum FallbackDecision { Discard, Defer, Retry }

public static class FallbackBudget
{
    public const int PerPump = 1;

    public static bool AttemptFailedThreadedSync(bool synchronousFallback) => synchronousFallback;

    public static FallbackDecision Decide(bool current, bool sameEpoch, bool wanted, int used) =>
        !current || !sameEpoch || !wanted ? FallbackDecision.Discard :
        used >= PerPump ? FallbackDecision.Defer : FallbackDecision.Retry;
}
