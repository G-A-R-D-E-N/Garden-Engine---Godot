using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using OpenCommonwealth.Services.Papyrus.Vmad;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Dialogue;

public enum EffectStatus
{
    Applied,
    Refused,
    Failed,
}

public sealed record EffectOutcome(string Effect, EffectStatus Status, string Detail)
{
    public override string ToString() => $"{Effect}: {Status} ({Detail})";
}

public interface IDialogueEffects
{
    IReadOnlyList<EffectOutcome> Run(DialogueInfoData info, bool end);
}

public sealed class QuestStageEffects(QuestStateMachine stages, Func<(int Ran, int Refused)> drain,
    Func<string, InfoVmadParse, InfoFragmentRow, (bool Completed, string Detail)>? fragments = null) : IDialogueEffects
{
    private readonly HashSet<FormKey> _unreadable = new();

    public const string FragmentNotRead = "the INFO has a VMAD subrecord but it was not read into fragment rows";

    public static QuestStageEffects For(GameSession session, string? speaker = null) => new(session.QuestStages, () =>
    {
        int refused = session.FragmentsRefused;
        int ran = session.RunQueuedFragments();
        return (ran, session.FragmentsRefused - refused);
    }, (info, parse, row) => session.RunInfoFragment(info, parse, row, speaker));

    // Requires: called on the main thread outside a native callback, because the drain and the fragment run Papyrus.
    public IReadOnlyList<EffectOutcome> Run(DialogueInfoData info, bool end)
    {
        var outcomes = new List<EffectOutcome>();
        if (info.Result is not { } result)
            return outcomes;
        if ((end ? result.StageOnEnd : result.StageOnBegin) is { } stage)
            outcomes.Add(SetStage(info, stage, end ? "set-stage-on-end" : "set-stage-on-begin"));
        if (result.HasVmad && Fragment(info, result, end) is { } fragment)
            outcomes.Add(fragment);
        return outcomes;
    }

    private EffectOutcome? Fragment(DialogueInfoData info, InfoResult result, bool end)
    {
        var name = end ? "info-fragment-end" : "info-fragment-begin";
        if (result.Vmad is not { } parse)
            return Unreadable(info, name, FragmentNotRead);
        if (parse.Failure is { } failure)
            return Unreadable(info, name, failure);
        if (parse.Rows.FirstOrDefault(row => row.End == end) is not { } row)
            return null;
        if (fragments is null)
            return new EffectOutcome(name, EffectStatus.Refused, "no Papyrus session runs INFO fragments");
        try
        {
            var (completed, detail) = fragments(info.Key.ToString(), parse, row);
            return new EffectOutcome(name, completed ? EffectStatus.Applied : EffectStatus.Refused, detail);
        }
        catch (Exception ex)
        {
            return new EffectOutcome(name, EffectStatus.Failed, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private EffectOutcome? Unreadable(DialogueInfoData info, string name, string reason) =>
        _unreadable.Add(info.Key) ? new EffectOutcome(name, EffectStatus.Refused, reason) : null;

    private EffectOutcome SetStage(DialogueInfoData info, int stage, string name)
    {
        if (info.Quest is not { } quest)
            return new EffectOutcome(name, EffectStatus.Refused, "the INFO's topic names no parent quest");
        var key = quest.ToString();
        try
        {
            var state = stages.StateOf(key);
            if (state != QuestState.Running)
                return new EffectOutcome(name, EffectStatus.Refused, $"quest {key} is {state}, not Running");
            if (!stages.Apply(key, QuestNative.SetStage, stage))
                return new EffectOutcome(name, EffectStatus.Refused, $"the quest state machine refused stage {stage} of {key}");
            var (ran, refused) = drain();
            return new EffectOutcome(name, EffectStatus.Applied, $"quest {key} stage {stage}; {ran} quest fragment(s) ran, {refused} refused");
        }
        catch (Exception ex)
        {
            return new EffectOutcome(name, EffectStatus.Failed, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
