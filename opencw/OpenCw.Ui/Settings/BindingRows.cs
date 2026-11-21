using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Settings;

public static class BindingRows
{
    private static readonly BindingSlot[] Slots = { BindingSlot.Primary, BindingSlot.Secondary };

    public static IEnumerable<BindingRef> All(IEnumerable<BindableAction> actions)
    {
        foreach (var action in actions)
            foreach (var device in Enum.GetValues<InputDevice>())
                foreach (var slot in Slots) yield return new BindingRef(action.Id, device, slot);
    }

    public static string Id(BindingRef row) => $"{row.ActionId}|{row.Device}|{row.Slot}";

    public static BindingRef? Parse(string id)
    {
        var parts = id.Split('|');
        return parts.Length == 3 && Enum.TryParse<InputDevice>(parts[1], out var device) && Enum.TryParse<BindingSlot>(parts[2], out var slot)
            ? new BindingRef(parts[0], device, slot)
            : null;
    }

    public static string Label(string actionLabel, BindingRef row) => (row.Device, row.Slot) switch
    {
        (InputDevice.Keyboard, BindingSlot.Primary) => actionLabel,
        (InputDevice.Keyboard, _) => actionLabel + " (2nd)",
        (_, BindingSlot.Primary) => actionLabel + " (pad)",
        _ => actionLabel + " (pad 2nd)",
    };

    public static string Text(KeyBindingModel model, BindingRef row) =>
        (model.Get(row.ActionId, row.Device, row.Slot) ?? "None").ToUpperInvariant();

    public static bool Bind(KeyBindingModel model, BindingRef row, string code)
    {
        if (!model.BeginCapture(row.ActionId, row.Device, row.Slot)) return false;
        var outcome = model.Capture(code);
        if (outcome == CaptureOutcome.Conflict && model.Resolve(ConflictResolution.Swap)) return true;
        model.CancelCapture();
        return outcome == CaptureOutcome.Bound;
    }
}
