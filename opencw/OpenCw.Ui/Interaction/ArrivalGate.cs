namespace OpenCommonwealth.Services.Interaction;

public enum ArrivalDecision { Wait, Place, GiveUp }

public static class ArrivalGate
{
    public static ArrivalDecision Decide(bool cellResident, bool groundFound, int waitedFrames, int maxFrames, int residentFrames, int groundFrames) =>
        cellResident && groundFound ? ArrivalDecision.Place
        : waitedFrames >= maxFrames || (cellResident && residentFrames >= groundFrames) ? ArrivalDecision.GiveUp : ArrivalDecision.Wait;

    public static bool ShouldReleasePlayer(ArrivalDecision decision, bool cellResident, bool landingGround, bool anchorGround) =>
        decision != ArrivalDecision.Wait && cellResident && (landingGround || anchorGround);
}
