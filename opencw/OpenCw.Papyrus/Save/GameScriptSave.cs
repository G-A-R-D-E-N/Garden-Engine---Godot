using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Save;

public sealed record ScriptLoadOutcome(string? Failure, IReadOnlyList<string> Warnings)
{
    public bool Ok => Failure is null;

    public static ScriptLoadOutcome Fine { get; } = new(null, Array.Empty<string>());
}

public static class GameScriptSave
{
    public static readonly IReadOnlyList<string> SectionNames = new[] { QuestStateSave.SectionName, QuestScriptSave.SectionName, PapyrusStateSave.SectionName };

    // Requires: a null session with no failure means there is no Papyrus runtime and no sections are written; a failed session throws so the caller refuses the write.
    public static IReadOnlyDictionary<string, byte[]> Capture(GameSession? session, string? sessionFailure)
    {
        if (session is null)
            return sessionFailure is null
                ? new Dictionary<string, byte[]>()
                : throw new InvalidOperationException($"the script session failed to start: {sessionFailure}");
        if (session.Host is RadicleScriptHost { PendingLatentWaits: > 0 } waiting)
            throw new InvalidOperationException($"{waiting.PendingLatentWaits} script call(s) are waiting on a latent native and cannot be saved");
        var sections = QuestStateSave.Sections(session.QuestStages).ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (name, payload) in QuestScriptSave.Sections(session.Quests, session.Host)) sections[name] = payload;
        foreach (var (name, payload) in PapyrusStateSave.Sections(session.Host)) sections[name] = payload;
        return sections;
    }

    public static ScriptLoadOutcome Restore(IReadOnlyDictionary<string, byte[]> sections, GameSession? session)
    {
        if (!SectionNames.Any(sections.ContainsKey))
        {
            if (session is not null) SessionRestore.Apply(session, null, null, null);
            return ScriptLoadOutcome.Fine;
        }
        try
        {
            var stages = sections.ContainsKey(QuestStateSave.SectionName) ? QuestStateSave.Read(sections) : null;
            var scripts = sections.ContainsKey(QuestScriptSave.SectionName) ? QuestScriptSave.Read(sections) : null;
            var instances = PapyrusStateSave.Read(sections);
            if (session is null) return new ScriptLoadOutcome("the save holds quest and script state but the script session is not running", Array.Empty<string>());
            var restored = SessionRestore.Apply(session, instances, scripts, stages);
            if (restored.Instances?.Refused is { } refused)
                return new ScriptLoadOutcome($"the saved script instances were refused: {refused}", Array.Empty<string>());
            var warnings = new List<string>();
            if (restored.Scripts?.ToLine() is { } line && restored.Scripts.DroppedQuests + restored.Scripts.DroppedScripts + restored.Scripts.DroppedProperties > 0)
                warnings.Add(line);
            if (restored.DroppedQuestStages > 0)
                warnings.Add($"dropped {restored.DroppedQuestStages} saved quest(s) not in this plugin set");
            return new ScriptLoadOutcome(null, warnings);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            var rollback = ex.Data[SessionRestore.RollbackFailureKey] is string failed ? $" (restoring the earlier instance state also failed: {failed})" : "";
            if (ex.Data[QuestScriptSave.UndoFailureKey] is string undo) rollback += $" (script undo failed: {undo})";
            if (ex.Data[SessionRestore.RollbackUnavailableKey] is string unavailable) rollback += $" (earlier state unavailable: {unavailable})";
            return new ScriptLoadOutcome($"quest section: {ex.Message}{rollback}", Array.Empty<string>());
        }
    }
}
