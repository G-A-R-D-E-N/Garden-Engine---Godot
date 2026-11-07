using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Radicle.Interop.Native;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Quest;

/// <summary>One decoded QUST stage, as Radicle hands it over.</summary>
public sealed record QuestStageInput(int Index, bool StartupStage, int Fragments);

public sealed record QuestStages(string Quest, IReadOnlyList<QuestStageInput> Stages);

public enum StageState
{
    /// <summary>One startup stage was identified and nothing blocks it.</summary>
    Identified,

    /// <summary>No stage is marked as the startup stage, so nothing is assumed.</summary>
    Unidentified,

    /// <summary>More than one stage is marked, so nothing is chosen.</summary>
    Ambiguous,

    /// <summary>The stage is identified, but unsupported fragment behavior still blocks it.</summary>
    BlockedByFragments,
}

public sealed record StageOutcome(string Quest, StageState State, int? Stage, string Reason);

public sealed record StageReport(IReadOnlyList<StageOutcome> Quests, int Identified, int Refused, int Unresolved)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"quests={Quests.Count} identified={Identified} refused={Refused} unresolved={Unresolved}\n");
        foreach (var outcome in Quests)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {outcome.Quest} {outcome.State.ToString().ToLowerInvariant()} stage={outcome.Stage?.ToString(CultureInfo.InvariantCulture)} {outcome.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Identifies the startup stage of a staged quest so it can pass B9's startup guard.
/// </summary>
/// <remarks>
/// The stage data is decoded by Radicle; this type only reads which stages the decoder marked. A quest
/// with exactly one marked stage starts there; a quest with none, or with more than one, is left
/// unresolved and counted, because an unidentified stage is never guessed — the first stage is not
/// assumed and a tie is not broken by index. A quest whose identified stage carries fragment behavior
/// that is not proven stays blocked by the B9 guard, and the reason is recorded.
/// </remarks>
public static class QuestStartupStages
{
    public const string IdentificationKey = "quest.startup.stage_identification";
    public const string FragmentTimingKey = "quest.runtime.fragment_dispatch_timing";

    public static StageReport Identify(IEnumerable<QuestStages> quests, EvidenceGate gate)
    {
        var ordered = quests.OrderBy(quest => quest.Quest, Utf8Order).ToList();
        var allowed = new List<QuestStages>();
        var refused = new StageOutcome?[ordered.Count];
        var refusedCount = 0;
        for (var i = 0; i < ordered.Count; ++i)
        {
            var proven = gate.Gate(IdentificationKey).Allowed;
            var crosses = !ordered[i].Quest.Contains('\u0000')
                && Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(ordered[i].Quest)) == ordered[i].Quest;
            if (proven && crosses) allowed.Add(ordered[i]);
            else
            {
                refused[i] = new StageOutcome(ordered[i].Quest, StageState.Unidentified, null,
                    proven ? "the quest name cannot cross to Radicle as UTF-8 text (embedded NUL or unpaired surrogate), so nothing was chosen"
                        : "startup-stage identification is not proven, so nothing was chosen");
                refusedCount++;
            }
        }

        var native = new Dictionary<string, Queue<StageOutcome>>(StringComparer.Ordinal);
        foreach (var result in IdentifyWithRadicle(allowed))
        {
            if (!native.TryGetValue(result.Quest, out var queue)) native[result.Quest] = queue = new Queue<StageOutcome>();
            queue.Enqueue(result);
        }
        var outcomes = new List<StageOutcome>(ordered.Count);
        for (var i = 0; i < ordered.Count; ++i)
        {
            var quest = ordered[i];
            if (refused[i] is { } skipped)
            {
                outcomes.Add(skipped);
                continue;
            }
            var outcome = native.TryGetValue(quest.Quest, out var pending) && pending.Count > 0
                ? pending.Dequeue()
                : new StageOutcome(quest.Quest, StageState.Unidentified, null, "Radicle returned no outcome for this quest, so nothing was chosen");
            if (outcome.State == StageState.Identified)
            {
                var stage = quest.Stages.First(candidate => candidate.StartupStage);
                if (stage.Fragments > 0 && !gate.Gate(FragmentTimingKey).Allowed)
                {
                    outcome = new StageOutcome(quest.Quest, StageState.BlockedByFragments, stage.Index,
                        $"the startup stage is {stage.Index}, but its {stage.Fragments} fragments have unproven dispatch timing");
                }
            }
            outcomes.Add(outcome);
        }

        return new StageReport(outcomes,
            outcomes.Count(outcome => outcome.State == StageState.Identified),
            outcomes.Count(outcome => outcome.State == StageState.BlockedByFragments) + refusedCount,
            outcomes.Count(outcome => outcome.State is StageState.Unidentified or StageState.Ambiguous) - refusedCount);
    }

    // Invariant: Radicle sorts quest names by UTF-8 bytes, so the C# order must match it byte for byte.
    private static readonly Comparer<string> Utf8Order = Comparer<string>.Create((left, right) =>
        Encoding.UTF8.GetBytes(left).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(right)));

    private static unsafe StageOutcome[] IdentifyWithRadicle(IReadOnlyList<QuestStages> quests)
    {
        if (quests.Count == 0) return Array.Empty<StageOutcome>();
        var blocks = new List<IntPtr>();
        var texts = new List<IntPtr>();
        var report = default(PapyrusStageReport);
        try
        {
            var input = new PapyrusQuestStages[quests.Count];
            for (var i = 0; i < quests.Count; ++i)
            {
                var stages = quests[i].Stages;
                var block = (PapyrusStageInput*)NativeMemory.AllocZeroed((nuint)Math.Max(1, stages.Count),
                    (nuint)sizeof(PapyrusStageInput));
                blocks.Add((IntPtr)block);
                for (var j = 0; j < stages.Count; ++j)
                {
                    block[j] = new PapyrusStageInput
                    {
                        Index = stages[j].Index,
                        StartupStage = stages[j].StartupStage ? 1 : 0,
                        Fragments = stages[j].Fragments,
                    };
                }
                var name = Marshal.StringToCoTaskMemUTF8(quests[i].Quest);
                texts.Add(name);
                input[i] = new PapyrusQuestStages { Quest = name, Stages = (IntPtr)block, StageCount = (nuint)stages.Count };
            }

            fixed (PapyrusQuestStages* native = input)
            {
                var status = RadicleNative.PapyrusIdentifyStartupStages(native, (nuint)input.Length, &report);
                if (status != Status.Ok) throw new InvalidOperationException($"Radicle refused startup stage identification: {status}");
            }
            var outcomes = (PapyrusStageOutcome*)report.Outcomes;
            var result = new StageOutcome[checked((int)report.Count)];
            for (var i = 0; i < result.Length; ++i)
            {
                result[i] = new StageOutcome(Marshal.PtrToStringUTF8(outcomes[i].Quest) ?? "", (StageState)outcomes[i].State,
                    outcomes[i].HasStage != 0 ? outcomes[i].Stage : null, Marshal.PtrToStringUTF8(outcomes[i].Reason) ?? "");
            }
            return result;
        }
        finally
        {
            RadicleNative.PapyrusStageReportDestroy(&report);
            foreach (var block in blocks) NativeMemory.Free((void*)block);
            foreach (var text in texts) Marshal.FreeCoTaskMem(text);
        }
    }
}
