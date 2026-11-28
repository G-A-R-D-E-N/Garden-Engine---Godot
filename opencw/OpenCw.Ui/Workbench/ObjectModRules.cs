namespace OpenCommonwealth.Services.Ui.Workbench;

// Constraint: both rules are UNVERIFIED game behaviour chosen from vanilla data (CodeNotes:Garden Engine/history/docs/code-notes/opencw-instance-items.md); turning one off restores the pre-rule behaviour.
public sealed record ObjectModRules(bool MatchTargetKeywords = true, bool ApplyDefaultMods = true)
{
    // Requires: main thread; set once at startup, read when a bench or a default-mod cache is built.
    public static ObjectModRules Current { get; set; } = new();
}
