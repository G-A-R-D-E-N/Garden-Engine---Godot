using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Terminals;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class TerminalSceneTests
{
    const string Ref = "000800:A.esp";
    const string Base = "000900:A.esp";

    static readonly IReadOnlySet<string> Terminals = new HashSet<string> { "terminals" };

    sealed class Records : ITerminalRecords
    {
        public bool HasKey { get; set; }

        public TerminalRecord? Terminal(string baseKey) =>
            new(default, default, default, default, [], [], []);

        public TerminalPlacement? Placement(string refKey) => new(Base, TerminalLockState.Password, 0, "Nope.", HasKey);
    }

    sealed class Words : IHackingWords
    {
        public IReadOnlyList<string> OfLength(int length) =>
            length == 4 ? ["CORD", "BARK", "DARK", "LARK", "PARK", "MARK", "HARK", "WORD", "FORD", "LORD", "BORD", "TORD", "NARK", "SARK", "YARK", "WARK"] : [];
    }

    sealed class Spy(ITerminalSource inner) : ITerminalSource
    {
        public IHackingSession? Last { get; private set; }

        public TerminalOpening Open(string terminalId)
        {
            var opening = inner.Open(terminalId);
            if (opening.Hack is not null) Last = opening.Hack;
            return opening;
        }

        public TerminalNode? Node(string terminalId, string nodeId) => inner.Node(terminalId, nodeId);

        public bool IsValid(string terminalId) => inner.IsValid(terminalId);

        public void EntryRun(string terminalId, string nodeId, string entryId) => inner.EntryRun(terminalId, nodeId, entryId);

        public void HackFinished(string terminalId, HackingOutcome outcome) => inner.HackFinished(terminalId, outcome);
    }

    sealed class Scene
    {
        public readonly Records Records = new();
        public readonly List<string> Refused = [];
        public readonly List<TerminalFlowModel> Shown = [];
        public double Now;
        readonly HashSet<int> _tried = [];
        public ContainerLedger Ledger = new();
        public Spy Source = null!;
        public ActivationDispatcher Dispatcher = null!;

        public Scene(ContainerLedger? ledger = null, double now = 0)
        {
            Now = now;
            Ledger = ledger ?? Ledger;
            Source = new Spy(new RecordTerminalSource(Records, new Words(), () => Now, seed: () => 7, ledger: Ledger));
            Dispatcher = new ActivationDispatcher();
            Dispatcher.Register(new TerminalActivation(Source, flow => { Shown.Add(flow); return true; }, Refused.Add));
        }

        public ActivationResult Activate() => Dispatcher.Dispatch(new ActivationTarget(Ref, Base, "Terminal", Terminals));

        public HackingBoard Board => (HackingBoard)((RecordTerminalSource.Recording)Source.Last!).Inner;

        public TerminalFlowModel EnterHack(TerminalFlowModel flow)
        {
            Assert.Equal(TerminalFlowPhase.Login, flow.Phase);
            flow.Handle(MenuInputAction.Accept);
            Assert.Equal(TerminalFlowPhase.Hacking, flow.Phase);
            flow.Hack!.Handle(MenuInputAction.Accept);
            Assert.Equal(HackingPhase.Playing, flow.Hack.Phase);
            return flow;
        }

        public HackingGuessKind Guess(TerminalFlowModel flow, bool correct)
        {
            var board = Board;
            var password = board.Password;
            for (var i = 0; i < board.Geometry.Cells; i++)
            {
                if (board.HitAt(i) is not { Kind: HackingHitKind.Word } hit || hit.Start != i) continue;
                var text = new string(Enumerable.Range(hit.Start, hit.Length).Select(board.CellAt).ToArray());
                if ((text == password) != correct || !_tried.Add(i)) continue;
                var before = board.AttemptsRemaining;
                Assert.True(flow.Hack!.Hover(i));
                flow.Hack.Handle(MenuInputAction.Accept);
                return correct ? HackingGuessKind.Correct : before == 1 ? HackingGuessKind.LockedOut : HackingGuessKind.Wrong;
            }
            throw new Xunit.Sdk.XunitException("no word to guess");
        }
    }

    [Fact]
    public void Failed_hack_locks_the_terminal_out_for_ten_seconds_and_the_lockout_survives_a_save()
    {
        var scene = new Scene();
        Assert.Equal(ActivationStatus.Handled, scene.Activate().Status);
        var flow = scene.EnterHack(scene.Shown.Single());
        Assert.Equal(4, scene.Board.MaxAttempts);
        for (var attempt = 3; attempt >= 1; attempt--)
        {
            scene.Guess(flow, correct: false);
            Assert.Equal(attempt, scene.Board.AttemptsRemaining);
            Assert.Null(scene.Ledger.LockedOutUntil(Ref));
        }
        scene.Guess(flow, correct: false);
        Assert.Equal(0, scene.Board.AttemptsRemaining);
        Assert.Equal(10, scene.Ledger.LockedOutUntil(Ref));
        Assert.Equal(HackingPhase.LockedOut, flow.Hack!.Phase);
        Assert.False(scene.Ledger.IsUnlocked(Ref));

        flow.Tick(3);
        Assert.Equal(HackingOutcome.LockedOut, flow.LastHackOutcome);
        Assert.Equal(TerminalFlowPhase.Closed, flow.Phase);
        Assert.Equal(10, scene.Ledger.LockedOutUntil(Ref));

        scene.Now = 4;
        var blocked = scene.Activate();
        Assert.Equal((ActivationStatus.Blocked, RecordTerminalOptions.LockedOutText), (blocked.Status, blocked.Detail));
        Assert.Equal([RecordTerminalOptions.LockedOutText], scene.Refused);
        Assert.Single(scene.Shown);

        var sections = ContainerStateSave.Sections(scene.Ledger, new PlayerItems(), now: scene.Now);
        var loaded = new Scene(now: 100);
        ContainerStateSave.Apply(sections, loaded.Ledger, new PlayerItems(), now: 100);
        Assert.Equal(106, loaded.Ledger.LockedOutUntil(Ref));
        loaded.Now = 105.5;
        Assert.Equal(ActivationStatus.Blocked, loaded.Activate().Status);
        Assert.Empty(loaded.Shown);
        loaded.Now = 106;
        Assert.Equal(ActivationStatus.Handled, loaded.Activate().Status);
        Assert.Equal(TerminalFlowPhase.Login, loaded.Shown.Single().Phase);

        scene.Now = 10;
        Assert.Equal(ActivationStatus.Handled, scene.Activate().Status);
        Assert.Equal(2, scene.Shown.Count);
    }

    [Fact]
    public void A_held_key_beats_the_lockout_and_a_solved_hack_stays_unlocked_through_a_save()
    {
        var scene = new Scene();
        scene.Ledger.LockOut(Ref, 50);
        Assert.Equal(ActivationStatus.Blocked, scene.Activate().Status);
        scene.Records.HasKey = true;
        Assert.Equal(ActivationStatus.Handled, scene.Activate().Status);
        Assert.Equal(TerminalFlowPhase.Menu, scene.Shown.Single().Phase);
        Assert.True(scene.Ledger.IsUnlocked(Ref));

        var solving = new Scene();
        Assert.Equal(ActivationStatus.Handled, solving.Activate().Status);
        var flow = solving.EnterHack(solving.Shown.Single());
        Assert.Equal(HackingGuessKind.Correct, solving.Guess(flow, correct: true));
        Assert.True(solving.Ledger.IsUnlocked(Ref));
        Assert.Null(solving.Ledger.LockedOutUntil(Ref));
        flow.Tick(5);
        Assert.Equal(TerminalFlowPhase.Menu, flow.Phase);

        var sections = ContainerStateSave.Sections(solving.Ledger, new PlayerItems());
        var loaded = new Scene();
        ContainerStateSave.Apply(sections, loaded.Ledger, new PlayerItems());
        Assert.True(loaded.Ledger.IsUnlocked(Ref));
        Assert.Equal(ActivationStatus.Handled, loaded.Activate().Status);
        Assert.Equal(TerminalFlowPhase.Menu, loaded.Shown.Single().Phase);
        Assert.Null(loaded.Source.Last);
    }
}
