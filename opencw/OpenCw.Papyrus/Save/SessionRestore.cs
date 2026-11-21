using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Save;

public sealed record SessionRestoreResult(PapyrusStateRestoreReport? Instances, QuestScriptRestoreReport? Scripts, int DroppedQuestStages);

public static class SessionRestore
{
    public const string RollbackFailureKey = "rollback";
    public const string RollbackUnavailableKey = "rollbackUnavailable";

    // Requires: every snapshot was already read and validated; a section that is absent is null. A throw after any instance or scalar write puts the pre-load instance state back; a failed put-back is recorded in Exception.Data[RollbackFailureKey] and the original exception is rethrown.
    public static SessionRestoreResult Apply(GameSession session, PapyrusStateSnapshot? instances, QuestScriptSnapshot? scripts, QuestStateSnapshot? stages)
    {
        string? unavailable = null;
        var before = instances is not null || scripts is not null ? Capture(session.Host, out unavailable) : null;
        try
        {
            var instanceReport = instances is null ? null : PapyrusStateSave.Restore(instances, session.Host);
            var scriptReport = scripts is null ? null : QuestScriptSave.Restore(scripts, session.Quests, session.Host);
            var dropped = stages is null ? 0 : session.QuestStages.Restore(stages);
            if (stages is not null)
                session.StartGameQuests(stages.Quests.Where(entry => entry.State is not null).Select(entry => entry.Quest).ToHashSet(StringComparer.Ordinal));
            return new SessionRestoreResult(instanceReport, scriptReport, dropped);
        }
        catch (Exception error)
        {
            if (before is null && unavailable is not null) error.Data[RollbackUnavailableKey] = unavailable;
            if (before is null && instances is not null) (session.Host as RadicleScriptHost)?.DropRetainedState();
            if (before is not null)
            {
                try
                {
                    var put = PapyrusStateSave.Restore(before, session.Host);
                    if (put.Refused is not null) error.Data[RollbackFailureKey] = put.Refused;
                }
                catch (Exception rollback)
                {
                    error.Data[RollbackFailureKey] = rollback.Message;
                }
            }
            throw;
        }
    }

    private static PapyrusStateSnapshot? Capture(IScriptHost host, out string? unavailable)
    {
        unavailable = null;
        if (host is not RadicleScriptHost radicle) return null;
        try
        {
            var capture = radicle.CaptureState();
            return new PapyrusStateSnapshot(capture.Blob, capture.Digest, capture.Engine);
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException)
        {
            unavailable = $"the earlier instance state could not be captured: {error.Message}";
            return null;
        }
    }
}
