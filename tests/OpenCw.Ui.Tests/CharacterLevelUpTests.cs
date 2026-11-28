using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Lists;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterLevelUpTests
{
    sealed class TestChecker : IPerkRequirementChecker
    {
        public bool Flag;

        public bool IsMet(string requirement, IReadOnlyDictionary<string, int> perkRanks) =>
            requirement == "flag"
                ? Flag
                : requirement.StartsWith("own:", StringComparison.Ordinal)
                  && perkRanks.TryGetValue(requirement.Substring(4), out var rank)
                  && rank > 0;
    }

    static readonly PerkDef[] Perks =
    {
        new("p1", "Alpha", "A", new[] { new PerkRank("alpha one"), new PerkRank("alpha two") }),
        new("p2", "Bravo", "A", new[] { new PerkRank("bravo one", new[] { "own:p1" }) }),
        new("p3", "Charlie", "B", new[] { new PerkRank("charlie one", new[] { "flag" }) }),
        new("p4", "Delta", "B", new[] { new PerkRank("delta one") }),
    };

    static readonly IReadOnlyDictionary<string, int> NoneOwned = new Dictionary<string, int>();

    static LevelUpModel Make(int picks = 3, IReadOnlyDictionary<string, int>? owned = null, TestChecker? checker = null,
                             bool requireAll = false, PerkDef[]? perks = null) =>
        new(perks ?? Perks, owned ?? NoneOwned, picks, checker ?? new TestChecker(), requireAll);

    static string[] Visible(LevelUpModel model) => model.Perks.Visible.Select(e => e.Def.Id).ToArray();

    [Fact]
    public void Only_perks_whose_requirements_are_met_are_listed_by_default()
    {
        var model = Make();

        Assert.Equal(new[] { "p1", "p4" }, Visible(model));
    }

    [Fact]
    public void Showing_unavailable_perks_lists_locked_ones_with_their_unmet_requirements()
    {
        var model = Make();
        model.ShowUnavailable = true;

        Assert.Equal(new[] { "p1", "p2", "p3", "p4" }, Visible(model));
        Assert.Equal(PerkStatus.Locked, model.Entry("p2")!.Status);
        Assert.Equal(new[] { "own:p1" }, model.Entry("p2")!.UnmetRequirements.ToArray());
        Assert.Equal(new[] { "flag" }, model.Entry("p3")!.UnmetRequirements.ToArray());
        Assert.Equal(PerkStatus.Available, model.Entry("p1")!.Status);
    }

    [Fact]
    public void A_pending_pick_can_satisfy_the_requirement_of_a_later_pick()
    {
        var model = Make(picks: 2);
        Assert.Equal(PickResult.RequirementsNotMet, model.Pick("p2"));

        Assert.Equal(PickResult.Picked, model.Pick("p1"));
        Assert.Equal(PerkStatus.Available, model.Entry("p2")!.Status);
        Assert.Equal(new[] { "p2", "p4" }, Visible(model));

        Assert.Equal(PickResult.Picked, model.Pick("p2"));
        Assert.Equal(0, model.RemainingPicks);
        Assert.Equal(PickResult.NoPicksLeft, model.Pick("p4"));
    }

    [Fact]
    public void A_perk_cannot_be_picked_twice_in_one_level_up_even_with_ranks_left()
    {
        var model = Make();

        Assert.Equal(PickResult.Picked, model.Pick("p1"));
        Assert.Equal(PickResult.AlreadyPicked, model.Pick("p1"));

        var pick = Assert.Single(model.Picks);
        Assert.Equal(new PerkPick("p1", 1), pick);
        Assert.Equal(2, model.RemainingPicks);
        Assert.Equal(PerkStatus.Picked, model.Entry("p1")!.Status);
    }

    [Fact]
    public void Owned_ranks_decide_the_next_rank_and_a_maxed_perk_cannot_be_picked()
    {
        var model = Make(owned: new Dictionary<string, int> { ["p1"] = 1 });
        var next = model.Entry("p1")!;
        Assert.Equal(1, next.CurrentRank);
        Assert.Equal(2, next.NextRank);

        Assert.Equal(PickResult.Picked, model.Pick("p1"));
        Assert.Equal(new PerkPick("p1", 2), Assert.Single(model.Picks));

        var maxed = Make(owned: new Dictionary<string, int> { ["p1"] = 2 });
        Assert.Equal(PerkStatus.Maxed, maxed.Entry("p1")!.Status);
        Assert.Equal(0, maxed.Entry("p1")!.NextRank);
        Assert.Equal(PickResult.Maxed, maxed.Pick("p1"));
        Assert.DoesNotContain("p1", Visible(maxed));
    }

    [Fact]
    public void Compare_gives_the_current_and_next_rank_text_from_data()
    {
        var owned = Make(owned: new Dictionary<string, int> { ["p1"] = 1 });
        var mid = owned.Compare("p1")!;
        Assert.Equal("Alpha", mid.PerkName);
        Assert.Equal(1, mid.CurrentRank);
        Assert.Equal(2, mid.NextRank);
        Assert.Equal("alpha one", mid.CurrentText);
        Assert.Equal("alpha two", mid.NextText);

        var fresh = Make().Compare("p1")!;
        Assert.Null(fresh.CurrentText);
        Assert.Equal("alpha one", fresh.NextText);

        var maxed = Make(owned: new Dictionary<string, int> { ["p1"] = 2 }).Compare("p1")!;
        Assert.Equal("alpha two", maxed.CurrentText);
        Assert.Null(maxed.NextText);

        Assert.Null(Make().Compare("nope"));
    }

    [Fact]
    public void Requirement_state_is_re_read_on_refresh_and_gates_the_pick()
    {
        var checker = new TestChecker();
        var model = Make(checker: checker);
        Assert.Equal(PickResult.RequirementsNotMet, model.Pick("p3"));

        checker.Flag = true;
        Assert.Equal(PickResult.RequirementsNotMet, model.Pick("p3"));
        model.Refresh();
        Assert.Equal(PickResult.Picked, model.Pick("p3"));
    }

    [Fact]
    public void Pick_results_are_reported_in_a_fixed_priority_order()
    {
        var model = Make(picks: 0);

        Assert.Equal(PickResult.UnknownPerk, model.Pick("zz"));
        Assert.Equal(PickResult.NoPicksLeft, model.Pick("p1"));
        Assert.Equal(PickResult.NoPicksLeft, model.Pick("p3"));
    }

    [Fact]
    public void Unpicking_a_perk_also_drops_picks_that_depended_on_it()
    {
        var model = Make();
        model.Pick("p1");
        model.Pick("p2");
        Assert.Equal(1, model.RemainingPicks);

        Assert.True(model.Unpick("p1"));
        Assert.Empty(model.Picks);
        Assert.Equal(3, model.RemainingPicks);
        Assert.Equal(PerkStatus.Locked, model.Entry("p2")!.Status);
        Assert.False(model.Unpick("p1"));
    }

    [Fact]
    public void Unpicking_an_independent_perk_keeps_the_others()
    {
        var model = Make();
        model.Pick("p1");
        model.Pick("p4");

        Assert.True(model.Unpick("p4"));
        Assert.Equal(new PerkPick("p1", 1), Assert.Single(model.Picks));
        Assert.Equal(PerkStatus.Available, model.Entry("p4")!.Status);
    }

    [Fact]
    public void Confirm_waits_for_all_picks_when_required_then_locks_the_model()
    {
        var model = Make(picks: 2, requireAll: true);
        model.Pick("p1");
        Assert.Equal(LevelUpConfirm.PicksRemaining, model.Confirm());
        Assert.False(model.IsConfirmed);

        model.Pick("p4");
        Assert.Equal(LevelUpConfirm.Confirmed, model.Confirm());
        Assert.Equal(new[] { new PerkPick("p1", 1), new PerkPick("p4", 1) }, model.ConfirmedPicks);

        Assert.Equal(PickResult.Finished, model.Pick("p3"));
        Assert.False(model.Unpick("p1"));
        Assert.False(model.Cancel());
        Assert.Equal(LevelUpConfirm.AlreadyFinished, model.Confirm());
    }

    [Fact]
    public void Confirm_with_no_picks_is_allowed_when_picks_are_optional()
    {
        var model = Make();

        Assert.Equal(LevelUpConfirm.Confirmed, model.Confirm());
        Assert.Empty(model.ConfirmedPicks);
    }

    [Fact]
    public void Cancel_discards_the_picks_and_locks_the_model()
    {
        var model = Make();
        model.Pick("p1");

        Assert.True(model.Cancel());
        Assert.True(model.IsCancelled);
        Assert.Empty(model.Picks);
        Assert.Empty(model.ConfirmedPicks);
        Assert.Equal(PickResult.Finished, model.Pick("p4"));
        Assert.Equal(LevelUpConfirm.AlreadyFinished, model.Confirm());
        Assert.False(model.IsConfirmed);
    }

    [Fact]
    public void Search_and_category_filters_apply_on_top_of_the_availability_filter()
    {
        var model = Make();
        model.ShowUnavailable = true;

        model.Perks.SetSearch("del");
        Assert.Equal(new[] { "p4" }, Visible(model));

        model.Perks.SetSearch(null);
        model.Perks.SetCategory("A");
        Assert.Equal(new[] { "p1", "p2" }, Visible(model));
        Assert.Equal(new[] { "A", "B" }, model.Categories.ToArray());

        model.ShowUnavailable = false;
        Assert.Equal(new[] { "p1" }, Visible(model));
    }

    [Fact]
    public void Changed_fires_for_each_accepted_pick_and_not_for_refused_ones()
    {
        var model = Make();
        var count = 0;
        model.Changed += () => count++;

        model.Pick("p2");
        Assert.Equal(0, count);
        model.Pick("p1");
        model.Pick("p1");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Construction_rejects_duplicate_ids_and_perks_without_ranks()
    {
        var dup = new[] { Perks[0], Perks[0] };
        Assert.Throws<ArgumentException>(() => Make(perks: dup));
        var rankless = new[] { new PerkDef("x", "X", "A", Array.Empty<PerkRank>()) };
        Assert.Throws<ArgumentException>(() => Make(perks: rankless));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(picks: -1));
    }

    [Fact]
    public void Generic_data_works_with_its_own_requirement_checker()
    {
        var data = new GenericCharacterData();
        var model = new LevelUpModel(data.Perks, NoneOwned, 2, data.PerkRequirements);

        Assert.Equal(PickResult.RequirementsNotMet, model.Pick("p2"));
        Assert.Equal(PickResult.Picked, model.Pick("p1"));
        Assert.Equal(PickResult.Picked, model.Pick("p2"));
    }
}
