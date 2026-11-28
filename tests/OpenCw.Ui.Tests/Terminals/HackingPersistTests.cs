using System.Text;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Terminals;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class HackingPersistTests
{
    const string Ref = "000800:A.esp";

    [Fact]
    public void Lockouts_survive_a_save_as_remaining_seconds_on_the_new_clock()
    {
        var ledger = new ContainerLedger();
        ledger.LockOut(Ref, 110);
        ledger.LockOut("000801:A.esp", 95);

        var sections = ContainerStateSave.Sections(ledger, new PlayerItems(), now: 100);
        var loaded = new ContainerLedger();
        ContainerStateSave.Apply(sections, loaded, new PlayerItems(), now: 5);

        Assert.Equal(15, loaded.LockedOutUntil(Ref));
        Assert.Null(loaded.LockedOutUntil("000801:A.esp"));
    }

    [Fact]
    public void A_ledger_with_only_a_lockout_still_saves_and_an_older_save_clears_lockouts()
    {
        var ledger = new ContainerLedger();
        ledger.LockOut(Ref, 10);
        var sections = ContainerStateSave.Sections(ledger, new PlayerItems(), now: 0);
        Assert.True(sections.ContainsKey(ContainerStateSave.SectionName));

        var loaded = new ContainerLedger();
        loaded.LockOut("STALE", 99);
        var older = new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = Encoding.UTF8.GetBytes("{\"version\":1,\"player\":[],\"containers\":[]}") };
        ContainerStateSave.Apply(older, loaded, new PlayerItems());
        Assert.Empty(loaded.Lockouts);
    }

    [Theory]
    [InlineData("[{\"ref\":\"\",\"seconds\":3}]")]
    [InlineData("[{\"ref\":\"x\",\"seconds\":0}]")]
    [InlineData("[{\"ref\":\"x\"}]")]
    [InlineData("\"x\"")]
    public void A_malformed_lockout_list_fails_the_load_and_changes_nothing(string lockouts)
    {
        var ledger = new ContainerLedger();
        ledger.LockOut(Ref, 50);
        var bad = new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = Encoding.UTF8.GetBytes("{\"version\":1,\"player\":[],\"containers\":[],\"lockouts\":" + lockouts + "}") };

        Assert.Throws<FormatException>(() => ContainerStateSave.Apply(bad, ledger, new PlayerItems()));
        Assert.Equal(50, ledger.LockedOutUntil(Ref));
    }

    [Fact]
    public void A_held_key_opens_a_locked_terminal_and_marks_it_unlocked_in_the_ledger()
    {
        var records = new FakeTerminalRecords { Lock = TerminalLockState.Password, HasKey = true };
        var ledger = new ContainerLedger();
        var source = new RecordTerminalSource(records, new NoWords(), () => 0, ledger: ledger);

        Assert.Equal(TerminalOpenStatus.Open, source.Open(Ref).Status);
        Assert.True(ledger.IsUnlocked(Ref));
    }

    [Fact]
    public void A_key_overrides_a_lockout_and_a_hack_unlock_and_lockout_land_in_the_shared_ledger()
    {
        var records = new FakeTerminalRecords { Lock = TerminalLockState.Password };
        var ledger = new ContainerLedger();
        double now = 0;
        var source = new RecordTerminalSource(records, new NoWords(), () => now, ledger: ledger);

        source.HackFinished(Ref, HackingOutcome.LockedOut);
        Assert.Equal(10, ledger.LockedOutUntil(Ref));
        Assert.Equal(TerminalOpenStatus.LockedOut, source.Open(Ref).Status);

        records.HasKey = true;
        Assert.Equal(TerminalOpenStatus.Open, source.Open(Ref).Status);

        var other = new ContainerLedger();
        var again = new RecordTerminalSource(new FakeTerminalRecords { Lock = TerminalLockState.Password }, new NoWords(), () => now, ledger: other);
        again.HackFinished(Ref, HackingOutcome.Accepted);
        Assert.True(other.IsUnlocked(Ref));
    }

    [Fact]
    public void A_key_also_opens_a_refused_lock_kind()
    {
        var ledger = new ContainerLedger();
        var source = new RecordTerminalSource(new FakeTerminalRecords { Lock = TerminalLockState.Refused, HasKey = true }, new NoWords(), () => 0, ledger: ledger);
        Assert.Equal(TerminalOpenStatus.Open, source.Open(Ref).Status);
    }

    sealed class NoWords : IHackingWords
    {
        public IReadOnlyList<string> OfLength(int length) => Array.Empty<string>();
    }

    sealed class FakeTerminalRecords : OpenCommonwealth.Services.Ui.Terminals.ITerminalRecords
    {
        public TerminalLockState Lock { get; set; }
        public bool HasKey { get; set; }

        public Radicle.Interop.TerminalRecord? Terminal(string baseKey) =>
            new(default, default, default, default, Array.Empty<Radicle.Interop.TerminalBodyEntry>(), Array.Empty<Radicle.Interop.TerminalMenuEntry>(), Array.Empty<Radicle.Interop.TerminalHolotapeEntry>());

        public TerminalPlacement? Placement(string refKey) => new("000900:A.esp", Lock, 0, "Nope.", HasKey);
    }
}
