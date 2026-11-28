using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Menus;

public static class MenuSoundMap
{
    public static readonly IReadOnlyDictionary<string, string> Mapped = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["password_good"] = "UITerminalPasswordGood",
        ["password_bad"] = "UITerminalPasswordBad",
        ["help_attempts"] = "UITerminalPasswordHelpAttempts",
        ["help_dud"] = "UITerminalPasswordHelpDud",
        ["hover_word"] = "UITerminalCharMultiple",
        ["build.pickup"] = "UIWorkshopModeItemPickUpGeneric",
        ["build.place"] = "UIWorkshopModeItemPutDownGeneric",
        ["build.fail"] = "UIWorkshopModeItemPutDownFail",
        ["build.store"] = "UIWorkshopModeItemStore",
        ["build.scrap"] = "UIWorkshopModeItemScrapGeneric",
        ["build.exit"] = "UIWorkshopModeExit",
        ["Transition"] = "UIPipBoyScreenBurstStatic",
        ["perk.first"] = "UIPerkMenuChoosePerkFirstRank",
        ["perk.rank"] = "UIPerkMenuChoosePerk",
        ["menu.close"] = "UIMenuCancel",
        ["terminal.accept"] = "UITerminalCharEnter",
        ["store_a"] = "UITerminalHardDriveA",
        ["store_b"] = "UITerminalHardDriveB",
        ["trade.commit"] = "ITMBarter",
        ["bench.build_fail"] = "UIMenuCancel",
    };

    public static readonly IReadOnlyList<string> Unmapped = new[]
    {
        "accept", "cancel", "ok", "select_move", "type_loop_start", "type_loop_stop", "type_skip",
        "focus.move", "perk.preview", "rank.view", "settler.assign", "special.up",
        "menu.back", "menu.confirm", "menu.forward", "menu.tab",
        "ui.accept", "ui.error", "ui.open", "ui.warn",
        "menu.blocked", "MapRollover", "Focus", "Confirm", "Cancel",
        "abort", "critical_fire", "denied", "enter", "exit", "queue", "select", "unqueue",
    };

    public static string? EditorId(string name) => Mapped.TryGetValue(name, out var id) ? id : null;
}
