using System.Globalization;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WitnessModelTests
{
    const string Settlers = "000044:Fallout4.esm";
    const string Guards = "000066:Fallout4.esm";
    const string Npc = "000999:Fallout4.esm";
    const string Gun = "000100:a.esm";
    const string Ref = "000ABC:Fallout4.esm";

    static WitnessCandidate Seeing() => new("npc", true, false, 5, 20, true, Guards, false);

    sealed class Source(params WitnessCandidate[] candidates) : IWitnessSource
    {
        public int Calls { get; private set; }

        public IReadOnlyList<WitnessCandidate> Candidates()
        {
            Calls++;
            return candidates;
        }
    }

    sealed class ThrowingSource : IWitnessSource
    {
        public IReadOnlyList<WitnessCandidate> Candidates() => throw new InvalidOperationException();
    }

    sealed class Facts : ICrimeFacts
    {
        public string? FactionOf(string owner) => owner == Settlers ? owner : null;

        public int ValueOf(string item) => 50;

        public double? StealMultiplier => 1;
    }

    [Fact]
    public void A_living_awake_witness_in_range_with_sight_and_a_crime_faction_sees_a_theft()
    {
        Assert.True(WitnessRules.Sees(Seeing(), CrimeKind.Theft));
        Assert.True(WitnessRules.Sees(Seeing() with { Distance = 20 }, CrimeKind.Theft));
    }

    [Fact]
    public void Any_missing_condition_means_no_witness()
    {
        var seeing = Seeing();

        Assert.False(WitnessRules.Sees(seeing with { Alive = false }, CrimeKind.Theft));
        Assert.False(WitnessRules.Sees(seeing with { Asleep = true }, CrimeKind.Theft));
        Assert.False(WitnessRules.Sees(seeing with { LineOfSight = false }, CrimeKind.Theft));
        Assert.False(WitnessRules.Sees(seeing with { Distance = 20.5 }, CrimeKind.Theft));
        Assert.False(WitnessRules.Sees(seeing with { Distance = double.NaN }, CrimeKind.Theft));
        Assert.False(WitnessRules.Sees(seeing with { CrimeFaction = null }, CrimeKind.Theft));
        Assert.False(WitnessRules.Sees(seeing with { CrimeFaction = " " }, CrimeKind.Theft));
    }

    [Fact]
    public void A_faction_that_ignores_stealing_ignores_a_theft_only()
    {
        var ignoring = Seeing() with { FactionIgnoresStealing = true };

        Assert.False(WitnessRules.Sees(ignoring, CrimeKind.Theft));
        Assert.True(WitnessRules.Sees(ignoring, CrimeKind.Trespass));
    }

    [Fact]
    public void One_witness_among_blind_ones_is_enough_and_nobody_is_not()
    {
        Assert.False(WitnessRules.AnySees([], CrimeKind.Theft));
        Assert.False(WitnessRules.AnySees([Seeing() with { Asleep = true }], CrimeKind.Theft));
        Assert.True(WitnessRules.AnySees([Seeing() with { Asleep = true }, Seeing()], CrimeKind.Theft));
    }

    [Fact]
    public void A_theft_is_witnessed_when_the_source_has_a_witness_and_feeds_the_formula()
    {
        var ledger = new CrimeLedger { Facts = new Facts(), Witnesses = new Source(Seeing()), Formula = CrimeLedger.BountyFor };

        var crime = ledger.Report(CrimeKind.Theft, Settlers, Gun, 2, Ref, witnessed: false);

        Assert.True(crime.Witnessed);
        Assert.Equal(100, ledger.StolenValue(Settlers, witnessed: true));
        Assert.Equal(0, ledger.StolenValue(Settlers, witnessed: false));
        Assert.Equal(100, ledger.Bounty(Settlers));
    }

    [Fact]
    public void A_theft_nobody_saw_or_a_failing_source_stays_unwitnessed()
    {
        foreach (IWitnessSource? source in new IWitnessSource?[] { null, new Source(), new Source(Seeing() with { Asleep = true }), new ThrowingSource() })
        {
            var ledger = new CrimeLedger { Facts = new Facts(), Witnesses = source, Formula = CrimeLedger.BountyFor };

            var crime = ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: false);

            Assert.False(crime.Witnessed);
            Assert.Equal(50, ledger.StolenValue(Settlers, witnessed: false));
            Assert.Equal(0, ledger.Bounty(Settlers));
        }
    }

    [Fact]
    public void A_caller_that_already_knows_it_was_witnessed_keeps_that_and_a_pickpocket_asks_nobody()
    {
        var source = new Source();
        var ledger = new CrimeLedger { Facts = new Facts(), Witnesses = source, Formula = CrimeLedger.BountyFor };

        Assert.True(ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: true).Witnessed);
        Assert.False(ledger.Report(CrimeKind.Pickpocket, Settlers, Gun, 1, Ref, witnessed: false).Witnessed);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void Without_a_formula_a_witnessed_theft_is_tallied_and_adds_no_bounty()
    {
        var ledger = new CrimeLedger { Facts = new Facts(), Witnesses = new Source(Seeing()) };

        var crime = ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: false);

        Assert.True(crime.Witnessed);
        Assert.Equal(50, ledger.StolenValue(Settlers, witnessed: true));
        Assert.Equal(0, ledger.Bounty(Settlers));
    }

    [Fact]
    public void A_caught_pickpocket_is_tallied_but_never_goes_through_the_steal_formula()
    {
        var ledger = new CrimeLedger { Facts = new Facts(), Formula = CrimeLedger.BountyFor };

        ledger.Report(CrimeKind.Pickpocket, Settlers, Gun, 2, Ref, witnessed: true);

        Assert.Equal(100, ledger.StolenValue(Settlers, witnessed: true));
        Assert.Equal(0, ledger.Bounty(Settlers));
    }

    static string Num(float value) => value.ToString(CultureInfo.InvariantCulture);

    static FakeRecordReader Reader()
    {
        var reader = new FakeRecordReader();
        reader.Add(Settlers, "FACT", "Settlers");
        reader.Add(Guards, "FACT", "Guards");
        reader.Add(Npc, "NPC_", "Vic");
        return reader;
    }

    [Fact]
    public void An_npc_owner_resolves_through_the_host_hook_and_a_faction_never_does()
    {
        var calls = new List<string>();
        var facts = new RecordCrimeFacts(() => new UiDataSources(Reader()), key => { calls.Add(key); return key == Npc ? Guards : null; });

        Assert.Equal(Settlers, facts.FactionOf(Settlers));
        Assert.Equal(Guards, facts.FactionOf(Npc));
        Assert.Equal(new[] { Npc }, calls.ToArray());
    }

    [Fact]
    public void A_throwing_host_hook_answers_no_faction()
    {
        var facts = new RecordCrimeFacts(() => new UiDataSources(Reader()), _ => throw new InvalidOperationException());

        Assert.Null(facts.FactionOf(Npc));
    }

    [Fact]
    public void A_faction_set_to_use_defaults_takes_the_game_setting_and_any_other_takes_its_own_multiplier()
    {
        var reader = Reader();
        reader.Records[Settlers].Scalars[RecordCrimeFacts.FactionFlagsPath] = RecordCrimeFacts.UseDefaultsFlag.ToString(CultureInfo.InvariantCulture);
        reader.Records[Guards].Scalars[RecordCrimeFacts.FactionFlagsPath] = "0";
        reader.Records[Guards].Scalars[RecordCrimeFacts.FactionStealPath] = Num(2.5f);
        reader.Add("000010:a.esm", "GMST", "fCrimeGoldSteal").Scalars[PickpocketSettings.FloatPath] = Num(0.5f);
        var facts = new RecordCrimeFacts(() => new UiDataSources(reader));

        Assert.Equal(0.5, facts.StealMultiplierFor(Settlers));
        Assert.Equal(2.5, facts.StealMultiplierFor(Guards));
    }

    [Fact]
    public void An_unreadable_faction_row_has_no_multiplier_and_no_fallback()
    {
        var reader = Reader();
        reader.Records[Guards].Scalars[RecordCrimeFacts.FactionFlagsPath] = "0";
        var facts = new RecordCrimeFacts(() => new UiDataSources(reader));

        Assert.Null(facts.StealMultiplierFor(Settlers));
        Assert.Null(facts.StealMultiplierFor(Guards));
        Assert.Null(new RecordCrimeFacts(() => null).StealMultiplierFor(Guards));
    }

    [Fact]
    public void The_crime_gold_integers_come_from_the_integer_rows_and_an_absent_one_is_null()
    {
        var reader = Reader();
        reader.Add("000011:a.esm", "GMST", "iCrimeGoldAttack").Scalars[RecordCrimeFacts.IntegerPath] = "40";
        reader.Add("000012:a.esm", "GMST", "iCrimeGoldTrespass").Scalars[RecordCrimeFacts.IntegerPath] = "5";
        reader.Add("000013:a.esm", "GMST", "iCrimeGoldMinValue").Scalars[RecordCrimeFacts.IntegerPath] = "not a number";
        ICrimeFacts facts = new RecordCrimeFacts(() => new UiDataSources(reader));

        Assert.Equal(new CrimeSettings(40, null, null, 5, null, null, null), facts.Settings);
        Assert.Null(new RecordCrimeFacts(() => null).Settings);
    }

    [Fact]
    public void The_ignore_stealing_flag_is_bit_nine_and_an_unreadable_row_does_not_ignore()
    {
        var reader = Reader();
        reader.Records[Settlers].Scalars[RecordCrimeFacts.FactionFlagsPath] = (1u << 9).ToString(CultureInfo.InvariantCulture);
        reader.Records[Guards].Scalars[RecordCrimeFacts.FactionFlagsPath] = (1u << 12).ToString(CultureInfo.InvariantCulture);

        Assert.True(RecordCrimeFacts.IgnoresStealing(reader, Settlers));
        Assert.False(RecordCrimeFacts.IgnoresStealing(reader, Guards));
        Assert.False(RecordCrimeFacts.IgnoresStealing(reader, Npc));
    }

    [Fact]
    public void The_ignore_pickpocket_flag_is_bit_thirteen_and_an_unreadable_row_does_not_ignore()
    {
        var reader = Reader();
        reader.Records[Settlers].Scalars[RecordCrimeFacts.FactionFlagsPath] = (1u << 13).ToString(CultureInfo.InvariantCulture);
        reader.Records[Guards].Scalars[RecordCrimeFacts.FactionFlagsPath] = (1u << 9).ToString(CultureInfo.InvariantCulture);

        Assert.True(RecordCrimeFacts.IgnoresPickpocket(reader, Settlers));
        Assert.False(RecordCrimeFacts.IgnoresPickpocket(reader, Guards));
        Assert.False(RecordCrimeFacts.IgnoresPickpocket(reader, Npc));
        Assert.True(new RecordCrimeFacts(() => new UiDataSources(reader)).FactionIgnoresPickpocket(Settlers));
        Assert.False(new RecordCrimeFacts(() => null).FactionIgnoresPickpocket(Settlers));
    }

    [Fact]
    public void The_pickpocket_crime_gold_is_the_faction_u16_and_an_unreadable_one_is_null()
    {
        var reader = Reader();
        reader.Records[Settlers].Scalars[RecordCrimeFacts.FactionPickpocketPath] = "25";
        reader.Records[Guards].Scalars[RecordCrimeFacts.FactionPickpocketPath] = "0";
        reader.Records[Npc].Scalars[RecordCrimeFacts.FactionPickpocketPath] = "70000";
        var facts = new RecordCrimeFacts(() => new UiDataSources(reader));

        Assert.Equal((int?)25, facts.PickpocketCrimeGold(Settlers));
        Assert.Equal((int?)0, facts.PickpocketCrimeGold(Guards));
        Assert.Null(facts.PickpocketCrimeGold(Npc));
        Assert.Null(facts.PickpocketCrimeGold("000777:a.esm"));
        Assert.Null(new RecordCrimeFacts(() => null).PickpocketCrimeGold(Settlers));
    }
}
