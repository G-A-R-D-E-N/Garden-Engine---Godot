namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread only; Talk and Trade are not roster changes, so Apply returns false for them and the host opens those screens itself.
public static class CompanionCommands
{
    public static bool Apply(CompanionMenuEntry entry, CompanionRoster roster, string refKey, string baseKey, bool possibleCompanion)
    {
        if (entry == CompanionMenuEntry.ComeWithMe) return CompanionRecruit.Ask(possibleCompanion, roster, refKey, baseKey);
        if (entry == CompanionMenuEntry.Dismiss) return roster.Dismiss(refKey);
        if (roster.Current is not { } held || held.RefKey != refKey) return false;
        return entry switch
        {
            CompanionMenuEntry.Follow => roster.Configure(refKey, CompanionState.Follow, held.Distance, held.Stance),
            CompanionMenuEntry.Wait => roster.Configure(refKey, CompanionState.Wait, held.Distance, held.Stance),
            CompanionMenuEntry.Distance => roster.Configure(refKey, held.State, NextDistance(held.Distance), held.Stance),
            CompanionMenuEntry.Stance => roster.Configure(refKey, held.State, held.Distance, NextStance(held.Stance)),
            _ => false,
        };
    }

    public static IReadOnlyList<(string Label, int Id)> Menu(CompanionRoster roster, string refKey, bool canRecruit, CompanionMenuLocks locks)
    {
        var held = roster.IsCompanion(refKey) ? roster.Current : null;
        var state = held is null ? FollowerState.Follow : CompanionFollowRules.StateOf(held.State);
        return CompanionCommandMenu.Entries(held is not null, state, canRecruit, locks).Select(entry => (Label(entry, held), (int)entry)).ToList();
    }

    public static string? Notice(CompanionMenuEntry entry) => entry switch
    {
        CompanionMenuEntry.ComeWithMe => "Companion recruited.",
        CompanionMenuEntry.Dismiss => "Companion dismissed.",
        _ => null,
    };

    public static string Label(CompanionMenuEntry entry, CompanionEntry? held) => entry switch
    {
        CompanionMenuEntry.Talk => "Talk",
        CompanionMenuEntry.Trade => "Trade",
        CompanionMenuEntry.Follow => "Follow me",
        CompanionMenuEntry.Wait => "Wait here",
        CompanionMenuEntry.Distance => held is null ? "Distance" : $"Distance: {held.Distance}",
        CompanionMenuEntry.Stance => held is null ? "Stance" : $"Stance: {held.Stance}",
        CompanionMenuEntry.Dismiss => "Dismiss",
        _ => "Come with me",
    };

    private static CompanionDistance NextDistance(CompanionDistance distance) => distance switch
    {
        CompanionDistance.Near => CompanionDistance.Medium,
        CompanionDistance.Medium => CompanionDistance.Far,
        _ => CompanionDistance.Near,
    };

    private static CompanionStance NextStance(CompanionStance stance) =>
        stance == CompanionStance.Defensive ? CompanionStance.Aggressive : CompanionStance.Defensive;
}
