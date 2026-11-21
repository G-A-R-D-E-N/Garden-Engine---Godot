namespace OpenCommonwealth.Services.Ui.Workshop;

public static class WorkshopObjectFacts
{
    // Requires: a workshop item that is neither a work object nor a bed is a target that only ends the command state; anything that is not a workshop item is null, so activating it keeps its normal meaning.
    public static CommandTarget? Of(string objectId, bool workObjectKeyword, bool bed, bool allowAssignment = true, bool workshopItem = false) =>
        workObjectKeyword || bed || workshopItem ? new CommandTarget(objectId, allowAssignment, workObjectKeyword, bed) : null;
}
