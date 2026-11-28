using Radicle.Interop;
using QuestState = OpenCommonwealth.Services.Papyrus.Quest.QuestState;

namespace OpenCommonwealth.Services.Dialogue;

public interface IQuestState
{
    QuestState StateOf(FormKey quest);

    int StageOf(FormKey quest);

    bool StageDone(FormKey quest, int stage);

    // Invariant: false means the session has no record of the quest, so a caller that must not guess answers unknown instead of a default.
    bool IsKnown(FormKey quest) => true;
}

public interface IAliasState
{
    bool? IsAliasRef(FormKey quest, int alias, FormKey reference, FormKey? baseForm);

    // Requires: called before the Radicle condition call whenever a row may ask IsAliasRef, so IsAliasRef inside the callback only reads data built here and never reads records.
    void Prime() { }
}
