using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed record QuestStageLog(int Stage, bool Done, string? Text);

public sealed record QuestSnapshot(string Id, string? Name, bool Completed, int Stage, IReadOnlyList<QuestStageLog> Logs);

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
            ? new Quest(snapshot.Id, snapshot.Name, Group, text, System.Array.Empty<QuestObjective>(), snapshot.Completed, snapshot.Stage)
            : null;
}
