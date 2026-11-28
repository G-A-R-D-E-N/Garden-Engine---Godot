using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterLevelUpSessionTests
{
    static readonly string[] Names = { "Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck" };

    sealed class FakeService : IProgressionService
    {
        public ProgressionSnapshot Snapshot { get; set; } = null!;

        public List<(string Id, int Rank)> Calls { get; } = new();

        public Func<string, int, ChooseResult>? Refuse { get; set; }

        public event Action? Changed;

        public void Publish(ProgressionSnapshot snapshot)
        {
            Snapshot = snapshot;
            Changed?.Invoke();
        }

        public ChooseResult TryChoose(string entryId, int rank)
        {
            Calls.Add((entryId, rank));
            if (Refuse is { } refuse) return refuse(entryId, rank);
            var chart = Snapshot.Chart;
            var attributes = chart.Attributes.ToArray();
            var entries = chart.Entries.Select(e =>
            {
                if (e.Id == entryId)
                {
                    if (e.Kind == PerkChartKind.Attribute) attributes[e.Column!.Value] = rank;
                    return e with { CurrentRank = rank, Available = e.Kind == PerkChartKind.Attribute ? rank < 10 : rank < e.MaxRank };
                }
                return entryId == "iron" && e.Id == "alpha" ? e with { Available = true } : e;
            }).ToList();
            Publish(Snapshot with { Chart = new PerkChartSnapshot(chart.Revision + 1, chart.Level, chart.PerkPoints - 1, attributes, entries) });
            return ChooseResult.Ok;
        }
    }

    static PerkChartEntry Attr(int column) =>
        new($"a{column}", Names[column], PerkChartKind.Attribute, column, 5, 10,
            Enumerable.Range(1, 10).Select(i => new PerkRankInfo($"{Names[column]} {i}")).ToArray(), true);

    static PerkChartEntry Perk(string id, string name, int column, int max, bool available) =>
        new(id, name, PerkChartKind.Perk, column, 0, max,
            Enumerable.Range(1, max).Select(i => new PerkRankInfo($"{name} rank {i}")).ToArray(), available);

    static ProgressionSnapshot Snap(int revision = 1, int level = 7, int points = 2, double xp = 100, double needed = 200, int? cap = null)
    {
        var entries = Enumerable.Range(0, 7).Select(Attr).ToList();
        entries.Add(Perk("iron", "Iron Fist", 0, 2, true));
        entries.Add(Perk("alpha", "Alpha", 0, 1, false));
        var chart = new PerkChartSnapshot(revision, level, points, new[] { 5, 5, 5, 5, 5, 5, 5 }, entries);
        return new ProgressionSnapshot(chart, xp, needed, cap);
    }

    static (LevelUpSession Session, FakeService Service, List<string> Sounds) Open(ProgressionSnapshot? snapshot = null)
    {
        var service = new FakeService { Snapshot = snapshot ?? Snap() };
        var session = LevelUpSession.TryOpen(service, false, false)!;
        var sounds = new List<string>();
        session.SoundRequested += sounds.Add;
        return (session, service, sounds);
    }

    static void Choose(LevelUpSession session, string id)
    {
        session.Chart.Select(id);
        session.Handle(MenuInputAction.Accept);
        session.Handle(MenuInputAction.Accept);
    }

    [Fact]
    public void Open_is_rejected_for_another_modal_pause_a_missing_service_or_an_invalid_snapshot()
    {
        var service = new FakeService { Snapshot = Snap() };

        Assert.Null(LevelUpSession.TryOpen(null, false, false));
        Assert.Null(LevelUpSession.TryOpen(service, true, false));
        Assert.Null(LevelUpSession.TryOpen(service, false, true));
        Assert.Null(LevelUpSession.TryOpen(new FakeService { Snapshot = Snap(points: 256) }, false, false));
        Assert.Null(LevelUpSession.TryOpen(new FakeService { Snapshot = Snap(level: -1) }, false, false));
        var shortAttributes = Snap() with { Chart = Snap().Chart with { Attributes = new[] { 1, 2, 3 } } };
        Assert.Null(LevelUpSession.TryOpen(new FakeService { Snapshot = shortAttributes }, false, false));
        Assert.Equal(PerkChartMode.Spend, LevelUpSession.TryOpen(service, false, false)?.Chart.Mode);
    }

    [Fact]
    public void Opening_with_points_uses_spend_mode_and_the_fanfare_and_with_none_uses_browse_mode()
    {
        var (spend, _, _) = Open();
        var (browse, _, _) = Open(Snap(points: 0));

        Assert.Equal(PerkChartMode.Spend, spend.Chart.Mode);
        Assert.Equal(new[] { "menu.open", "levelup.fanfare" }, spend.OpenSounds);
        Assert.Equal("Perk points: 2", spend.PointsLine);
        Assert.Equal("Level 7", spend.LevelLine);
        Assert.Equal(PerkChartMode.Browse, browse.Chart.Mode);
        Assert.Equal(new[] { "menu.open" }, browse.OpenSounds);
        Assert.DoesNotContain(browse.Hints, h => h.Label == "Choose");
        Assert.Contains(spend.Hints, h => h.Label == "Choose");
        Assert.Equal(new[] { "Strength 5", "Perception 5", "Endurance 5", "Charisma 5", "Intelligence 5", "Agility 5", "Luck 5" }, spend.SpecialStrip);
    }

    [Fact]
    public void Xp_text_and_bar_use_whole_numbers_clamp_and_handle_max_level_and_the_level_cap()
    {
        var (zero, _, _) = Open(Snap(xp: 0));
        Assert.Equal("0 / 200 XP (0%)", zero.XpText);
        Assert.Equal(0.0, zero.XpFraction);

        var (half, _, _) = Open(Snap(xp: 100.9));
        Assert.Equal("100 / 200 XP (50%)", half.XpText);
        Assert.Equal(100.9 / 200, half.XpFraction, 6);

        var (over, _, _) = Open(Snap(xp: 500));
        Assert.Equal(1.0, over.XpFraction);

        var (flat, _, _) = Open(Snap(needed: 0));
        Assert.Equal("Max level", flat.XpText);
        Assert.Equal(1.0, flat.XpFraction);
        Assert.True(flat.ShowXpBar);

        var (capped, _, _) = Open(Snap(level: 7, cap: 7));
        Assert.Equal("Max level", capped.XpText);
        Assert.False(capped.ShowXpBar);

    }

    [Fact]
    public void Choosing_a_perk_calls_the_service_once_spends_one_point_and_lists_it_with_new_unlocks()
    {
        var (session, service, sounds) = Open();

        Choose(session, "iron");

        Assert.Equal(new[] { ("iron", 1) }, service.Calls);
        Assert.Equal("Perk points: 1", session.PointsLine);
        Assert.Equal(new[] { "Iron Fist rank 1" }, session.Summary);
        Assert.True(session.Chart.IsNew("alpha"));
        Assert.False(session.Chart.IsNew("iron"));
        Assert.Contains("perk.first", sounds);
        Assert.False(session.Chart.IsAwaitingModel);
        Assert.Equal(PerkChartMode.Spend, session.Chart.Mode);
        Assert.False(session.AllSpent);
    }

    [Fact]
    public void Choosing_an_attribute_lists_the_before_and_after_value_and_requests_the_special_cue()
    {
        var (session, service, sounds) = Open();

        Choose(session, "a0");

        Assert.Equal(new[] { ("a0", 6) }, service.Calls);
        Assert.Equal(new[] { "Strength 5 -> 6" }, session.Summary);
        Assert.Contains("special.up", sounds);
        Assert.Equal("Strength 6", session.SpecialStrip[0]);
    }

    [Fact]
    public void The_last_point_keeps_the_menu_open_in_browse_mode_with_the_all_spent_line()
    {
        var (session, _, _) = Open(Snap(points: 1));

        Choose(session, "iron");

        Assert.False(session.IsClosed);
        Assert.True(session.AllSpent);
        Assert.Equal("All perk points spent.", session.StatusLine);
        Assert.Equal(PerkChartMode.Browse, session.Chart.Mode);
        Assert.Equal("Perk points: 0", session.PointsLine);
    }

    [Theory]
    [InlineData(ChooseResult.NoPoints, "No perk points left")]
    [InlineData(ChooseResult.Locked, "Requirements not met")]
    [InlineData(ChooseResult.AlreadyMax, "Already at maximum")]
    [InlineData(ChooseResult.Unknown, "That choice is no longer available")]
    public void A_refused_choice_shows_its_reason_for_two_seconds_and_leaves_points_and_chart_usable(ChooseResult result, string line)
    {
        var (session, service, _) = Open();
        service.Refuse = (_, _) => result;

        Choose(session, "iron");

        Assert.Equal(line, session.Message);
        Assert.Equal(line, session.StatusLine);
        Assert.Equal("Perk points: 2", session.PointsLine);
        Assert.Empty(session.Summary);
        Assert.False(session.Chart.IsAwaitingModel);
        session.Tick(1.9);
        Assert.Equal(line, session.Message);
        session.Tick(0.2);
        Assert.Equal(string.Empty, session.Message);

        session.Handle(MenuInputAction.Accept);
        Assert.True(session.Chart.IsConfirming);
    }

    [Fact]
    public void An_outside_change_refreshes_the_header_and_keeps_a_still_valid_confirmation_and_selection()
    {
        var (session, service, _) = Open();
        session.Chart.Select("iron");
        session.Handle(MenuInputAction.Accept);

        service.Publish(Snap(revision: 2, points: 3));

        Assert.True(session.Chart.IsConfirming);
        Assert.Equal("iron", session.Chart.SelectedId);
        Assert.Equal("Perk points: 3", session.PointsLine);
    }

    [Fact]
    public void An_outside_change_that_removes_availability_cancels_the_confirmation_with_a_message()
    {
        var (session, service, _) = Open();
        session.Chart.Select("iron");
        session.Handle(MenuInputAction.Accept);
        var locked = Snap(revision: 2);
        var entries = locked.Chart.Entries.Select(e => e.Id == "iron" ? e with { Available = false } : e).ToList();

        service.Publish(locked with { Chart = locked.Chart with { Entries = entries } });

        Assert.False(session.Chart.IsConfirming);
        Assert.Equal("That choice is no longer available", session.Chart.ErrorLine);
        Assert.Equal("iron", session.Chart.SelectedId);
    }

    [Fact]
    public void Gaining_a_level_or_points_while_open_updates_live_and_adds_a_header_line()
    {
        var (session, service, _) = Open();

        service.Publish(Snap(revision: 2, level: 8, points: 3));

        Assert.False(session.IsClosed);
        Assert.Equal("Level 8", session.LevelLine);
        Assert.Equal("Perk points: 3", session.PointsLine);
        Assert.Equal(new[] { "Level 8 reached" }, session.BannerLines);
        service.Publish(Snap(revision: 3, level: 8, points: 4));
        Assert.Single(session.BannerLines);
    }

    [Fact]
    public void Close_reports_remaining_points_and_choices_once_and_later_closes_and_changes_are_silent()
    {
        var (session, service, sounds) = Open();
        Choose(session, "iron");
        var closed = new List<(int Points, int Choices)>();
        session.Closed += (points, choices) => closed.Add((points, choices.Count));

        session.Chart.Handle(MenuInputAction.Back);
        session.Close();
        session.Discard();
        service.Publish(Snap(revision: 9, points: 9));

        Assert.Equal(new[] { (1, 1) }, closed);
        Assert.True(session.IsClosed);
        Assert.Contains("menu.close", sounds);
        Assert.Empty(session.Summary);
        Assert.False(session.Handle(MenuInputAction.Up));
        Assert.Equal("Perk points: 1", session.PointsLine);
    }

    [Fact]
    public void Back_while_confirming_only_cancels_the_confirmation_and_discard_closes_silently()
    {
        var (session, _, _) = Open();
        var closes = 0;
        session.Closed += (_, _) => closes++;
        session.Chart.Select("iron");
        session.Handle(MenuInputAction.Accept);

        session.Handle(MenuInputAction.Back);
        Assert.False(session.IsClosed);
        Assert.False(session.Chart.IsConfirming);

        session.Discard();
        Assert.True(session.IsClosed);
        Assert.Equal(0, closes);
    }

    [Fact]
    public void The_watcher_opens_after_the_delay_retries_while_busy_and_respects_the_setting()
    {
        var safe = true;
        var watcher = new LevelUpWatcher(() => safe);
        var opens = 0;
        watcher.OpenRequested += () => opens++;

        watcher.LevelGained(2);
        watcher.Tick(1.4);
        Assert.Equal(0, opens);
        watcher.Tick(0.2);
        Assert.Equal(1, opens);
        Assert.False(watcher.IsPending);

        safe = false;
        watcher.LevelGained(2);
        watcher.Tick(1.6);
        Assert.Equal(1, opens);
        Assert.True(watcher.IsPending);
        safe = true;
        watcher.Tick(0.9);
        Assert.Equal(1, opens);
        watcher.Tick(0.2);
        Assert.Equal(2, opens);

        watcher.AutoOpen = false;
        watcher.LevelGained(2);
        watcher.Tick(5);
        Assert.Equal(2, opens);
        Assert.Equal("Level 8 reached. Perk points: 3", LevelUpWatcher.GainedNotice(8, 3));
    }

    [Fact]
    public void The_reminder_fires_every_five_minutes_only_while_points_exist_and_the_menu_is_closed()
    {
        var watcher = new LevelUpWatcher(() => true) { AutoOpen = false, PerkPoints = 2 };
        var reminders = new List<int>();
        watcher.ReminderDue += reminders.Add;

        watcher.Tick(299);
        Assert.Empty(reminders);
        watcher.Tick(2);
        Assert.Equal(new[] { 2 }, reminders);
        watcher.Tick(300);
        Assert.Equal(2, reminders.Count);

        watcher.MenuOpen = true;
        watcher.Tick(600);
        Assert.Equal(2, reminders.Count);
        watcher.MenuOpen = false;
        watcher.PerkPoints = 0;
        watcher.Tick(600);
        Assert.Equal(2, reminders.Count);
        Assert.Equal("Perk points available. Open the level-up menu.", LevelUpWatcher.ReminderText);
    }

    [Fact]
    public void The_special_strip_survives_a_snapshot_with_fewer_than_seven_attributes()
    {
        var (session, service, _) = Open();
        service.Snapshot = Snap() with { Chart = Snap().Chart with { Attributes = new[] { 1, 2, 3 } } };

        var strip = session.SpecialStrip;

        Assert.Equal(7, strip.Count);
        Assert.Equal("Strength 1", strip[0]);
        Assert.Equal("Luck --", strip[6]);
    }

    [Fact]
    public void An_accepted_choice_for_an_entry_missing_from_the_old_snapshot_is_not_a_failure()
    {
        var (session, service, _) = Open();
        service.Snapshot = Snap() with { Chart = Snap().Chart with { Entries = new List<PerkChartEntry>() } };
        service.Refuse = (_, _) =>
        {
            service.Publish(Snap(revision: 2, points: 1));
            return ChooseResult.Ok;
        };

        Choose(session, "iron");

        Assert.Equal(string.Empty, session.Message);
        Assert.Empty(session.Summary);
        Assert.False(session.Chart.IsAwaitingModel);
        Assert.Equal("Perk points: 1", session.PointsLine);
    }

    [Fact]
    public void The_level_and_points_lines_follow_the_snapshot_the_chart_shows()
    {
        var (session, service, _) = Open();
        service.Snapshot = Snap(revision: 1, level: 9, points: 9);

        Assert.Equal("Level 7", session.LevelLine);
        Assert.Equal("Perk points: 2", session.PointsLine);
    }
}
