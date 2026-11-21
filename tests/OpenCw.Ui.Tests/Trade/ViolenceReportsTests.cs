using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ViolenceReportsTests
{
    const string Settlers = "000044:Fallout4.esm";
    const string Guards = "000066:Fallout4.esm";
    const string Victim = "000F10:a.esm";
    const string VictimBase = "000F11:a.esm";

    static readonly CrimeSettings Amounts = new(Attack: 7, Murder: 9, Pickpocket: 3, Trespass: 5, StealHorse: 11, MinValue: 13, Escape: 15);

    sealed class Facts : ICrimeFacts
    {
        public string? FactionOf(string owner) => owner == Victim ? Settlers : null;

        public int ValueOf(string item) => 50;

        public double? StealMultiplier => 1;

        public CrimeSettings? Settings => Amounts;
    }

    sealed class Source(params WitnessCandidate[] candidates) : IWitnessSource
    {
        public IReadOnlyList<WitnessCandidate> Candidates() => candidates;
    }

    static WitnessCandidate Seeing() => new("npc", true, false, 5, 20, true, Settlers, false);

    static CrimeLedger Ledger(params WitnessCandidate[] witnesses) =>
        new() { Facts = new Facts(), Witnesses = new Source(witnesses), Formula = CrimeLedger.BountyFor };

    // Detects: a witnessed attack on a peaceful actor that never reaches the ledger; it must add the attack bounty once however often it is hit.
    [Fact]
    public void A_witnessed_attack_is_reported_once_per_victim()
    {
        var ledger = Ledger(Seeing());
        var reports = new ViolenceReports();
        Assert.Equal(CrimeKind.Attack, reports.Attack(ledger, Victim, VictimBase, false)!.Kind);
        Assert.Null(reports.Attack(ledger, Victim, VictimBase, false));
        Assert.Equal(7, ledger.Bounty(Settlers));
        Assert.Single(ledger.Events);
    }

    [Fact]
    public void An_unwitnessed_attack_is_recorded_without_a_bounty()
    {
        var ledger = Ledger();
        var reports = new ViolenceReports();
        Assert.False(reports.Attack(ledger, Victim, VictimBase, false)!.Witnessed);
        Assert.Equal(0, ledger.Bounty(Settlers));
    }

    // Detects: a fight with an actor that was already hostile counting as a crime; it must report nothing.
    [Fact]
    public void A_hostile_victim_is_a_fair_fight()
    {
        var ledger = Ledger(Seeing());
        var reports = new ViolenceReports();
        Assert.Null(reports.Attack(ledger, Victim, VictimBase, true));
        Assert.Null(reports.Murder(ledger, Victim, VictimBase, true));
        Assert.Empty(ledger.Events);
    }

    // Detects: the murder of a victim that the first hit made hostile going unreported; an attacked victim must still count as non-hostile at death.
    [Fact]
    public void A_kill_after_an_attack_is_a_murder_even_though_the_victim_turned_hostile()
    {
        var ledger = Ledger(Seeing());
        var reports = new ViolenceReports();
        reports.Attack(ledger, Victim, VictimBase, false);
        Assert.Equal(CrimeKind.Murder, reports.Murder(ledger, Victim, VictimBase, true)!.Kind);
        Assert.Equal(16, ledger.Bounty(Settlers));
        Assert.Null(reports.Murder(ledger, Victim, VictimBase, true));
    }

    [Fact]
    public void A_fatal_first_blow_on_a_peaceful_actor_is_a_murder()
    {
        var ledger = Ledger(Seeing());
        var reports = new ViolenceReports();
        Assert.Equal(CrimeKind.Murder, reports.Murder(ledger, Victim, VictimBase, false)!.Kind);
        Assert.Equal(9, ledger.Bounty(Settlers));
    }
}
