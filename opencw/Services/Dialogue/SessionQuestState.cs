using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using FormKey = Radicle.Interop.FormKey;

namespace OpenCommonwealth.Services.Dialogue;

public sealed class SessionQuestState(QuestStateMachine stages) : IQuestState
{
    public static SessionQuestState From(GameSession session) => new(session.QuestStages);

    public QuestState StateOf(FormKey quest) => stages.StateOf(quest.ToString());

    public int StageOf(FormKey quest) => stages.StageOf(quest.ToString());

    public bool StageDone(FormKey quest, int stage) => stages.StageDone(quest.ToString(), stage);
}
