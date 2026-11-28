using OpenCommonwealth.Services.Interaction;

namespace OpenCommonwealth.Services.Ui.Trade;

public enum PickpocketOffer
{
    NotOffered,
    AlreadyCaught,
    Open,
}

public sealed record PickpocketTarget(
    int LifeState,
    int KnockState,
    bool RaceAllows,
    bool AliasForbids,
    bool AngryWithPlayer,
    bool AlreadyCaught,
    bool IsMount,
    bool IsGhost = false,
    bool IsPlayerTeammate = false);

public static class PickpocketRules
{
    public const int DeadLifeState = 2;

    public static bool AllowsPickpocket(PickpocketTarget t) =>
        t.LifeState != DeadLifeState && t.RaceAllows && !t.AliasForbids;

    public static bool KnockedOut(PickpocketTarget t) =>
        NpcStates.KnockedOut(t.KnockState);

    public static PickpocketOffer Offer(PickpocketTarget t, bool playerSneaking)
    {
        if (!playerSneaking || t.IsMount || t.IsGhost || t.IsPlayerTeammate || !AllowsPickpocket(t)) return PickpocketOffer.NotOffered;
        return t.AngryWithPlayer || (t.AlreadyCaught && !KnockedOut(t)) ? PickpocketOffer.AlreadyCaught : PickpocketOffer.Open;
    }
}
