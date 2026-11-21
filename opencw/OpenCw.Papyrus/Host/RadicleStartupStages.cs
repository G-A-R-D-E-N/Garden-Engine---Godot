using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Quest;
using StageInput = Radicle.Interop.QuestStageInput;
using StageSet = Radicle.Interop.QuestStageSet;

namespace OpenCommonwealth.Services.Papyrus.Host;

public sealed record StartupStageIdentification(IReadOnlyDictionary<string, int> Stages, int Unresolved, int Refused, string Report);

public static class RadicleStartupStages
{
    public static StartupStageIdentification Identify(IEnumerable<QuestStartupInput> quests)
    {
        var sets = quests.Where(input => input.Stages is not null)
            .Select(input => new StageSet(input.Quest, input.Stages!
                .Select(stage => new StageInput(stage.Index, stage.IsStartup, stage.Fragments)).ToArray())).ToArray();
        if (sets.Length == 0) return new StartupStageIdentification(new Dictionary<string, int>(), 0, 0, string.Empty);
        var report = Radicle.Interop.PapyrusQuestLifecycle.IdentifyStartupStages(sets);
        var identified = report.Outcomes
            .Where(outcome => outcome.State == Radicle.Interop.StartupStageState.Identified && outcome.Stage is not null)
            .ToDictionary(outcome => outcome.Quest, outcome => outcome.Stage!.Value, StringComparer.Ordinal);
        return new StartupStageIdentification(identified, report.Unresolved, report.Refused, report.Report);
    }
}
