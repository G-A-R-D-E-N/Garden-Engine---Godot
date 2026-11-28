using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Dialogue;
using Radicle.Interop;
using QuestState = OpenCommonwealth.Services.Papyrus.Quest.QuestState;

namespace OpenCommonwealth.Services.Papyrus.LiveState;

// Constraint: contextQuest is the quest the host is evaluating for and stands in where Radicle names none (an INFO row); knownQuestsOnly answers every quest fact unknown for a quest the bound source does not know; no member may call back into Radicle or the record reader; actorOf maps the condition's run-on to the store key of the actor that stands for it, or null when none does.
public sealed class StoreLiveState(LiveStateStore store, Func<LiveSubject, string?> actorOf,
    IReadOnlyDictionary<FormKey, IReadOnlyList<FormKey>>? lists = null, bool knownQuestsOnly = false, FormKey? contextQuest = null) : IConditionLiveState
{
    private IQuestState? QuestsOf(FormKey quest) => store.Quests is { } quests && (!knownQuestsOnly || quests.IsKnown(quest)) ? quests : null;

    private ActorFacts? Facts(LiveSubject who) => store.Actor(actorOf(who));

    private T Seen<T>(string fact, T answer, LiveSubject? who = null, FormKey? form = null, int? extra = null)
    {
        if (store.Trace.Enabled)
            store.Trace.Emit("Condition", $"{fact}{(who is { } w ? $" runOn={w.RunOn} actor={actorOf(w) ?? "none"}" : "")}{(form is { } f ? $" form={f}" : "")}{(extra is { } x ? $" arg={x}" : "")} -> {(object?)answer ?? "unknown"}");
        return answer;
    }

    private bool? AnyOf(FormKey form, Func<FormKey, bool?> test)
    {
        if (lists is null || !lists.TryGetValue(form, out var members)) return test(form);
        var results = members.Select(test).ToList();
        if (results.Contains(true)) return true;
        return results.Count > 0 && results.All(r => r is not null) ? false : null;
    }

    public double? CurrentTime() => Seen(nameof(CurrentTime), store.HourOfDay);

    public bool? IsDead(LiveSubject who) => Seen(nameof(IsDead), Facts(who)?.Dead, who);

    public double? Level(LiveSubject who) => Seen(nameof(Level), Facts(who)?.Level, who);

    public bool? IsId(LiveSubject who, FormKey form) =>
        Seen(nameof(IsId), Facts(who) is { } facts ? AnyOf(form, member => facts.Base is { } own ? own == member : null) : null, who, form);

    public bool? IsRace(LiveSubject who, FormKey race) =>
        Seen(nameof(IsRace), Facts(who) is { } facts ? AnyOf(race, member => facts.Race is { } own ? own == member : null) : null, who, race);

    public bool? InFaction(LiveSubject who, FormKey faction) =>
        Seen(nameof(InFaction), Facts(who)?.Factions is { } factions ? factions.ContainsKey(faction) : (bool?)null, who, faction);

    // Constraint: the rank is -1 for a non-member of known factions; unknown factions have no rank.
    public int? FactionRank(LiveSubject who, FormKey faction) =>
        Seen(nameof(FactionRank), Facts(who)?.Factions is { } factions ? (factions.TryGetValue(faction, out var rank) ? rank : -1) : (int?)null, who, faction);

    public bool? HasKeyword(LiveSubject who, FormKey keyword) =>
        Seen(nameof(HasKeyword), Facts(who)?.Keywords is { } keywords ? keywords.Contains(keyword) : (bool?)null, who, keyword);

    public double? GlobalValue(FormKey global) => Seen(nameof(GlobalValue), store.GlobalValue(global), null, global);

    public double? ActorValue(LiveSubject who, FormKey actorValue) =>
        Seen(nameof(ActorValue), Facts(who)?.Values is { } values && values.TryGetValue(actorValue, out var value) ? value : null, who, actorValue);

    public int? QuestStage(FormKey quest) => Seen(nameof(QuestStage), QuestsOf(quest) is { } quests ? quests.StageOf(quest) : (int?)null, null, quest);

    public bool? QuestStageDone(FormKey quest, int stage) =>
        Seen(nameof(QuestStageDone), QuestsOf(quest) is { } quests ? quests.StageDone(quest, stage) : (bool?)null, null, quest, stage);

    public bool? QuestRunning(FormKey quest) =>
        Seen(nameof(QuestRunning), QuestsOf(quest) is { } quests ? quests.StateOf(quest) == QuestState.Running : (bool?)null, null, quest);

    public bool? QuestCompleted(FormKey quest) =>
        Seen(nameof(QuestCompleted), QuestsOf(quest) is { } quests ? quests.StateOf(quest) == QuestState.Completed : (bool?)null, null, quest);

    public bool? IsVoiceType(LiveSubject who, FormKey voiceType) =>
        Seen(nameof(IsVoiceType), Facts(who) is { } facts ? AnyOf(voiceType, member => facts.Voice is { } own ? own == member : null) : null, who, voiceType);

    public int? SexOf(LiveSubject who) => Seen(nameof(SexOf), Facts(who)?.Sex, who);

    public int? ItemCount(LiveSubject who, FormKey item) => Seen(nameof(ItemCount), Facts(who)?.Items?.Invoke(item), who, item);

    public bool? IsAliasRef(LiveSubject who, FormKey? quest, int alias) =>
        Seen(nameof(IsAliasRef), (quest ?? contextQuest) is { } q && alias >= 0 && store.Aliases is { } aliases && Facts(who) is { Ref: { } reference } facts
            ? aliases.IsAliasRef(q, alias, reference, facts.Base) : null, who, quest ?? contextQuest, alias);
}
