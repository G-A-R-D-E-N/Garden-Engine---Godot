using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed record QuestStageLog(int Stage, bool Done, string? Text);

public sealed record QuestSnapshot(string Id, string? Name, bool Completed, int Stage, IReadOnlyList<QuestStageLog> Logs,
                                   IReadOnlyList<QuestObjective>? Objectives = null, bool Active = false);

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

    public static string? TrackedQuestId(IEnumerable<QuestSnapshot> snapshots) =>
        snapshots.FirstOrDefault(snapshot => !snapshot.Completed && snapshot.Active && Entry(snapshot) is not null)?.Id;

    public static QuestLogModel Build(IEnumerable<QuestSnapshot> snapshots)
    {
        var list = snapshots.ToList();
        var model = new QuestLogModel(Entries(list));
        if (TrackedQuestId(list) is { } tracked && model.Select("quest:" + tracked))
            model.TrackCurrent();
        return model;
    }

    private static Quest? Entry(QuestSnapshot snapshot) =>
        !string.IsNullOrWhiteSpace(snapshot.Name) && CurrentLog(snapshot.Stage, snapshot.Logs) is { } text
            ? new Quest(snapshot.Id, snapshot.Name, Group, text, ObjectivesOf(snapshot), snapshot.Completed, snapshot.Stage)
            : null;

    public static IReadOnlyList<QuestObjective> VisibleObjectives(QuestSnapshot snapshot)
    {
        if (snapshot.Objectives is not { Count: > 0 } objectives || !snapshot.Completed)
            return System.Array.Empty<QuestObjective>();
        return objectives.Select(o => o with { Completed = true }).ToArray();
    }

    private static IReadOnlyList<QuestObjective> ObjectivesOf(QuestSnapshot snapshot) => VisibleObjectives(snapshot);
}
