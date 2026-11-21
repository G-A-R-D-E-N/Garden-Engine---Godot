using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Terminals;
using Radicle.Interop;
using Xunit;
using ItemType = Radicle.Interop.Native.TerminalItemType;

namespace OpenCw.Ui.Tests;

public class RecordTerminalSourceTests
{
    const string Base = "000900:A.esp";
    const string Sub = "000901:A.esp";
    static readonly string Ref = RecordKey.Format("A.esp", 0x800);

    sealed class FakeRecords : ITerminalRecords
    {
        public Dictionary<string, TerminalRecord> Terms { get; } = new();
        public Dictionary<string, TerminalPlacement> Placements { get; } = new();

        public TerminalRecord? Terminal(string baseKey) => Terms.GetValueOrDefault(baseKey);

        public TerminalPlacement? Placement(string refKey) => Placements.GetValueOrDefault(refKey);
    }

    sealed class Words(params string[] words) : IHackingWords
    {
        public IReadOnlyList<string> OfLength(int length) => words.Where(w => w.Length == length).ToList();
    }

    static UiText T(string? s) => new(default, s, 0);

    static TerminalMenuEntry Item(string text, ItemType? type, ushort? id = null, FormKey? sub = null, int conditions = 0, string response = "", string page = "") =>
        new(T(text), T(response), T(page), type, null, id, null, sub, conditions);

    static TerminalRecord Term(string key, params TerminalMenuEntry[] items) =>
        new(RecordKey.TryParse(key, out var p, out var l) ? new FormKey(p, l) : default, T("Name"), T("Header"), T("Welcome\r\nLine"),
            new[] { new TerminalBodyEntry(T("hidden"), 1), new TerminalBodyEntry(T("Body\r\ntext"), 0) }, items, Array.Empty<TerminalHolotapeEntry>());

    static (RecordTerminalSource Source, FakeRecords Records, double[] Clock) Make(TerminalLockState lockState = TerminalLockState.Unlocked, int level = 0, Words? words = null)
    {
        var records = new FakeRecords();
        records.Terms[Base] = Term(Base,
            Item("Open sub", ItemType.Submenu, 1, new FormKey("A.esp", 0x901)),
            Item("Back", ItemType.ReturnToTop, 2),
            Item("Redraw", ItemType.ForceRedraw, 3),
            Item("Read", ItemType.DisplayText, 4, page: "Page\r\n1"),
            Item("Picture", ItemType.DisplayImage, 5),
            Item("Mystery", null, 6),
            Item("Gated", ItemType.DisplayText, 7, conditions: 2));
        records.Terms[Sub] = Term(Sub);
        records.Placements[Ref] = new TerminalPlacement(Base, lockState, level, "Nope.");
        var clock = new[] { 0.0 };
        return (new RecordTerminalSource(records, words ?? new Words("COLD", "CORD", "HOLD", "FORD", "WORD", "BOLD"), () => clock[0], () => 5), records, clock);
    }

    [Fact]
    public void Unknown_references_and_terminals_are_unavailable()
    {
        var (source, records, _) = Make();
        Assert.Equal(TerminalOpenStatus.Unavailable, source.Open("000999:A.esp").Status);
        records.Placements["000999:A.esp"] = new TerminalPlacement("000998:A.esp", TerminalLockState.Unlocked);
        var opening = source.Open("000999:A.esp");
        Assert.Equal(TerminalOpenStatus.Unavailable, opening.Status);
        Assert.Equal(TerminalFlowModel.DefaultFailure, opening.Reason);
    }

    [Fact]
    public void An_unlocked_terminal_opens_at_its_own_base_record()
    {
        var (source, _, _) = Make();
        var opening = source.Open(Ref);
        Assert.Equal(TerminalOpenStatus.Open, opening.Status);
        Assert.Equal(Base, opening.RootNode);
    }

    [Fact]
    public void Node_maps_entry_types_strips_carriage_returns_and_picks_the_unconditional_body()
    {
        var (source, _, _) = Make();
        var node = source.Node(Ref, Base)!;
        Assert.Equal("Header", node.Header);
        Assert.Equal("Welcome\nLine", node.Welcome);
        Assert.Equal("Body\ntext", node.Body);
        Assert.Equal(
            new[] { TerminalEntryKind.Submenu, TerminalEntryKind.ReturnToTop, TerminalEntryKind.Redraw, TerminalEntryKind.Text, TerminalEntryKind.Image, TerminalEntryKind.Unsupported, TerminalEntryKind.Text },
            node.Entries.Select(e => e.Kind));
        Assert.Equal(RecordKey.Format("A.esp", 0x901), node.Entries[0].TargetNode);
        Assert.Equal("Page\n1", node.Entries[3].PageText);
        Assert.Equal(new[] { "0", "1", "2", "3", "4", "5", "6" }, node.Entries.Select(e => e.Id));
    }

    [Fact]
    public void Entries_with_conditions_are_unavailable_and_others_are_not()
    {
        var (source, _, _) = Make();
        var node = source.Node(Ref, Base)!;
        Assert.Equal(string.Empty, node.Entries[6].Unavailable);
        Assert.All(node.Entries.Take(6), e => Assert.Null(e.Unavailable));
    }

    [Fact]
    public void An_unknown_node_is_null_and_a_missing_header_falls_back_to_the_name()
    {
        var (source, records, _) = Make();
        Assert.Null(source.Node(Ref, "000777:A.esp"));
        records.Terms[Base] = records.Terms[Base] with { HeaderText = T(null) };
        Assert.Equal("Name", source.Node(Ref, Base)!.Header);
    }

    [Fact]
    public void Running_an_entry_raises_the_event_with_its_item_id_and_unknown_entries_carry_none()
    {
        var (source, _, _) = Make();
        var runs = new List<TerminalEntryRun>();
        source.EntryRunRequested += runs.Add;
        source.EntryRun(Ref, Base, "3");
        source.EntryRun(Ref, Base, "99");
        source.EntryRun(Ref, Base, "x");
        Assert.Equal(new[] { new TerminalEntryRun(Ref, Base, 4), new TerminalEntryRun(Ref, Base, null), new TerminalEntryRun(Ref, Base, null) }, runs);
    }

    [Fact]
    public void Entry_run_without_a_listener_is_harmless_and_is_valid_defaults_true()
    {
        var (source, _, _) = Make();
        source.EntryRun(Ref, Base, "0");
        Assert.True(source.IsValid(Ref));
    }

    [Fact]
    public void The_is_valid_delegate_is_consulted()
    {
        var (_, records, clock) = Make();
        var source = new RecordTerminalSource(records, new Words(), () => clock[0], isValid: id => id != Ref);
        Assert.False(source.IsValid(Ref));
        Assert.True(source.IsValid("other"));
    }

    [Fact]
    public void A_board_asks_for_the_player_s_intelligence_each_time_and_falls_back_to_the_default_when_it_is_unknown()
    {
        string[] pool = Enumerable.Range(0, 12).SelectMany(i => new[] { new string((char)('A' + i), 1) + "ZZZ", new string((char)('A' + i), 1) + "ZZZZ" }).ToArray();
        var (_, records, _) = Make(TerminalLockState.Password, 0, new Words(pool));
        var asked = 0;
        float? live = 10f;
        var source = new RecordTerminalSource(records, new Words(pool), () => 0, () => 5, intelligence: () => { asked++; return live; });

        Assert.Equal(TerminalOpenStatus.Locked, source.Open(Ref).Status);
        live = null;
        Assert.Equal(TerminalOpenStatus.Locked, source.Open(Ref).Status);
        live = float.NaN;
        Assert.Equal(TerminalOpenStatus.Locked, source.Open(Ref).Status);
        Assert.Equal(3, asked);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 6)]
    [InlineData(2, 8)]
    public void A_password_lock_opens_a_hacking_board_with_words_sized_by_level(int level, int length)
    {
        string[] pool = Enumerable.Range(0, 12).Select(i => new string((char)('A' + i), 1) + new string('Z', length - 1)).ToArray();
        var (source, _, _) = Make(TerminalLockState.Password, level, new Words(pool));
        var opening = source.Open(Ref);
        Assert.Equal(TerminalOpenStatus.Locked, opening.Status);
        var board = Assert.IsType<HackingBoard>(Assert.IsType<RecordTerminalSource.Recording>(opening.Hack).Inner);
        Assert.Contains(board.Password, pool);
        Assert.Equal(length, board.Password.Length);
    }

    [Fact]
    public void An_odd_reference_id_adds_one_letter()
    {
        string[] pool = { "ABCDE", "ABCDF", "ABCDG" };
        string oddRef = RecordKey.Format("A.esp", 0x803);
        var (source, records, _) = Make(TerminalLockState.Password, 0, new Words(pool));
        records.Placements[oddRef] = records.Placements[Ref];
        Assert.Equal(5, ((HackingBoard)((RecordTerminalSource.Recording)source.Open(oddRef).Hack!).Inner).Password.Length);
    }

    [Fact]
    public void Too_few_words_for_the_length_makes_the_terminal_unavailable_with_a_reason()
    {
        var (source, _, _) = Make(TerminalLockState.Password, 3);
        var opening = source.Open(Ref);
        Assert.Equal(TerminalOpenStatus.Unavailable, opening.Status);
        Assert.Equal(RecordTerminalOptions.NoWordsText, opening.Reason);
    }

    [Fact]
    public void A_refused_lock_is_unavailable_with_the_placement_reason()
    {
        var (source, _, _) = Make(TerminalLockState.Refused);
        var opening = source.Open(Ref);
        Assert.Equal(TerminalOpenStatus.Unavailable, opening.Status);
        Assert.Equal("Nope.", opening.Reason);
    }

    [Fact]
    public void A_hacked_terminal_opens_afterwards_and_a_cancelled_hack_stays_locked()
    {
        var (source, _, _) = Make(TerminalLockState.Password, 0);
        source.HackFinished(Ref, HackingOutcome.Cancelled);
        Assert.Equal(TerminalOpenStatus.Locked, source.Open(Ref).Status);
        source.HackFinished(Ref, HackingOutcome.Accepted);
        Assert.Equal(TerminalOpenStatus.Open, source.Open(Ref).Status);
    }

    [Fact]
    public void A_lockout_blocks_reopening_for_ten_seconds_then_a_fresh_board_appears()
    {
        var (source, _, clock) = Make(TerminalLockState.Password, 0);
        clock[0] = 100;
        source.HackFinished(Ref, HackingOutcome.LockedOut);
        clock[0] = 109.9;
        var blocked = source.Open(Ref);
        Assert.Equal(TerminalOpenStatus.LockedOut, blocked.Status);
        Assert.Equal(RecordTerminalOptions.LockedOutText, blocked.Reason);
        clock[0] = 110;
        Assert.Equal(TerminalOpenStatus.Locked, source.Open(Ref).Status);
    }

    static bool IsPassword(HackingBoard board, HackingHit hit) =>
        new string(Enumerable.Range(hit.Start, hit.Length).Select(board.CellAt).ToArray()) == board.Password;

    static HackingHit Hit(HackingBoard board, bool password)
    {
        for (int i = 0; i < board.Geometry.Cells; i++)
            if (board.HitAt(i) is { Kind: HackingHitKind.Word } hit
                && (new string(Enumerable.Range(hit.Start, hit.Length).Select(board.CellAt).ToArray()) == board.Password) == password)
                return hit;
        throw new InvalidOperationException();
    }

    [Fact]
    public void A_lockout_is_recorded_when_the_last_guess_fails_even_if_the_menu_closes_before_it_finishes()
    {
        var (source, _, clock) = Make(TerminalLockState.Password, 0);
        var hack = (RecordTerminalSource.Recording)source.Open(Ref).Hack!;
        var board = (HackingBoard)hack.Inner;
        for (int i = 0; i < 20 && board.AttemptsRemaining > 0; i++)
            for (int c = 0; c < board.Geometry.Cells && board.AttemptsRemaining > 0; c++)
                if (board.HitAt(c) is { Kind: HackingHitKind.Word } h && !IsPassword(board, h)) hack.Select(h);
        Assert.Equal(0, board.AttemptsRemaining);
        Assert.Equal(TerminalOpenStatus.LockedOut, source.Open(Ref).Status);
        clock[0] = 10;
        Assert.Equal(TerminalOpenStatus.Locked, source.Open(Ref).Status);
    }

    [Fact]
    public void A_late_finished_report_does_not_renew_a_lockout_already_recorded_at_the_guess()
    {
        var (source, _, clock) = Make(TerminalLockState.Password, 0);
        var hack = (RecordTerminalSource.Recording)source.Open(Ref).Hack!;
        var board = (HackingBoard)hack.Inner;
        for (int c = 0; c < board.Geometry.Cells && board.AttemptsRemaining > 0; c++)
            if (board.HitAt(c) is { Kind: HackingHitKind.Word } h && !IsPassword(board, h)) hack.Select(h);
        clock[0] = 12;
        source.HackFinished(Ref, HackingOutcome.LockedOut);
        clock[0] = 13;
        Assert.Equal(TerminalOpenStatus.Locked, source.Open(Ref).Status);
    }

    [Fact]
    public void A_correct_guess_unlocks_the_terminal_without_waiting_for_the_menu_to_finish()
    {
        var (source, _, _) = Make(TerminalLockState.Password, 0);
        var hack = (RecordTerminalSource.Recording)source.Open(Ref).Hack!;
        Assert.Equal(HackingGuessKind.Correct, hack.Select(Hit((HackingBoard)hack.Inner, true)).Kind);
        Assert.Equal(TerminalOpenStatus.Open, source.Open(Ref).Status);
    }

    sealed class Verdicts(Dictionary<ushort, RuleVerdict> byFunction) : ITerminalConditions
    {
        public List<IReadOnlyList<RecordCondition>> Asked { get; } = new();

        public RuleVerdict Evaluate(IReadOnlyList<RecordCondition> conditions)
        {
            Asked.Add(conditions);
            return byFunction[conditions[0].Row.Function];
        }
    }

    static IReadOnlyList<RecipeCondition> Rows(ushort marker) =>
        new[] { new RecipeCondition(new RadicleCondition(0, 0, marker, 0, 0, 0, 1, 0, 0, null, null), null, null, null, null) };

    static (RecordTerminalSource Source, Verdicts Verdicts) Conditional(Dictionary<ushort, RuleVerdict> byFunction, bool withEvaluator = true, bool withRows = true)
    {
        var records = new FakeRecords();
        var verdicts = new Verdicts(byFunction);
        records.Terms[Base] = new TerminalRecord(new FormKey("A.esp", 0x900), T("Name"), T("Header"), T("Welcome"),
            new[]
            {
                new TerminalBodyEntry(T("first"), 1, withRows ? Rows(10) : null),
                new TerminalBodyEntry(T("second"), 1, withRows ? Rows(11) : null),
                new TerminalBodyEntry(T("fallback"), 0),
            },
            new[]
            {
                Item("Shown", ItemType.DisplayText, 1, conditions: 1) with { Conditions = withRows ? Rows(20) : null },
                Item("Hidden", ItemType.DisplayText, 2, conditions: 1) with { Conditions = withRows ? Rows(21) : null },
                Item("Grey", ItemType.DisplayText, 3, conditions: 1) with { Conditions = withRows ? Rows(22) : null },
                Item("Plain", ItemType.DisplayText, 4),
            }, Array.Empty<TerminalHolotapeEntry>());
        records.Placements[Ref] = new TerminalPlacement(Base, TerminalLockState.Unlocked);
        var source = new RecordTerminalSource(records, new Words(), () => 0, conditions: withEvaluator ? verdicts : null);
        return (source, verdicts);
    }

    static readonly Dictionary<ushort, RuleVerdict> Mixed = new()
    {
        [10] = RuleVerdict.False, [11] = RuleVerdict.True,
        [20] = RuleVerdict.True, [21] = RuleVerdict.False, [22] = RuleVerdict.Unevaluable,
    };

    [Fact]
    public void The_body_is_the_first_whose_conditions_pass()
    {
        var (source, verdicts) = Conditional(Mixed);
        Assert.Equal("second", source.Node(Ref, Base)!.Body);
        Assert.Equal(new ushort[] { 10, 11, 20, 21, 22 }, verdicts.Asked.Select(a => a[0].Row.Function));
    }

    [Fact]
    public void An_unevaluable_body_is_skipped_and_the_unconditional_one_wins()
    {
        var (source, _) = Conditional(new() { [10] = RuleVerdict.Unevaluable, [11] = RuleVerdict.False, [20] = RuleVerdict.True, [21] = RuleVerdict.True, [22] = RuleVerdict.True });
        Assert.Equal("fallback", source.Node(Ref, Base)!.Body);
    }

    [Fact]
    public void A_failing_entry_is_hidden_an_unevaluable_one_is_greyed_and_the_ids_keep_the_record_index()
    {
        var (source, _) = Conditional(Mixed);
        var node = source.Node(Ref, Base)!;
        Assert.Equal(new[] { "0", "2", "3" }, node.Entries.Select(e => e.Id));
        Assert.Equal(new[] { "Shown", "Grey", "Plain" }, node.Entries.Select(e => e.Text));
        Assert.Equal(new string?[] { null, string.Empty, null }, node.Entries.Select(e => e.Unavailable));
    }

    [Fact]
    public void Running_a_shown_entry_after_a_hidden_one_still_reports_its_own_item_id()
    {
        var (source, _) = Conditional(Mixed);
        var runs = new List<TerminalEntryRun>();
        source.EntryRunRequested += runs.Add;
        source.EntryRun(Ref, Base, "3");
        Assert.Equal(new[] { new TerminalEntryRun(Ref, Base, 4) }, runs);
    }

    [Fact]
    public void Conditions_the_record_did_not_carry_stay_unevaluable_even_with_an_evaluator()
    {
        var (source, verdicts) = Conditional(Mixed, withRows: false);
        var node = source.Node(Ref, Base)!;
        Assert.Equal("fallback", node.Body);
        Assert.Equal(new[] { "0", "1", "2", "3" }, node.Entries.Select(e => e.Id));
        Assert.Equal(new string?[] { string.Empty, string.Empty, string.Empty, null }, node.Entries.Select(e => e.Unavailable));
        Assert.Empty(verdicts.Asked);
    }

    [Fact]
    public void Without_an_evaluator_conditioned_entries_are_greyed_and_never_hidden()
    {
        var (source, _) = Conditional(Mixed, withEvaluator: false);
        var node = source.Node(Ref, Base)!;
        Assert.Equal("fallback", node.Body);
        Assert.Equal(new string?[] { string.Empty, string.Empty, string.Empty, null }, node.Entries.Select(e => e.Unavailable));
    }

    [Fact]
    public void The_flow_model_reaches_the_menu_through_the_source()
    {
        var (source, _, _) = Make();
        var flow = TerminalFlowModel.TryOpen(source, Ref, out _);
        Assert.NotNull(flow);
        Assert.NotNull(flow!.Menu);
    }

    [Fact]
    public void A_locked_flow_unlocks_the_terminal_when_the_password_is_picked()
    {
        var (source, _, _) = Make(TerminalLockState.Password, 0);
        var flow = TerminalFlowModel.TryOpen(source, Ref, out _);
        Assert.Equal(TerminalFlowPhase.Login, flow!.Phase);
        var hack = (RecordTerminalSource.Recording)source.Open(Ref).Hack!;
        Assert.Equal(TerminalOpenStatus.Locked, source.Open(Ref).Status);

        Assert.Equal(HackingGuessKind.Correct, hack.Select(Hit((HackingBoard)hack.Inner, true)).Kind);

        Assert.Equal(TerminalOpenStatus.Open, source.Open(Ref).Status);
        Assert.Equal(TerminalFlowPhase.Menu, TerminalFlowModel.TryOpen(source, Ref, out _)!.Phase);
    }
}
