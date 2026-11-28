using System.Text;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CrimeFlatAmountTests
{
    const string Settlers = "000044:Fallout4.esm";
    const string Guards = "000066:Fallout4.esm";
    const string Bed = "000300:a.esm";
    const string BedRef = "000301:a.esm";
    const string Actor = "000F10:a.esm";

    static readonly CrimeSettings Amounts = new(Attack: 7, Murder: 9, Pickpocket: 3, Trespass: 5, StealHorse: 11, MinValue: 13, Escape: 15);

    sealed class Facts(CrimeSettings? settings) : ICrimeFacts
    {
        public string? FactionOf(string owner) => owner == Settlers ? owner : null;

        public int ValueOf(string item) => 50;

        public double? StealMultiplier => 1;

        public CrimeSettings? Settings => settings;
    }

    sealed class ThrowingSettings : ICrimeFacts
    {
        public string? FactionOf(string owner) => owner == Settlers ? owner : null;

        public int ValueOf(string item) => 50;

        public double? StealMultiplier => 1;

        public CrimeSettings? Settings => throw new InvalidOperationException();
    }

    sealed class Source(params WitnessCandidate[] candidates) : IWitnessSource
    {
        public IReadOnlyList<WitnessCandidate> Candidates() => candidates;
    }

    static WitnessCandidate Seeing() => new("npc", true, false, 5, 20, true, Guards, false);

    static CrimeLedger Ledger(CrimeSettings? settings, IWitnessSource? source = null) =>
        new() { Facts = new Facts(settings), Witnesses = source, Formula = CrimeLedger.BountyFor };

    [Fact]
    public void Each_kind_takes_its_own_flat_amount_and_a_theft_takes_none()
    {
        Assert.Equal(3, CrimeLedger.FlatAmount(CrimeKind.Pickpocket, Amounts));
        Assert.Equal(5, CrimeLedger.FlatAmount(CrimeKind.Trespass, Amounts));
        Assert.Equal(7, CrimeLedger.FlatAmount(CrimeKind.Attack, Amounts));
        Assert.Equal(9, CrimeLedger.FlatAmount(CrimeKind.Murder, Amounts));
        Assert.Null(CrimeLedger.FlatAmount(CrimeKind.Theft, Amounts));
        Assert.Null(CrimeLedger.FlatAmount(CrimeKind.Trespass, null));
    }

    [Fact]
    public void A_pickpocket_takes_the_victim_faction_amount_when_it_is_not_zero_and_the_setting_otherwise()
    {
        Assert.Equal(25, CrimeLedger.FlatAmount(CrimeKind.Pickpocket, Amounts, 25));
        Assert.Equal(3, CrimeLedger.FlatAmount(CrimeKind.Pickpocket, Amounts, 0));
        Assert.Equal(3, CrimeLedger.FlatAmount(CrimeKind.Pickpocket, Amounts, null));
        Assert.Equal(7, CrimeLedger.FlatAmount(CrimeKind.Attack, Amounts, 25));
        Assert.Null(CrimeLedger.FlatAmount(CrimeKind.Pickpocket, null, 0));
    }

    [Fact]
    public void A_witnessed_pickpocket_adds_the_flat_amount_and_still_tallies_the_stolen_value()
    {
        var ledger = Ledger(Amounts);

        ledger.Report(CrimeKind.Pickpocket, Settlers, "000100:a.esm", 2, Actor, witnessed: true);

        Assert.Equal(3, ledger.Bounty(Settlers));
        Assert.Equal(100, ledger.StolenValue(Settlers, witnessed: true));
    }

    [Fact]
    public void A_witnessed_trespass_adds_the_flat_amount_without_any_stolen_value()
    {
        var ledger = Ledger(Amounts, new Source(Seeing()));

        var crime = ledger.Report(CrimeKind.Trespass, Settlers, Bed, 1, BedRef, witnessed: false);

        Assert.True(crime.Witnessed);
        Assert.Equal(5, ledger.Bounty(Settlers));
        Assert.Equal(0, ledger.StolenValue(Settlers, witnessed: true));
        Assert.Equal(0, ledger.StolenValue(Settlers, witnessed: false));
    }

    [Fact]
    public void An_unwitnessed_trespass_adds_no_bounty_and_no_row()
    {
        var ledger = Ledger(Amounts, new Source());

        var crime = ledger.Report(CrimeKind.Trespass, Settlers, Bed, 1, BedRef, witnessed: false);

        Assert.False(crime.Witnessed);
        Assert.Equal(0, ledger.Bounty(Settlers));
        Assert.Empty(ledger.Factions);
        Assert.Single(ledger.Events);
    }

    [Fact]
    public void A_witness_that_ignores_stealing_still_sees_a_trespass_but_not_a_theft()
    {
        var ledger = Ledger(Amounts, new Source(Seeing() with { FactionIgnoresStealing = true }));

        Assert.True(ledger.Report(CrimeKind.Trespass, Settlers, Bed, 1, BedRef, witnessed: false).Witnessed);
        Assert.False(ledger.Report(CrimeKind.Theft, Settlers, Bed, 1, BedRef, witnessed: false).Witnessed);
    }

    [Fact]
    public void A_witnessed_attack_and_murder_add_their_flat_amounts_to_the_faction_row()
    {
        var ledger = Ledger(Amounts);

        ledger.Report(CrimeKind.Attack, Settlers, Actor, 1, Actor, witnessed: true);
        ledger.Report(CrimeKind.Murder, Settlers, Actor, 1, Actor, witnessed: true);

        Assert.Equal(16, ledger.Bounty(Settlers));
        Assert.Equal(0, ledger.StolenValue(Settlers, witnessed: true));
    }

    [Fact]
    public void A_missing_negative_or_unreadable_setting_adds_nothing_and_the_event_is_still_kept()
    {
        foreach (var ledger in new[]
                 {
                     Ledger(null), Ledger(new CrimeSettings(null, null, null, null, null, null, null)),
                     Ledger(new CrimeSettings(-4, -4, -4, -4, null, null, null)), new CrimeLedger { Facts = new ThrowingSettings() },
                 })
        {
            ledger.Report(CrimeKind.Attack, Settlers, Actor, 1, Actor, witnessed: true);
            ledger.Report(CrimeKind.Murder, Settlers, Actor, 1, Actor, witnessed: true);

            Assert.Equal(0, ledger.Bounty(Settlers));
            Assert.Empty(ledger.Factions);
            Assert.Equal(2, ledger.Events.Count);
        }
    }

    [Fact]
    public void A_flat_amount_for_an_owner_with_no_faction_is_not_tallied()
    {
        var ledger = Ledger(Amounts);

        ledger.Report(CrimeKind.Attack, Actor, Actor, 1, Actor, witnessed: true);

        Assert.Empty(ledger.Factions);
    }

    [Fact]
    public void The_new_kinds_and_a_flat_bounty_survive_the_section()
    {
        var saved = Ledger(Amounts);
        saved.Report(CrimeKind.Murder, Settlers, Actor, 1, Actor, witnessed: true);

        var loaded = new CrimeLedger();
        loaded.Apply(saved.Sections());

        Assert.Equal(saved.Events, loaded.Events);
        Assert.Equal(9, loaded.Bounty(Settlers));
    }

    [Fact]
    public void A_script_crime_faction_is_kept_none_apart_from_absent_and_case_insensitive()
    {
        var overrides = new CrimeFactionOverrides();

        overrides.Set(Actor, Guards);
        overrides.Set("000F11:a.esm", null);

        Assert.True(overrides.TryGet(Actor.ToLowerInvariant(), out var faction));
        Assert.Equal(Guards, faction);
        Assert.True(overrides.TryGet("000F11:a.esm", out var none));
        Assert.Null(none);
        Assert.False(overrides.TryGet("000F12:a.esm", out _));
        Assert.Throws<ArgumentException>(() => overrides.Set(" ", Guards));

        overrides.Set(Actor, null);
        Assert.True(overrides.TryGet(Actor, out var cleared));
        Assert.Null(cleared);
    }

    [Fact]
    public void Script_crime_factions_round_trip_through_the_ledger_section_and_count_as_state()
    {
        var saved = new CrimeLedger();
        Assert.True(saved.IsEmpty);
        saved.CrimeFactions.Set(Actor, Guards);
        saved.CrimeFactions.Set("000F11:a.esm", null);
        Assert.False(saved.IsEmpty);

        var loaded = new CrimeLedger();
        loaded.Apply(saved.Sections());

        Assert.True(loaded.CrimeFactions.TryGet(Actor, out var faction));
        Assert.Equal(Guards, faction);
        Assert.True(loaded.CrimeFactions.TryGet("000F11:a.esm", out var none));
        Assert.Null(none);
        Assert.Empty(loaded.Events);
    }

    [Fact]
    public void A_section_without_overrides_loads_and_clears_the_ones_held()
    {
        var plain = new CrimeLedger { Facts = new Facts(Amounts) };
        plain.Report(CrimeKind.Theft, Settlers, "000100:a.esm", 1, BedRef, witnessed: true);
        var ledger = new CrimeLedger();
        ledger.CrimeFactions.Set(Actor, Guards);

        ledger.Apply(plain.Sections());

        Assert.True(ledger.CrimeFactions.IsEmpty);
        Assert.Single(ledger.Events);
    }

    [Fact]
    public void A_malformed_override_row_is_refused_and_leaves_the_ledger_untouched()
    {
        var ledger = new CrimeLedger();
        ledger.CrimeFactions.Set(Actor, Guards);
        var bad = Encoding.UTF8.GetBytes("{\"version\":1,\"events\":[],\"factions\":[],\"crimeFactions\":[{\"faction\":\"000066:Fallout4.esm\"}]}");

        Assert.Throws<FormatException>(() => ledger.Apply(new Dictionary<string, byte[]> { [CrimeLedger.SectionName] = bad }));

        Assert.True(ledger.CrimeFactions.TryGet(Actor, out var faction));
        Assert.Equal(Guards, faction);
    }

    [Fact]
    public void Clear_drops_the_script_crime_factions_too()
    {
        var ledger = new CrimeLedger();
        ledger.CrimeFactions.Set(Actor, Guards);

        ledger.Clear();

        Assert.True(ledger.IsEmpty);
    }

    static ActivationTarget Target(string? owner, bool noCrime = false, string refKey = BedRef, string baseKey = Bed) =>
        new(refKey, baseKey, "Furniture", new HashSet<string> { "furniture" }, 0, owner, noCrime);

    [Fact]
    public void An_owned_bed_the_player_is_not_allowed_is_a_trespass_against_its_owner()
    {
        var ledger = Ledger(Amounts, new Source(Seeing()));

        var crime = TrespassReports.Report(ledger, Target(Settlers), _ => false);

        Assert.NotNull(crime);
        Assert.Equal(CrimeKind.Trespass, crime!.Kind);
        Assert.Equal(Settlers, crime.Owner);
        Assert.Equal(Bed, crime.Item);
        Assert.Equal(BedRef, crime.Ref);
        Assert.True(crime.Witnessed);
        Assert.Equal(5, ledger.Bounty(Settlers));
    }

    [Fact]
    public void An_unowned_no_crime_or_permitted_use_reports_nothing()
    {
        var ledger = Ledger(Amounts, new Source(Seeing()));

        Assert.Null(TrespassReports.Report(ledger, Target(null), _ => false));
        Assert.Null(TrespassReports.Report(ledger, Target(" "), _ => false));
        Assert.Null(TrespassReports.Report(ledger, Target(Settlers, noCrime: true), _ => false));
        Assert.Null(TrespassReports.Report(ledger, Target(Settlers), _ => true));
        Assert.Null(TrespassReports.Report(ledger, Target(Settlers, baseKey: ""), _ => false));
        Assert.Empty(ledger.Events);
    }

    [Fact]
    public void A_witnessed_trespass_on_a_reference_is_not_charged_twice_but_an_unwitnessed_one_does_not_hide_a_later_witness()
    {
        var ledger = Ledger(Amounts, new Source(Seeing()));
        var unseen = Ledger(Amounts, new Source());

        Assert.NotNull(TrespassReports.Report(unseen, Target(Settlers), _ => false));
        unseen.Witnesses = new Source(Seeing());
        Assert.NotNull(TrespassReports.Report(unseen, Target(Settlers), _ => false));
        Assert.Null(TrespassReports.Report(unseen, Target(Settlers), _ => false));
        Assert.Equal(5, unseen.Bounty(Settlers));
        Assert.NotNull(TrespassReports.Report(ledger, Target(Settlers), _ => false));
        Assert.NotNull(TrespassReports.Report(ledger, Target(Settlers, refKey: "000302:a.esm"), _ => false));
        Assert.Equal(10, ledger.Bounty(Settlers));
    }

    [Fact]
    public void An_unwitnessed_repeat_on_a_reference_is_not_recorded_but_a_later_witnessed_one_is()
    {
        var ledger = Ledger(Amounts, new Source());

        Assert.NotNull(TrespassReports.Report(ledger, Target(Settlers), _ => false));
        Assert.Null(TrespassReports.Report(ledger, Target(Settlers), _ => false));
        Assert.Single(ledger.Events);
        ledger.Witnesses = new Source(Seeing());
        var witnessed = TrespassReports.Report(ledger, Target(Settlers), _ => false);

        Assert.NotNull(witnessed);
        Assert.True(witnessed!.Witnessed);
        Assert.Equal(2, ledger.Events.Count);
        Assert.Equal(5, ledger.Bounty(Settlers));
    }

    [Fact]
    public void A_failing_owner_check_throws_and_records_nothing()
    {
        var ledger = Ledger(Amounts);

        Assert.Throws<InvalidOperationException>(() => TrespassReports.Report(ledger, Target(Settlers), _ => throw new InvalidOperationException()));

        Assert.Empty(ledger.Events);
    }

    [Fact]
    public void The_dispatcher_tells_the_observer_after_a_handler_ran_and_not_for_an_unhandled_target()
    {
        var seen = new List<(string Ref, ActivationStatus Status)>();
        var previous = ActivationDispatcher.Dispatched;
        ActivationDispatcher.Dispatched = (target, result) => { lock (seen) seen.Add((target.RefKey, result.Status)); };
        try
        {
            var dispatcher = new ActivationDispatcher();
            dispatcher.Register(new DelegateActivationHandler(target => target.RefKey == "000A01:a.esm", _ => new ActivationResult(ActivationStatus.Handled)));

            dispatcher.Dispatch(Target(Settlers, refKey: "000A01:a.esm"));
            dispatcher.Dispatch(Target(Settlers, refKey: "000A02:a.esm"));
        }
        finally { ActivationDispatcher.Dispatched = previous; }

        lock (seen)
        {
            Assert.Contains(("000A01:a.esm", ActivationStatus.Handled), seen);
            Assert.DoesNotContain(seen, entry => entry.Ref == "000A02:a.esm");
        }
    }
}
