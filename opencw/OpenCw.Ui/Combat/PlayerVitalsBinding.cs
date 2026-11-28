using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Vats;

namespace OpenCommonwealth.Services.Combat;

// Requires: main thread only; aidItem maps an AidKind to a carried item key and use consumes it through InventoryActions.
public sealed class PlayerVitalsBinding(PlayerVitals vitals, Func<AidKind, string?> aidItem, Func<string, InventoryActionResult> use)
{
    public const string StimpakObject = "StimpackObject_DO";
    public const string RadAwayObject = "RadAwayObject_DO";

    public event Action<AidKind, InventoryActionResult>? AidResolved;

    // Constraint: the Pip-Boy aid buttons use the item a DFOB default object names (DFOB\Object); null when the record or its link is absent.
    public static string? AidItem(IRecordReader reader, AidKind kind) =>
        reader.FindEditorId(kind == AidKind.Stimpak ? StimpakObject : RadAwayObject, "DFOB") is { } dfob
        && RecordRows.Format(reader.Scalar(dfob, "DFOB\\Object")) is { } item ? item : null;

    public static AidSupply? Supply(IRecordReader reader, PlayerItems items, AidKind kind) =>
        AidItem(reader, kind) is { } item ? new AidSupply(reader.Name(item) ?? reader.EditorId(item) ?? item, items.CountOf(item)) : null;

    public void Attach(PlayerStatusModel status) => status.AidRequested += OnAid;

    public void Attach(VatsMenuModel vats) => vats.Accepted += OnAccepted;

    private void OnAid(AidKind kind)
    {
        var result = aidItem(kind) is { } item ? use(item) : new InventoryActionResult(InventoryActionStatus.Refused, "no item for this aid");
        AidResolved?.Invoke(kind, result);
    }

    private void OnAccepted(IReadOnlyList<VatsQueued> queue) => vitals.SpendActionPoints(queue.Sum(q => q.Cost));
}
