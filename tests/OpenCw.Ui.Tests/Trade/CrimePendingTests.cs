using System.Text;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CrimePendingTests
{
    const string Settlers = "000044:Fallout4.esm";
    const string Guards = "000066:Fallout4.esm";
    const string Gun = "000100:a.esm";
    const string Ref = "000ABC:Fallout4.esm";
    const string Bed = "000300:a.esm";
    const string BedRef = "000301:a.esm";

    sealed class Flaky : ICrimeFacts
    {
        public bool Down { get; set; }

        public long Failures { get; private set; }

        public long FailureCount => Failures;

        public string? FactionOf(string owner) => Answer(owner == Settlers ? owner : null);

        public int ValueOf(string item) => Answer(50);

        public double? StealMultiplier => 1;

        public CrimeSettings? Settings => new(7, 9, 3, 5, null, null, null);

        T Answer<T>(T value)
        {
            if (!Down) return value;
            Failures++;
            return default!;
        }
    }

    sealed class Source(params WitnessCandidate[] candidates) : IWitnessSource
    {
        public IReadOnlyList<WitnessCandidate> Candidates() => candidates;
    }

    static WitnessCandidate Witness(string faction, string who = "npc") => new(who, true, false, 5, 20, true, faction, false);

    static CrimeLedger Ledger(Flaky facts, params WitnessCandidate[] witnesses) =>
        new() { Facts = facts, Witnesses = new Source(witnesses), Formula = CrimeLedger.BountyFor };

    static ActivationTarget Target(string refKey = BedRef) =>
        new(refKey, Bed, "Furniture", new HashSet<string> { "furniture" }, 0, Settlers, false);

    [Fact]
    public void AReportWhoseFactsFailAreKeptPendingAndTalliedExactlyOnceWhenTheyRecover()
    {
        var facts = new Flaky { Down = true };
        var ledger = Ledger(facts);

        var crime = ledger.Report(CrimeKind.Theft, Settlers, Gun, 2, Ref, witnessed: true);

        Assert.True(crime.Pending);
        Assert.Equal(1, ledger.PendingCount);
        Assert.Equal(0, ledger.Bounty(Settlers));
        Assert.Empty(ledger.Factions);

        facts.Down = false;
        Assert.Equal(100, ledger.Bounty(Settlers));
        Assert.Equal(100, ledger.StolenValue(Settlers, witnessed: true));
        Assert.Equal(0, ledger.PendingCount);
        var resolved = Assert.Single(ledger.Events);
        Assert.Equal(Settlers, resolved.Faction);
        Assert.Equal(100, resolved.Value);
        Assert.Equal(0, ledger.ResolvePending());
        Assert.Equal(100, ledger.Bounty(Settlers));
        Assert.Equal(100, ledger.StolenValue(Settlers, witnessed: true));
    }

    [Fact]
    public void APendingReportStaysPendingWhileTheFactsKeepFailing()
    {
        var facts = new Flaky { Down = true };
        var ledger = Ledger(facts);
        ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: true);

        Assert.Equal(0, ledger.ResolvePending());
        Assert.Equal(0, ledger.Bounty(Settlers));
        Assert.Equal(1, ledger.PendingCount);
    }

    [Fact]
    public void ThePendingStateSurvivesASaveAndResolvesOnceAfterTheLoad()
    {
        var facts = new Flaky { Down = true };
        var saved = Ledger(facts, Witness(Settlers, "guard"));
        saved.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: false);
        var loaded = Ledger(facts);

        loaded.Apply(saved.Sections());

        Assert.Equal(1, loaded.PendingCount);
        Assert.Empty(loaded.Factions);
        facts.Down = false;
        Assert.Equal(50, loaded.Bounty(Settlers));
        var crime = Assert.Single(loaded.Events);
        Assert.True(crime.Witnessed);
        Assert.Equal("guard", crime.Witness);
        Assert.False(crime.Pending);
        Assert.Equal(50, loaded.StolenValue(Settlers, witnessed: true));
        Assert.Equal(50, loaded.Bounty(Settlers));
    }

    [Fact]
    public void ALedgerSavedBeforeTheNewFieldsStillLoads()
    {
        const string old = "{\"version\":1,\"events\":[{\"kind\":\"Theft\",\"owner\":\"000044:Fallout4.esm\",\"item\":\"000100:a.esm\",\"count\":2,\"value\":100,\"witnessed\":true,\"ref\":\"000ABC:Fallout4.esm\",\"faction\":\"000044:Fallout4.esm\"}],\"factions\":[{\"faction\":\"000044:Fallout4.esm\",\"bounty\":100,\"witnessed\":100,\"unwitnessed\":0}]}";
        var ledger = new CrimeLedger();

        ledger.Apply(new Dictionary<string, byte[]> { [CrimeLedger.SectionName] = Encoding.UTF8.GetBytes(old) });

        var crime = Assert.Single(ledger.Events);
        Assert.False(crime.Pending);
        Assert.False(crime.Attempt);
        Assert.Null(crime.Witness);
        Assert.Equal(100, ledger.Bounty(Settlers));
    }

    [Fact]
    public void AWitnessFromAnotherFactionDoesNotMakeTheOwnersFactionCrimeWitnessed()
    {
        var ledger = Ledger(new Flaky(), Witness(Guards, "guard"));

        var crime = ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: false);

        Assert.False(crime.Witnessed);
        Assert.Null(crime.Witness);
        Assert.Equal(0, ledger.Bounty(Settlers));
        Assert.Equal(50, ledger.StolenValue(Settlers, witnessed: false));
    }

    [Fact]
    public void AWitnessFromTheOwnersFactionIsNamedOnTheEvent()
    {
        var ledger = Ledger(new Flaky(), Witness(Guards, "guard"), Witness(Settlers, "neighbour"));

        var crime = ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: false);

        Assert.True(crime.Witnessed);
        Assert.Equal("neighbour", crime.Witness);
        Assert.Equal(50, ledger.Bounty(Settlers));
    }

    [Fact]
    public void AWitnessSeenWhileTheFactionWasUnreadableIsJudgedAgainstTheFactionOnceItReads()
    {
        var facts = new Flaky { Down = true };
        var other = Ledger(facts, Witness(Guards));
        var same = Ledger(facts, Witness(Settlers));
        other.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: false);
        same.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: false);

        facts.Down = false;

        Assert.Equal(0, other.Bounty(Settlers));
        Assert.Equal(50, other.StolenValue(Settlers, witnessed: false));
        Assert.Equal(50, same.Bounty(Settlers));
        Assert.Equal(50, same.StolenValue(Settlers, witnessed: true));
    }

    [Fact]
    public void ACaughtPickpocketAttemptAddsNoStolenValueButAWitnessedOneStillAddsItsFlatBounty()
    {
        var ledger = Ledger(new Flaky());

        var crime = ledger.Report(CrimeKind.Pickpocket, Settlers, Gun, 2, Ref, witnessed: true, attempt: true);

        Assert.True(crime.Attempt);
        Assert.Equal(0, crime.Value);
        Assert.Equal(3, ledger.Bounty(Settlers));
        Assert.Equal(0, ledger.StolenValue(Settlers, witnessed: true));
    }

    [Fact]
    public void AWitnessedTrespassOnlySuppressesAnImmediateRepeatNotOneAfterAnotherCrime()
    {
        var ledger = Ledger(new Flaky(), Witness(Settlers));

        Assert.NotNull(TrespassReports.Report(ledger, Target(), _ => false));
        Assert.Null(TrespassReports.Report(ledger, Target(), _ => false));
        ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: false);
        Assert.NotNull(TrespassReports.Report(ledger, Target(), _ => false));
        Assert.Equal(10, ledger.Bounty(Settlers) - 50);
    }

    [Fact]
    public void RecordFactsCountAMissingSourceAsAFailureAndAReadableOneAsNone()
    {
        var down = new RecordCrimeFacts(() => null);
        var before = down.FailureCount;
        down.ValueOf(Gun);
        Assert.True(down.FailureCount > before);

        var reader = new FakeRecordReader();
        reader.Add(Gun, "WEAP", "Pipe").Scalars["WEAP\\Value"] = "50";
        var up = new RecordCrimeFacts(() => new UiDataSources(reader));
        var start = up.FailureCount;
        Assert.Equal(50, up.ValueOf(Gun));
        Assert.Equal(start, up.FailureCount);
    }

    [Fact]
    public void AnNpcOwnerWhoseFactionTheHostCouldNotReadKeepsTheCrimePendingUntilItReads()
    {
        const string Npc = "000777:a.esm";
        long failures = 0;
        var down = true;
        var reader = new FakeRecordReader();
        reader.Add(Gun, "WEAP", "Pipe").Scalars["WEAP\\Value"] = "50";
        var facts = new RecordCrimeFacts(() => new UiDataSources(reader), _ =>
        {
            if (!down) return Settlers;
            failures++;
            return null;
        }, () => failures);
        var ledger = new CrimeLedger { Facts = facts };

        var crime = ledger.Report(CrimeKind.Theft, Npc, Gun, 1, Ref, witnessed: false);

        Assert.True(crime.Pending);
        Assert.Equal(0, ledger.StolenValue(Settlers, false));
        down = false;
        Assert.Equal(1, ledger.ResolvePending());
        Assert.Equal(50, ledger.StolenValue(Settlers, false));
        Assert.Equal(0, ledger.PendingCount);
    }

    [Fact]
    public void AnNpcOwnerWithNoFactionThatReadsCleanlyIsNotPending()
    {
        var reader = new FakeRecordReader();
        reader.Add(Gun, "WEAP", "Pipe").Scalars["WEAP\\Value"] = "50";
        var facts = new RecordCrimeFacts(() => new UiDataSources(reader), _ => null, () => 0);
        var ledger = new CrimeLedger { Facts = facts };

        var crime = ledger.Report(CrimeKind.Theft, "000777:a.esm", Gun, 1, Ref, witnessed: false);

        Assert.False(crime.Pending);
        Assert.Null(crime.Faction);
    }
}
