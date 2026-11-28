using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Settings;

public static class KeyBindingMigration
{
    public const int CurrentVersion = 2;

    private static readonly IReadOnlyDictionary<string, string> LegacyControllerPrimary = new Dictionary<string, string>
    {
        ["sneak"] = "Pad RightStick",
        ["attack"] = "Pad RightShoulder",
        ["aim"] = "Pad LeftShoulder",
        ["reload"] = "Pad Y",
    };

    public static bool IsLegacyControllerDefault(string actionId, string? primary, string? secondary) =>
        secondary is null && primary is not null &&
        LegacyControllerPrimary.TryGetValue(actionId, out var legacy) && primary == legacy;
}
