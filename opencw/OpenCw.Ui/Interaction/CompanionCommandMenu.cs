using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Interaction;

public enum CompanionMenuEntry { Talk, Trade, Follow, Wait, Distance, Stance, Dismiss, ComeWithMe }

[Flags]
public enum CompanionMenuLocks { None = 0, Trade = 1, Dismiss = 2, Dialogue = 4, Command = 8, All = 16 }

public static class CompanionCommandMenu
{
    public static IReadOnlyList<CompanionMenuEntry> Entries(bool companion, FollowerState state, bool canRecruit, CompanionMenuLocks locks)
    {
        var candidates = companion
            ? CompanionEntries(state)
            : canRecruit
                ? new[] { CompanionMenuEntry.Talk, CompanionMenuEntry.ComeWithMe }
                : new[] { CompanionMenuEntry.Talk };
        return candidates.Where(entry => (locks & (CompanionMenuLocks.All | LockOf(entry))) == 0).ToArray();
    }

    private static CompanionMenuEntry[] CompanionEntries(FollowerState state) =>
    [
        CompanionMenuEntry.Talk,
        CompanionMenuEntry.Trade,
        state == FollowerState.Wait ? CompanionMenuEntry.Follow : CompanionMenuEntry.Wait,
        CompanionMenuEntry.Distance,
        CompanionMenuEntry.Dismiss,
    ];

    private static CompanionMenuLocks LockOf(CompanionMenuEntry entry) => entry switch
    {
        CompanionMenuEntry.Talk => CompanionMenuLocks.Dialogue,
        CompanionMenuEntry.Trade => CompanionMenuLocks.Trade,
        CompanionMenuEntry.Dismiss => CompanionMenuLocks.Dismiss,
        CompanionMenuEntry.ComeWithMe => CompanionMenuLocks.None,
        _ => CompanionMenuLocks.Command,
    };
}
