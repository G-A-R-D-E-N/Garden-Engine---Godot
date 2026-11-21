using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed record QuestStageLog(int Stage, bool Done, string? Text);

public sealed record QuestObjectiveLog(int Index, bool Shown, string Text);

public sealed record QuestSnapshot(string Id, string? Name, bool Completed, int Stage, IReadOnlyList<QuestStageLog> Logs,
                                   IReadOnlyList<QuestObjectiveLog>? Objectives = null);

public static class RecordQuestLog
{
    public const string Group = "Quests";

    public static string? CurrentLog(int stage, IEnumerable<QuestStageLog> logs) =>
        logs.Where(log => !string.IsNullOrWhiteSpace(log.Text) && (log.Done || log.Stage == stage))
            .OrderByDescending(log => log.Stage)
            .Select(log => log.Text)
            .FirstOrDefault();

    public static IReadOnlyList<Quest> Entries(IEnumerable<QuestSnapshot> snapshots) =>
        snapshots.Select(Entry).OfType<Quest>().ToList();

    public static QuestLogModel Build(IEnumerable<QuestSnapshot> snapshots) => new(Entries(snapshots));

    private static Quest? Entry(QuestSnapshot snapshot) =>
        !string.IsNullOrWhiteSpace(snapshot.Name) && CurrentLog(snapshot.Stage, snapshot.Logs) is { } text
            ? new Quest(snapshot.Id, snapshot.Name, Group, text, ObjectivesOf(snapshot), snapshot.Completed, snapshot.Stage)
            : null;

    // Invariant: only shown objectives list, and one is completed when its quest is or a later objective is shown.
    public static IReadOnlyList<QuestObjective> ObjectivesOf(QuestSnapshot snapshot)
    {
        var shown = (snapshot.Objectives ?? System.Array.Empty<QuestObjectiveLog>())
            .Where(o => o.Shown && !string.IsNullOrWhiteSpace(o.Text)).OrderBy(o => o.Index).ToList();
        return shown.Select(o => new QuestObjective($"{snapshot.Id}:{o.Index}", o.Text, snapshot.Completed || shown.Any(later => later.Index > o.Index))).ToList();
    }
}
