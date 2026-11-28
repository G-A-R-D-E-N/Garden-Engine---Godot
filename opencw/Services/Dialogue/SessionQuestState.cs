using System.Runtime.CompilerServices;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using FormKey = Radicle.Interop.FormKey;

namespace OpenCommonwealth.Services.Dialogue;

public sealed class SessionQuestState(QuestStateMachine stages) : IQuestState
{
    private static readonly ConditionalWeakTable<GameSession, SessionQuestState> Bound = new();

    // Invariant: one instance per session, so owners of the store's binding can recognise their own by reference.
    public static SessionQuestState From(GameSession session) => Bound.GetValue(session, s => new SessionQuestState(s.QuestStages));

    public QuestState StateOf(FormKey quest) => stages.StateOf(quest.ToString());

    public int StageOf(FormKey quest) => stages.StageOf(quest.ToString());

    public bool StageDone(FormKey quest, int stage) => stages.StageDone(quest.ToString(), stage);

    public bool IsKnown(FormKey quest) => stages.IsKnown(quest.ToString());
}
