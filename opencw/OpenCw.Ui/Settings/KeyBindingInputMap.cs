using System;
using System.Collections.Generic;
using System.Text;

namespace OpenCommonwealth.Services.Ui.Settings;

public sealed record InputMapEntry(string ActionName, InputDevice Device, string Code);

// Overridden is true when the player changed that device's bindings away from the defaults.
public sealed record InputTarget(string ActionName, InputDevice Device, bool Overridden);

public sealed record KeyBindingPlan(IReadOnlyList<InputMapEntry> Entries, IReadOnlyList<InputTarget> Targets, string Signature);

public static class KeyBindingInputMap
{
    private static readonly BindingSlot[] Slots = { BindingSlot.Primary, BindingSlot.Secondary };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["interact"] = "activate",
        ["aim"] = "iron_sights",
    };

    public const string TriggerLeft = "Trigger Left";
    public const string TriggerRight = "Trigger Right";

    // Requires: Godot MouseButton numbers.
    public static string? MouseCode(int buttonIndex) => buttonIndex switch
    {
        1 => "Mouse Left",
        2 => "Mouse Right",
        3 => "Mouse Middle",
        8 => "Mouse 4",
        9 => "Mouse 5",
        _ => null,
    };

    public static int? MouseButtonIndex(string code) => code switch
    {
        "Mouse Left" => 1,
        "Mouse Right" => 2,
        "Mouse Middle" => 3,
        "Mouse 4" => 8,
        "Mouse 5" => 9,
        _ => null,
    };

    // Requires: Godot JoyAxis trigger numbers.
    public static string? TriggerCode(int axis) => axis switch
    {
        4 => TriggerLeft,
        5 => TriggerRight,
        _ => null,
    };

    public static int? TriggerAxis(string code) => code switch
    {
        TriggerLeft => 4,
        TriggerRight => 5,
        _ => null,
    };

    public static string ActionNameFor(string bindableId) => Aliases.TryGetValue(bindableId, out var name) ? name : bindableId;

    public static KeyBindingPlan Build(KeyBindingModel model)
    {
        var entries = new List<InputMapEntry>();
        var targets = new List<InputTarget>();
        var signature = new StringBuilder();
        foreach (var action in model.Actions)
        {
            var name = ActionNameFor(action.Id);
            foreach (var device in Enum.GetValues<InputDevice>())
            {
                var overridden = !model.IsDefault(action.Id, device);
                targets.Add(new InputTarget(name, device, overridden));
                signature.Append(name).Append(':').Append((int)device).Append(overridden ? '!' : '=');
                foreach (var slot in Slots)
                {
                    var code = model.Get(action.Id, device, slot);
                    if (string.IsNullOrEmpty(code)) continue;
                    entries.Add(new InputMapEntry(name, device, code));
                    signature.Append(code).Append(',');
                }
                signature.Append(';');
            }
        }
        return new KeyBindingPlan(entries, targets, signature.ToString());
    }
}
