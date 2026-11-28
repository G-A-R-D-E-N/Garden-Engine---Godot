using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Save;

public sealed record SessionRestoreResult(PapyrusStateRestoreReport? Instances, QuestScriptRestoreReport? Scripts, int DroppedQuestStages, ScriptReferenceReport? References = null);

public sealed class SessionRestoreOutcome
{
    public PapyrusStateRestoreReport? Instances { get; internal set; }

    public QuestScriptRestoreReport? Scripts { get; internal set; }

    public int DroppedQuestStages { get; internal set; }

    public ScriptReferenceReport? References { get; internal set; }

    public SessionRestoreResult ToResult() => new(Instances, Scripts, DroppedQuestStages, References);
}

public static class SessionRestore
{
    public const string RollbackFailureKey = "rollback";
    public const string RollbackUnavailableKey = "rollbackUnavailable";
    public const string StageUndoFailureKey = "stageUndo";

    // Requires: every snapshot was already read and validated; a section that is absent is null. A throw after any instance or scalar write puts the pre-load instance state and the quest stages back; a failed put-back is recorded in Exception.Data and the original exception is rethrown.
    public static SessionRestoreResult Apply(GameSession session, PapyrusStateSnapshot? instances, QuestScriptSnapshot? scripts, QuestStateSnapshot? stages)
    {
        var transaction = new LoadTransaction();
        var outcome = Register(transaction, session, instances, scripts, stages);
        transaction.Run().ThrowFirst();
        return outcome.ToResult();
    }

    // Invariant: quest stages run before script state, so a saved reference or property that names a quest-owned instance finds it; a failed quests step undoes only the stages, a failed scripts step undoes the instance state and the stages.
    public static SessionRestoreOutcome Register(LoadTransaction transaction, GameSession session, PapyrusStateSnapshot? instances, QuestScriptSnapshot? scripts, QuestStateSnapshot? stages)
    {
        var outcome = new SessionRestoreOutcome();
        string? unavailable = null;
        var before = instances is not null || scripts is not null ? Capture(session.Host, out unavailable) : null;
        var stagesBefore = stages is null ? null : session.QuestStages.Capture();
        var stale = session.QuestStages.QueuedFragments;
        var stagesUndone = false;
        var scriptsUndone = false;

        void UndoStages(Exception error)
        {
            if (stagesUndone || stagesBefore is null) return;
            stagesUndone = true;
            try { session.QuestStages.Restore(stagesBefore); }
            catch (Exception stageUndo) { error.Data[StageUndoFailureKey] = stageUndo.Message; }
        }

        void UndoScripts(Exception error)
        {
            if (scriptsUndone) return;
            scriptsUndone = true;
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
            UndoStages(error);
        }

        if (stages is not null)
            transaction.Add(LoadStage.Quests, "quest stages", () =>
            {
                outcome.DroppedQuestStages = session.QuestStages.Restore(stages);
                session.StartGameQuests(stages.Quests.Where(entry => entry.State is not null).Select(entry => entry.Quest).ToHashSet(StringComparer.Ordinal));
            }, UndoStages);

        if (instances is not null)
            transaction.Add(LoadStage.Scripts, "papyrus instances", () =>
            {
                outcome.Instances = PapyrusStateSave.Restore(instances, session.Host);
                if (outcome.Instances.Refused is { } refused) transaction.Warn(LoadStage.Scripts, "papyrus instances", $"refused, instance state left as started: {refused}");
            }, UndoScripts, LoadStage.Quests);
        if (scripts is not null)
            transaction.Add(LoadStage.Scripts, "quest scripts", () => outcome.Scripts = QuestScriptSave.Restore(scripts, session.Quests, session.Host), UndoScripts, LoadStage.Quests, LoadStage.Scripts);
        if (instances is not null && session.Host is RadicleScriptHost radicle)
            transaction.Add(LoadStage.Scripts, "script references", () =>
            {
                var report = radicle.ResolveReferences();
                outcome.References = report;
                foreach (var name in report.Unresolved) transaction.Warn(LoadStage.Scripts, "script references", $"left unresolved: {name}");
            }, null, LoadStage.Quests, LoadStage.Scripts);
        if (stages is null)
            transaction.Add(LoadStage.Scripts, "queued fragments", () => session.QuestStages.DropFirstQueued(stale), null, LoadStage.Quests, LoadStage.Scripts);
        return outcome;
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
