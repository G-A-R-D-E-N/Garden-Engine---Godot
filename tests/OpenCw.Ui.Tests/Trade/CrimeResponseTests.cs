using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CrimeResponseTests
{
    const string Settlers = "000044:Fallout4.esm";
    const string Guards = "000066:Fallout4.esm";
    const string Gun = "000100:a.esm";
    const string Ref = "000ABC:Fallout4.esm";

    sealed class Facts : ICrimeFacts
    {
        public bool Down { get; set; }

        public long FailureCount { get; private set; }

        public string? FactionOf(string owner) => Answer(owner == Settlers ? owner : null);

        public int ValueOf(string item) => Answer(10);

        public double? StealMultiplier => 1;

        public CrimeSettings? Settings => new(7, 9, 3, 5, null, null, null);

        T Answer<T>(T value)
        {
            if (!Down) return value;
            FailureCount++;
            return default!;
        }
    }

    sealed class Source(params WitnessCandidate[] candidates) : IWitnessSource
    {
        public IReadOnlyList<WitnessCandidate> Candidates() => candidates;
    }

    static WitnessCandidate Witness(string who, string faction = Settlers) => new(who, true, false, 5, 20, true, faction, false);

    static (CrimeLedger Ledger, List<CrimeResponse> Heard) Ledger(Facts? facts = null, params WitnessCandidate[] witnesses)
    {
        var heard = new List<CrimeResponse>();
        return (new CrimeLedger { Facts = facts ?? new Facts(), Witnesses = new Source(witnesses), Formula = CrimeLedger.BountyFor, Responder = heard.Add }, heard);
    }

    [Theory]
    [InlineData(CrimeKind.Theft)]
    [InlineData(CrimeKind.Trespass)]
    [InlineData(CrimeKind.Attack)]
    [InlineData(CrimeKind.Murder)]
    public void EveryWitnessedCrimeKindReachesTheActorsThatSawIt(CrimeKind kind)
    {
        var (ledger, heard) = Ledger(null, Witness("a"), Witness("b"));

        ledger.Report(kind, Settlers, Gun, 1, Ref, witnessed: false);

        var response = Assert.Single(heard);
        Assert.Equal(kind, response.Kind);
        Assert.Equal(new[] { "a", "b" }, response.Actors.ToArray());
    }

    [Fact]
    public void ACrimeNobodySawAndAWitnessOfAnotherFactionReachNoActor()
    {
        var (nobody, none) = Ledger();
        nobody.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: false);
        var (other, heard) = Ledger(null, Witness("a", Guards));
        other.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: false);

        Assert.Empty(none);
        Assert.Empty(heard);
    }

    [Fact]
    public void ACaughtPickpocketReachesItsVictimAndACleanOneReachesNobody()
    {
        var (ledger, heard) = Ledger(null, Witness("bystander"));

        ledger.Report(CrimeKind.Pickpocket, "victim", Gun, 1, "victim", witnessed: false);
        Assert.Empty(heard);

        ledger.Report(CrimeKind.Pickpocket, "victim", Gun, 1, "victim", witnessed: true, attempt: true);
        var response = Assert.Single(heard);
        Assert.Equal(CrimeKind.Pickpocket, response.Kind);
        Assert.Equal(new[] { "victim" }, response.Actors.ToArray());
    }

    [Fact]
    public void APendingCrimeRespondsOnceWhenItsFactsRecover()
    {
        var facts = new Facts { Down = true };
        var (ledger, heard) = Ledger(facts, Witness("a"));

        var crime = ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: false);
        Assert.True(crime.Pending);
        Assert.Empty(heard);

        facts.Down = false;
        Assert.Equal(1, ledger.ResolvePending());
        Assert.Equal(0, ledger.ResolvePending());

        Assert.Equal(new[] { "a" }, Assert.Single(heard).Actors.ToArray());
    }

    [Fact]
    public void AThrowingResponderLeavesTheCrimeRecorded()
    {
        var (ledger, _) = Ledger(null, Witness("a"));
        ledger.Responder = _ => throw new InvalidOperationException();

        var crime = ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: false);

        Assert.True(crime.Witnessed);
        Assert.Single(ledger.Events);
    }
}
