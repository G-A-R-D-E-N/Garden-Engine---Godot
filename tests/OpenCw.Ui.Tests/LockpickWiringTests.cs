using OpenCommonwealth.Services.Interaction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Ui.Lockpick;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using Xunit;
using LockKind = Radicle.Interop.Native.LockKind;

namespace OpenCw.Ui.Tests;

public class LockpickWiringTests
{
    const string Ref = "000ABC:Fallout4.esm";
    const string Base = "000123:Fallout4.esm";
    const string Pin = "00000A:Fallout4.esm";
    const string KeyItem = "000050:Fallout4.esm";

    sealed class Facts : ILockFacts
    {
        public LockKind Kind { get; set; }
        public bool Key { get; set; }
        public LockKind Lock(string refKey) => Kind;
        public bool HasKey(string refKey) => Key;
    }

    sealed class FakeContainers : IContainerRecords
    {
        public LockKind Kind { get; set; } = LockKind.Advanced;
        public ContainerSetup? Read(string refKey, string baseKey) =>
            new("Safe", false, Kind != LockKind.None, new[] { new InventoryStack("LOOT", 3) });
    }

    static PlayerItems Pins(int count)
    {
        var items = new PlayerItems();
        items.Add(Pin, count);
        return items;
    }

    static LockData Locked(LockKind kind) => new(true, 25, kind, FakeRecordReader.Key(KeyItem), 0, false);

    [Theory]
    [InlineData(LockKind.None, false, false, LockAccess.Open)]
    [InlineData(LockKind.Master, true, false, LockAccess.KeyUnlock)]
    [InlineData(LockKind.RequiresKey, true, false, LockAccess.KeyUnlock)]
    [InlineData(LockKind.Novice, false, false, LockAccess.Pick)]
    [InlineData(LockKind.Master, false, false, LockAccess.Pick)]
    [InlineData(LockKind.RequiresKey, false, false, LockAccess.Refused)]
    [InlineData(LockKind.Barred, false, false, LockAccess.Refused)]
    [InlineData(LockKind.Chained, false, false, LockAccess.Refused)]
    [InlineData(LockKind.RequiresTerminal, false, false, LockAccess.Refused)]
    [InlineData(LockKind.Inaccessible, false, false, LockAccess.Refused)]
    [InlineData(LockKind.RequiresKey, false, true, LockAccess.Open)]
    [InlineData(LockKind.Master, false, true, LockAccess.Open)]
    public void The_gate_opens_unlocks_picks_or_refuses_by_lock_kind_key_and_saved_unlock(LockKind kind, bool key, bool unlocked, LockAccess expected)
    {
        var ledger = new ContainerLedger();
        if (unlocked) ledger.MarkUnlocked(Ref);

        Assert.Equal(expected, LockGate.Decide(new Facts { Kind = kind, Key = key }, Ref, ledger).Access);
    }

    [Theory]
    [InlineData(LockKind.Novice, LockLevel.Novice)]
    [InlineData(LockKind.Advanced, LockLevel.Advanced)]
    [InlineData(LockKind.Expert, LockLevel.Expert)]
    [InlineData(LockKind.Master, LockLevel.Master)]
    public void A_pick_decision_carries_the_lock_level(LockKind kind, LockLevel level) =>
        Assert.Equal(level, LockGate.Decide(new Facts { Kind = kind }, Ref, new ContainerLedger()).Level);

    [Fact]
    public void Harder_locks_have_a_narrower_sweet_window()
    {
        Assert.True(BobbyPinSession.Tolerance(LockLevel.Novice) > BobbyPinSession.Tolerance(LockLevel.Advanced));
        Assert.True(BobbyPinSession.Tolerance(LockLevel.Advanced) > BobbyPinSession.Tolerance(LockLevel.Expert));
        Assert.True(BobbyPinSession.Tolerance(LockLevel.Expert) > BobbyPinSession.Tolerance(LockLevel.Master));
    }

    [Fact]
    public void A_pick_on_the_sweet_angle_turns_the_lock_open_without_breaking_a_pin()
    {
        var items = Pins(2);
        var session = new BobbyPinSession(LockLevel.Master, new PlayerPinStock(items, Pin), 30);

        session.MovePick(30);
        for (var i = 0; i < 10; i++) session.Step(1, 0.1);

        Assert.Equal(LockpickPhase.Solved, session.View.Phase);
        Assert.Equal(2, items.CountOf(Pin));
    }

    [Theory]
    [InlineData("000ABC:Fallout4.esm")]
    [InlineData("000ABD:Fallout4.esm")]
    [InlineData("0DEF01:Fallout4.esm")]
    [InlineData("123456:Fallout4.esm")]
    public void A_player_aiming_in_quarter_degree_mouse_steps_can_solve_any_lock(string lockId)
    {
        var items = Pins(1);
        var session = new BobbyPinSession(LockLevel.Master, new PlayerPinStock(items, Pin), BobbyPinSessions.SweetAngle(lockId));

        session.MovePick(Math.Round(BobbyPinSessions.SweetAngle(lockId) / 0.25) * 0.25);
        for (var i = 0; i < 10; i++) session.Step(1, 0.1);

        Assert.Equal(LockpickPhase.Solved, session.View.Phase);
        Assert.Equal(1, items.CountOf(Pin));
    }

    [Fact]
    public void Forcing_the_turn_off_the_sweet_angle_wears_the_pin_out_and_takes_it_from_the_stock()
    {
        var items = Pins(2);
        var session = new BobbyPinSession(LockLevel.Expert, new PlayerPinStock(items, Pin), 30);

        for (var i = 0; i < 30; i++) session.Step(1, 0.1);

        Assert.Equal(1, items.CountOf(Pin));
        Assert.Equal(LockpickPhase.Active, session.View.Phase);
        Assert.True(session.View.Condition < 100);
        Assert.Contains("pin_break", session.DrainCues());
        Assert.Empty(session.DrainCues());
    }

    [Fact]
    public void Solving_the_lock_raises_one_turn_cue_and_one_unlock_cue_and_each_cue_has_a_sound()
    {
        var session = new BobbyPinSession(LockLevel.Master, new PlayerPinStock(Pins(1), Pin), 0);

        for (var i = 0; i < 10; i++) session.Step(1, 0.1);

        var cues = session.DrainCues();
        Assert.Equal(new[] { "cylinder_turn", "unlock" }, cues);
        Assert.All(cues, cue => Assert.NotNull(InteractionSoundMap.LockpickEditorId(cue)));
        Assert.Equal("UILockpickingUnlock", InteractionSoundMap.LockpickEditorId("unlock"));
    }

    [Fact]
    public void A_held_turn_stuck_at_zero_does_not_repeat_the_turn_cue_but_a_new_press_does()
    {
        var session = new BobbyPinSession(LockLevel.Master, new PlayerPinStock(Pins(3), Pin), 80);

        session.Step(1, 0.05);
        session.Step(1, 0.05);
        Assert.Single(session.DrainCues(), "cylinder_turn");
        session.Step(0, 0.05);
        session.Step(1, 0.05);
        Assert.Single(session.DrainCues(), "cylinder_turn");
    }

    [Fact]
    public void Losing_the_last_pin_ends_the_attempt()
    {
        var items = Pins(1);
        var session = new BobbyPinSession(LockLevel.Expert, new PlayerPinStock(items, Pin), 30);

        for (var i = 0; i < 30; i++) session.Step(1, 0.1);

        Assert.Equal(LockpickPhase.OutOfPins, session.View.Phase);
        Assert.Equal(0, items.CountOf(Pin));
    }

    [Fact]
    public void Starting_needs_a_pin_and_the_sweet_angle_is_stable_per_lock_and_in_range()
    {
        Assert.Null(new BobbyPinSessions(null).Start(Ref, LockLevel.Novice));
        Assert.Null(new BobbyPinSessions(new PlayerPinStock(new PlayerItems(), Pin)).Start(Ref, LockLevel.Novice));
        Assert.NotNull(new BobbyPinSessions(new PlayerPinStock(Pins(1), Pin)).Start(Ref, LockLevel.Novice));

        var angle = BobbyPinSessions.SweetAngle(Ref);
        Assert.Equal(angle, BobbyPinSessions.SweetAngle(Ref.ToLowerInvariant()));
        Assert.InRange(angle, -BobbyPinSessions.SweetRange, BobbyPinSessions.SweetRange);
        Assert.NotEqual(angle, BobbyPinSessions.SweetAngle("000ABD:Fallout4.esm"));
    }

    [Fact]
    public void A_locked_container_stays_shut_until_the_ledger_unlocks_it_and_the_loot_is_unchanged()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();

        Assert.Null(PlacedContainerInventory.Open(Ref, Base, new FakeContainers(), ledger, player, out var refusal));
        Assert.Equal(PlacedContainerInventory.LockedText, refusal);

        ledger.MarkUnlocked(Ref);
        var inventory = PlacedContainerInventory.Open(Ref, Base, new FakeContainers(), ledger, player, out _);

        Assert.Equal(3, inventory!.ContainerStacks.Single().Count);
    }

    [Fact]
    public void Picking_a_container_lock_through_the_menu_unlocks_it_spends_no_pin_and_survives_save_and_load()
    {
        var reader = new FakeRecordReader();
        reader.Add(Ref, "REFR").Lock = Locked(LockKind.Advanced);
        var items = Pins(3);
        var facts = new Facts { Kind = LockKind.Advanced };
        var ledger = new ContainerLedger();
        var sessions = new BobbyPinSessions(new PlayerPinStock(items, Pin));
        var model = LockpickMenuModel.TryOpen(new RecordLockpickRules(reader, sessions), Ref, out var reason)!;
        Assert.Equal(string.Empty, reason);
        Assert.Equal("ADVANCED LOCK", model.Title);
        Assert.Equal(LockAccess.Pick, LockGate.Decide(facts, Ref, ledger).Access);

        LockpickOutcome? outcome = null;
        model.Closed += (result, _) => outcome = result;
        model.MovePickMouse(4 * BobbyPinSessions.SweetAngle(Ref));
        model.SetTurn(LockpickTurnKey.W, true);
        for (var i = 0; i < 60 && outcome is null; i++) model.Tick(0.1);

        Assert.Equal(LockpickOutcome.Unlocked, outcome);
        Assert.Equal(3, items.CountOf(Pin));
        var opened = 0;
        var finished = LockpickResults.ForContainer(ledger, Ref, () => opened++);
        finished(false);
        Assert.Equal((0, LockAccess.Pick), (opened, LockGate.Decide(facts, Ref, ledger).Access));
        finished(outcome == LockpickOutcome.Unlocked);
        Assert.Equal(1, opened);

        var loaded = new ContainerLedger();
        ContainerStateSave.Apply(ContainerStateSave.Sections(ledger, new PlayerItems()), loaded, new PlayerItems());
        Assert.Equal(LockAccess.Open, LockGate.Decide(facts, Ref, loaded).Access);
        Assert.NotNull(PlacedContainerInventory.Open(Ref, Base, new FakeContainers(), loaded, new PlayerItems(), out _));
    }

    [Theory]
    [InlineData(LockKind.RequiresKey)]
    [InlineData(LockKind.Barred)]
    public void Rules_refuse_a_lock_that_cannot_be_picked_with_no_menu(LockKind kind)
    {
        var reader = new FakeRecordReader();
        reader.Add(Ref, "REFR").Lock = Locked(kind);

        var model = LockpickMenuModel.TryOpen(new RecordLockpickRules(reader, new BobbyPinSessions(new PlayerPinStock(Pins(2), Pin))), Ref, out var reason);

        Assert.Null(model);
        Assert.Equal(new LockpickOptions().RefusedText, reason);
    }

    [Fact]
    public void No_bobby_pins_means_no_menu_and_the_out_of_pins_reason()
    {
        var reader = new FakeRecordReader();
        reader.Add(Ref, "REFR").Lock = Locked(LockKind.Novice);

        var model = LockpickMenuModel.TryOpen(new RecordLockpickRules(reader, new BobbyPinSessions(new PlayerPinStock(new PlayerItems(), Pin))), Ref, out var reason);

        Assert.Null(model);
        Assert.Equal(new LockpickOptions().OutOfPinsText, reason);
    }

    [Fact]
    public void Unlocks_alone_make_a_save_section_and_an_older_section_without_them_still_loads()
    {
        var ledger = new ContainerLedger();
        ledger.MarkUnlocked(Ref);
        ledger.MarkUnlocked("000DEF:Fallout4.esm");

        var sections = ContainerStateSave.Sections(ledger, new PlayerItems());
        var loaded = new ContainerLedger();
        loaded.MarkUnlocked("STALE");
        ContainerStateSave.Apply(sections, loaded, new PlayerItems());

        Assert.Equal(new[] { "000ABC:Fallout4.esm", "000DEF:Fallout4.esm" }, loaded.Unlocked.Order(StringComparer.Ordinal));
        var older = new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = Encoding.UTF8.GetBytes("{\"version\":1,\"player\":[],\"containers\":[]}") };
        ContainerStateSave.Apply(older, loaded, new PlayerItems());
        Assert.Empty(loaded.Unlocked);
    }

    [Theory]
    [InlineData("{\"version\":1,\"player\":[],\"containers\":[],\"unlocked\":[\"\"]}")]
    [InlineData("{\"version\":1,\"player\":[],\"containers\":[],\"unlocked\":[5]}")]
    [InlineData("{\"version\":1,\"player\":[],\"containers\":[],\"unlocked\":\"x\"}")]
    public void A_malformed_unlock_list_fails_the_load_and_changes_nothing(string json)
    {
        var ledger = new ContainerLedger();
        ledger.MarkUnlocked(Ref);
        var bad = new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = Encoding.UTF8.GetBytes(json) };

        Assert.Throws<FormatException>(() => ContainerStateSave.Apply(bad, ledger, new PlayerItems()));
        Assert.True(ledger.IsUnlocked(Ref));
    }
}
