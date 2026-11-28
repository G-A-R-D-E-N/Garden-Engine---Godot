using System;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.Workshop;

public static class WorkshopScrapRelease
{
    public static void Release(string objectId, bool built, Action removeTarget) =>
        Release(objectId, built, removeTarget, BuiltObjectLedger.Current, ContainerLedger.Current, SettlerAssignments.Current);

    public static void Release(string objectId, bool built, Action removeTarget, BuiltObjectLedger builtObjects, ContainerLedger containers, SettlerAssignments settlers)
    {
        removeTarget();
        settlers.ObjectGone(objectId);
        if (built) builtObjects.Remove(objectId);
        else containers.MarkTaken(objectId);
    }
}
