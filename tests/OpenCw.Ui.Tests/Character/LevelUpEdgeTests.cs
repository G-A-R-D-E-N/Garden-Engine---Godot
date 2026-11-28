using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class LevelUpEdgeTests
{
    static readonly string[] Names = { "Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck" };

    sealed class NeverChecker : IPerkRequirementChecker
    {
        public bool IsMet(string requirement, IReadOnlyDictionary<string, int> perkRanks) => requirement != "never";
    }

    sealed class Service : IProgressionService
    {
        public ProgressionSnapshot Snapshot { get; set; } = null!;

        public ChooseResult Result { get; set; } = ChooseResult.Ok;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public ChooseResult TryChoose(string entryId, int rank) => Result;
    }

    static readonly PerkDef[] Perks =
    {
        new("p1", "Alpha", "A", new[] { new PerkRank("alpha one"), new PerkRank("alpha two") }),
        new("p2", "Bravo", "A", new[] { new PerkRank("bravo one"), new PerkRank("bravo two", new[] { "never" }) }),
    };

    static LevelUpModel Make(int picks, IReadOnlyDictionary<string, int>? owned = null, bool requireAll = false) =>
        new(Perks, owned ?? new Dictionary<string, int>(), picks, new NeverChecker(), requireAll);

    static ProgressionSnapshot Snap(int level = 3, int points = 1, bool attributeHeads = true, int? cap = null)
    {
        var entries = new List<PerkChartEntry>();
        if (attributeHeads)
        {
            entries.AddRange(Enumerable.Range(0, 7).Select(c => new PerkChartEntry($"a{c}", Names[c], PerkChartKind.Attribute, c, 5, 10,
                Enumerable.Range(1, 10).Select(i => new PerkRankInfo($"{Names[c]} {i}")).ToArray(), true)));
        }
        entries.Add(new PerkChartEntry("iron", "Iron Fist", PerkChartKind.Perk, 0, 0, 1, new[] { new PerkRankInfo("Iron Fist rank 1") }, true));
        return new ProgressionSnapshot(new PerkChartSnapshot(1, level, points, new[] { 5, 5, 5, 5, 5, 5, 5 }, entries), 10, 100, cap);
    }

    [Fact]
    public void A_negative_pick_budget_is_rejected_and_a_zero_budget_blocks_every_pick()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Make(-1));
        Assert.Equal("picksAvailable", error.ParamName);

        var none = Make(0);
        Assert.Equal(0, none.RemainingPicks);
        Assert.Equal(PickResult.NoPicksLeft, none.Pick("p1"));
        Assert.Empty(none.Picks);
    }

    [Fact]
    public void Owned_ranks_past_the_last_rank_count_as_maxed_and_later_changes_to_the_input_are_ignored()
    {
        var owned = new Dictionary<string, int> { ["p1"] = 9 };
        var model = Make(1, owned);

        Assert.Equal(2, model.Entry("p1")!.CurrentRank);
        Assert.Equal(PerkStatus.Maxed, model.Entry("p1")!.Status);

        owned["p1"] = 0;
        model.Refresh();
        Assert.Equal(2, model.Entry("p1")!.CurrentRank);
    }

    [Fact]
    public void The_requirements_of_the_next_rank_decide_availability_not_those_of_rank_one()
    {
        var fresh = Make(2);
        Assert.Equal(PerkStatus.Available, fresh.Entry("p2")!.Status);
        Assert.Empty(fresh.Entry("p2")!.UnmetRequirements);

        var second = Make(2, new Dictionary<string, int> { ["p2"] = 1 });
        var entry = second.Entry("p2")!;
        Assert.Equal(PerkStatus.Locked, entry.Status);
        Assert.Equal(2, entry.NextRank);
        Assert.Equal(new[] { "never" }, entry.UnmetRequirements);
        Assert.Equal(PickResult.RequirementsNotMet, second.Pick("p2"));
    }

    [Fact]
    public void A_picked_perk_compares_its_owned_rank_against_the_picked_rank()
    {
        var model = Make(1, new Dictionary<string, int> { ["p1"] = 1 });
        model.Pick("p1");

        var compare = model.Compare("p1")!;

        Assert.Equal(new RankComparison("Alpha", 1, 2, "alpha one", "alpha two"), compare);
        Assert.Equal(PerkStatus.Picked, model.Entry("p1")!.Status);
    }

    [Fact]
    public void Changed_fires_once_per_accepted_change_and_never_for_refused_calls()
    {
        var model = Make(2);
        var changes = 0;
        model.Changed += () => changes++;

        model.Pick("p1");
        Assert.Equal(1, changes);
        model.Refresh();
        Assert.Equal(2, changes);
        Assert.True(model.Unpick("p1"));
        Assert.Equal(3, changes);
        Assert.False(model.Unpick("p1"));
        Assert.False(model.Unpick("missing"));
        Assert.Equal(PickResult.UnknownPerk, model.Pick("missing"));
        Assert.Equal(3, changes);

        model.Pick("p1");
        Assert.Equal(LevelUpConfirm.Confirmed, model.Confirm());
        Assert.Equal(5, changes);
        Assert.Equal(new[] { new PerkPick("p1", 1) }, model.ConfirmedPicks);
        Assert.Equal(new[] { new PerkPick("p1", 1) }, model.Picks);

        Assert.False(model.Cancel());
        Assert.False(model.Unpick("p1"));
        Assert.Equal(LevelUpConfirm.AlreadyFinished, model.Confirm());
        Assert.Equal(PickResult.Finished, model.Pick("p2"));
        Assert.Equal(5, changes);
    }

    [Fact]
    public void Cancel_gives_the_picks_back_raises_changed_once_and_blocks_a_later_confirm()
    {
        var model = Make(2);
        var changes = 0;
        model.Pick("p1");
        model.Changed += () => changes++;

        Assert.True(model.Cancel());

        Assert.Equal(1, changes);
        Assert.True(model.IsCancelled);
        Assert.False(model.IsConfirmed);
        Assert.True(model.IsFinished);
        Assert.Empty(model.Picks);
        Assert.Empty(model.ConfirmedPicks);
        Assert.Equal(2, model.RemainingPicks);
        Assert.Equal(PerkStatus.Available, model.Entry("p1")!.Status);
        Assert.Equal(LevelUpConfirm.AlreadyFinished, model.Confirm());
    }

    [Fact]
    public void The_unavailable_toggle_reads_back_what_was_set()
    {
        var model = Make(1);

        Assert.False(model.ShowUnavailable);
        model.ShowUnavailable = true;
        Assert.True(model.ShowUnavailable);
        model.ShowUnavailable = false;
        Assert.False(model.ShowUnavailable);
    }

    [Fact]
    public void A_choice_line_names_an_attribute_change_and_a_perk_rank_with_invariant_digits()
    {
        Assert.Equal("Luck 6 -> 7", new LevelUpChoice("a6", "Luck", 7, 6, PerkChartKind.Attribute).Line);
        Assert.Equal("Iron Fist rank 2", new LevelUpChoice("iron", "Iron Fist", 2, 1, PerkChartKind.Perk).Line);
    }

    [Fact]
    public void The_special_strip_numbers_attributes_that_have_no_head_entry()
    {
        var session = LevelUpSession.TryOpen(new Service { Snapshot = Snap(attributeHeads: false) }, false, false)!;

        Assert.Equal(new[] { "#1 5", "#2 5", "#3 5", "#4 5", "#5 5", "#6 5", "#7 5" }, session.SpecialStrip);
    }

    [Fact]
    public void A_level_above_the_cap_counts_as_at_cap_and_a_level_below_it_does_not()
    {
        var over = LevelUpSession.TryOpen(new Service { Snapshot = Snap(level: 9, cap: 7) }, false, false)!;
        var under = LevelUpSession.TryOpen(new Service { Snapshot = Snap(level: 6, cap: 7) }, false, false)!;
        var open = LevelUpSession.TryOpen(new Service { Snapshot = Snap(level: 99, cap: null) }, false, false)!;

        Assert.True(over.AtCap);
        Assert.False(over.ShowXpBar);
        Assert.Equal("Max level", over.XpText);
        Assert.False(under.AtCap);
        Assert.Equal("10 / 100 XP (10%)", under.XpText);
        Assert.False(open.AtCap);
        Assert.True(open.ShowXpBar);
    }

    [Fact]
    public void A_session_that_has_spent_nothing_is_not_all_spent_even_with_no_points()
    {
        var session = LevelUpSession.TryOpen(new Service { Snapshot = Snap(points: 0) }, false, false)!;

        Assert.False(session.AllSpent);
        Assert.Equal(string.Empty, session.StatusLine);
        Assert.Empty(session.Choices);
        Assert.Empty(session.BannerLines);
    }

    [Fact]
    public void A_refusal_message_ignores_non_positive_ticks_and_clears_at_exactly_two_seconds()
    {
        var service = new Service { Snapshot = Snap(points: 1), Result = ChooseResult.NoPoints };
        var session = LevelUpSession.TryOpen(service, false, false)!;
        var changes = 0;
        session.Changed += () => changes++;
        session.Chart.Select("iron");
        session.Handle(MenuInputAction.Accept);
        session.Handle(MenuInputAction.Accept);

        Assert.Equal("No perk points left", session.Message);
        Assert.Equal("No perk points left", session.StatusLine);
        Assert.False(session.Chart.IsAwaitingModel);
        Assert.Equal(1, changes);

        session.Tick(0);
        session.Tick(-3);
        session.Tick(double.NaN);
        session.Tick(1.5);
        Assert.Equal("No perk points left", session.Message);
        Assert.Equal(1, changes);
        session.Tick(0.5);
        Assert.Equal(string.Empty, session.Message);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void A_discarded_session_ignores_input_and_ticks()
    {
        var session = LevelUpSession.TryOpen(new Service { Snapshot = Snap(points: 1) }, false, false)!;
        session.Discard();

        Assert.True(session.IsClosed);
        Assert.False(session.Handle(MenuInputAction.Down));
        session.Tick(5);
        Assert.Equal(string.Empty, session.Message);
    }

    [Fact]
    public void The_watcher_forgets_a_cancelled_or_pre_empted_open_and_skips_one_while_the_menu_is_open()
    {
        var watcher = new LevelUpWatcher(() => true);
        var opens = 0;
        watcher.OpenRequested += () => opens++;

        watcher.LevelGained(2);
        Assert.True(watcher.IsPending);
        watcher.CancelPending();
        Assert.False(watcher.IsPending);
        watcher.Tick(5);

        watcher.LevelGained(2);
        watcher.MenuOpen = true;
        watcher.Tick(5);
        Assert.False(watcher.IsPending);

        watcher.LevelGained(4);
        Assert.False(watcher.IsPending);
        Assert.Equal(4, watcher.PerkPoints);
        watcher.MenuOpen = false;
        watcher.Tick(5);
        Assert.Equal(0, opens);
    }

    [Fact]
    public void The_watcher_open_delay_is_exact_ignores_bad_ticks_and_a_busy_retry_waits_one_second()
    {
        var safe = true;
        var watcher = new LevelUpWatcher(() => safe);
        var opens = 0;
        watcher.OpenRequested += () => opens++;

        watcher.LevelGained(1);
        watcher.Tick(0);
        watcher.Tick(-2);
        watcher.Tick(double.NaN);
        watcher.Tick(1.0);
        Assert.Equal(0, opens);
        watcher.Tick(0.5);
        Assert.Equal(1, opens);

        safe = false;
        watcher.LevelGained(1);
        watcher.Tick(1.5);
        safe = true;
        watcher.Tick(0.5);
        Assert.Equal(1, opens);
        watcher.Tick(0.5);
        Assert.Equal(2, opens);
    }

    [Fact]
    public void The_reminder_clock_restarts_after_the_menu_closes_and_reports_the_latest_points()
    {
        var watcher = new LevelUpWatcher(() => true) { AutoOpen = false };
        var reminders = new List<int>();
        watcher.ReminderDue += reminders.Add;
        watcher.LevelGained(5);

        watcher.Tick(200);
        watcher.MenuOpen = true;
        watcher.Tick(1);
        watcher.MenuOpen = false;
        watcher.Tick(299);
        Assert.Empty(reminders);
        watcher.Tick(1);
        Assert.Equal(new[] { 5 }, reminders);
    }
}
