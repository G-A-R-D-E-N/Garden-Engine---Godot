using System.Linq;
using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CompanionCommandMenuTests
{
    private static CompanionMenuEntry[] Entries(bool companion, FollowerState state, bool canRecruit = false, CompanionMenuLocks locks = CompanionMenuLocks.None) =>
        CompanionCommandMenu.Entries(companion, state, canRecruit, locks).ToArray();

    [Fact]
    public void A_following_companion_is_offered_wait_and_not_follow()
    {
        Assert.Equal(
            new[] { CompanionMenuEntry.Talk, CompanionMenuEntry.Trade, CompanionMenuEntry.Wait, CompanionMenuEntry.Distance, CompanionMenuEntry.Dismiss },
            Entries(true, FollowerState.Follow));
    }

    [Theory]
    [InlineData(FollowerState.Follow)]
    [InlineData(FollowerState.Wait)]
    public void The_stance_entry_is_not_offered_while_nothing_reads_the_stance(FollowerState state)
    {
        Assert.DoesNotContain(CompanionMenuEntry.Stance, Entries(true, state));
        Assert.DoesNotContain(CompanionMenuEntry.Stance, Entries(true, state, true));
    }

    [Fact]
    public void A_waiting_companion_is_offered_follow_and_not_wait()
    {
        Assert.Equal(
            new[] { CompanionMenuEntry.Talk, CompanionMenuEntry.Trade, CompanionMenuEntry.Follow, CompanionMenuEntry.Distance, CompanionMenuEntry.Dismiss },
            Entries(true, FollowerState.Wait));
    }

    [Fact]
    public void A_recruitable_non_companion_is_offered_come_with_me()
    {
        Assert.Equal(new[] { CompanionMenuEntry.Talk, CompanionMenuEntry.ComeWithMe }, Entries(false, FollowerState.Follow, canRecruit: true));
    }

    [Fact]
    public void A_non_companion_that_cannot_be_recruited_is_offered_talk_only()
    {
        Assert.Equal(new[] { CompanionMenuEntry.Talk }, Entries(false, FollowerState.Follow));
    }

    [Fact]
    public void All_hides_every_entry_for_a_companion_and_for_a_recruitable_actor()
    {
        Assert.Empty(Entries(true, FollowerState.Follow, locks: CompanionMenuLocks.All));
        Assert.Empty(Entries(false, FollowerState.Follow, canRecruit: true, locks: CompanionMenuLocks.All));
    }

    [Theory]
    [InlineData(CompanionMenuLocks.Dialogue, CompanionMenuEntry.Talk)]
    [InlineData(CompanionMenuLocks.Trade, CompanionMenuEntry.Trade)]
    [InlineData(CompanionMenuLocks.Dismiss, CompanionMenuEntry.Dismiss)]
    public void Each_keyword_lock_hides_only_its_own_entry(CompanionMenuLocks locks, CompanionMenuEntry hidden)
    {
        var entries = Entries(true, FollowerState.Follow, locks: locks);

        Assert.DoesNotContain(hidden, entries);
        Assert.Equal(4, entries.Length);
    }

    [Fact]
    public void Command_lock_hides_the_follow_wait_distance_and_stance_entries()
    {
        Assert.Equal(
            new[] { CompanionMenuEntry.Talk, CompanionMenuEntry.Trade, CompanionMenuEntry.Dismiss },
            Entries(true, FollowerState.Follow, locks: CompanionMenuLocks.Command));
    }

    [Fact]
    public void Come_with_me_is_not_hidden_by_the_companion_keyword_locks()
    {
        var locks = CompanionMenuLocks.Dialogue | CompanionMenuLocks.Trade | CompanionMenuLocks.Dismiss | CompanionMenuLocks.Command;

        Assert.Equal(new[] { CompanionMenuEntry.ComeWithMe }, Entries(false, FollowerState.Follow, canRecruit: true, locks: locks));
    }
}
