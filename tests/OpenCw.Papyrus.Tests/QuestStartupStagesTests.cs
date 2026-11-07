using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class QuestStartupStagesTests
{
    private static readonly (string Key, string Status)[] Rows =
    {
        (QuestStartupStages.IdentificationKey, "PROVEN"),
        (QuestStartupStages.FragmentTimingKey, "UNPROVEN"),
    };

    private static EvidenceGate Gate(params (string Key, string Status)[] overrides)
    {
        var rows = Rows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            rows[pair.Key] = pair.Status;
        }
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static QuestStages Quest(string name, params QuestStageInput[] stages) => new(name, stages);

    [Fact]
    public void AQuestNameWithAnEmbeddedNulIsRefusedAndTheOthersStillResolve()
    {
        var report = QuestStartupStages.Identify(new[]
        {
            Quest("000800:Quests.esp", new QuestStageInput(20, true, 0)),
            Quest("000900\u0000:Quests.esp", new QuestStageInput(20, true, 0)),
        }, Gate());

        Assert.Equal(2, report.Quests.Count);
        Assert.Equal(1, report.Identified);
        Assert.Equal(1, report.Refused);
        Assert.Equal(0, report.Unresolved);
        Assert.Contains("embedded NUL", report.Quests.Single(outcome => outcome.State == StageState.Unidentified).Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AQuestNameWithAnUnpairedSurrogateIsRefusedBeforeTheRadicleCall()
    {
        var report = QuestStartupStages.Identify(new[]
        {
            Quest("000800:Quests.esp", new QuestStageInput(20, true, 0)),
            Quest("0009\uD800:Quests.esp", new QuestStageInput(20, true, 0)),
        }, Gate());

        Assert.Equal(1, report.Identified);
        Assert.Equal(1, report.Refused);
        Assert.Equal(StageState.Unidentified, report.Quests.Single(outcome => outcome.Quest.Contains('\uD800')).State);
    }

    [Fact]
    public void OneMarkedStageIsIdentified()
    {
        var report = QuestStartupStages.Identify(new[]
        {
            Quest("000800:Quests.esp",
                new QuestStageInput(10, false, 0),
                new QuestStageInput(20, true, 0),
                new QuestStageInput(30, false, 0)),
        }, Gate());

        var outcome = Assert.Single(report.Quests);
        Assert.Equal(StageState.Identified, outcome.State);
        Assert.Equal(20, outcome.Stage);
        Assert.Equal(1, report.Identified);
    }

    [Fact]
    public void AQuestWithoutAMarkedStageIsNeverGuessed()
    {
        var report = QuestStartupStages.Identify(new[]
        {
            Quest("000800:Quests.esp", new QuestStageInput(10, false, 0), new QuestStageInput(20, false, 0)),
        }, Gate());

        var outcome = Assert.Single(report.Quests);
        Assert.Equal(StageState.Unidentified, outcome.State);
        Assert.Null(outcome.Stage);
        Assert.Equal(1, report.Unresolved);
        Assert.Equal(0, report.Identified);
    }

    [Fact]
    public void AQuestWithNoStagesIsUnidentified()
    {
        var report = QuestStartupStages.Identify(new[] { Quest("000800:Quests.esp") }, Gate());

        Assert.Equal(StageState.Unidentified, Assert.Single(report.Quests).State);
        Assert.Equal(1, report.Unresolved);
    }

    [Fact]
    public void TwoMarkedStagesAreAmbiguousAndNeverTiedBroken()
    {
        var report = QuestStartupStages.Identify(new[]
        {
            Quest("000800:Quests.esp", new QuestStageInput(10, true, 0), new QuestStageInput(20, true, 0)),
        }, Gate());

        var outcome = Assert.Single(report.Quests);
        Assert.Equal(StageState.Ambiguous, outcome.State);
        Assert.Null(outcome.Stage);
        Assert.Contains("never broken", outcome.Reason, StringComparison.Ordinal);
        Assert.Equal(1, report.Unresolved);
    }

    [Fact]
    public void AnIdentifiedStageWithUnprovenFragmentsStaysBlocked()
    {
        var report = QuestStartupStages.Identify(new[]
        {
            Quest("000800:Quests.esp", new QuestStageInput(10, true, 3)),
        }, Gate());

        var outcome = Assert.Single(report.Quests);
        Assert.Equal(StageState.BlockedByFragments, outcome.State);
        Assert.Equal(10, outcome.Stage);
        Assert.Equal(1, report.Refused);
        Assert.Equal(0, report.Identified);
    }

    [Fact]
    public void AnIdentifiedStageWithProvenFragmentsProgresses()
    {
        var report = QuestStartupStages.Identify(new[]
        {
            Quest("000800:Quests.esp", new QuestStageInput(10, true, 3)),
        }, Gate((QuestStartupStages.FragmentTimingKey, "PROVEN")));

        var outcome = Assert.Single(report.Quests);
        Assert.Equal(StageState.Identified, outcome.State);
        Assert.Equal(10, outcome.Stage);
        Assert.Equal(1, report.Identified);
    }

    [Fact]
    public void AnUnprovenIdentificationKeyChosesNothingAndTheReportIsDeterministic()
    {
        var quests = new List<QuestStages>
        {
            Quest("000900:Quests.esp", new QuestStageInput(5, true, 0)),
            Quest("000800:Quests.esp", new QuestStageInput(10, true, 0), new QuestStageInput(11, true, 0)),
        };

        var report = QuestStartupStages.Identify(quests, Gate((QuestStartupStages.IdentificationKey, "UNPROVEN")));
        Assert.Equal(2, report.Refused);
        Assert.All(report.Quests, outcome => Assert.Null(outcome.Stage));

        var shuffled = QuestStartupStages.Identify(quests.AsEnumerable().Reverse(), Gate());
        var ordered = QuestStartupStages.Identify(quests, Gate());
        Assert.Equal(ordered.ToReport(), shuffled.ToReport());
        Assert.Equal(ordered.Digest(), shuffled.Digest());
        Assert.Equal(ordered.Quests.Count, ordered.Identified + ordered.Refused + ordered.Unresolved);
    }
}
