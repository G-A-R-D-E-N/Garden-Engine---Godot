using System.Text;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CrimeLedgerTests
{
    const string Settlers = "000044:Fallout4.esm";
    const string Gunners = "000055:Fallout4.esm";
    const string Npc = "000999:Fallout4.esm";
    const string Gun = "000100:a.esm";
    const string Ref = "000ABC:Fallout4.esm";

    sealed class Facts(IEnumerable<string> factions, int value = 10, double? multiplier = 1, int? pickpocketGold = null, bool ignoresPickpocket = false) : ICrimeFacts
    {
        readonly HashSet<string> _factions = new(factions, StringComparer.OrdinalIgnoreCase);

        public string? FactionOf(string owner) => _factions.Contains(owner) ? owner : null;

        public int ValueOf(string item) => value;

        public double? StealMultiplier => multiplier;

        public int? PickpocketCrimeGold(string faction) => pickpocketGold;

        public bool FactionIgnoresPickpocket(string faction) => ignoresPickpocket;
    }

    sealed class ThrowingFacts : ICrimeFacts
    {
        public string? FactionOf(string owner) => throw new InvalidOperationException();

        public int ValueOf(string item) => throw new InvalidOperationException();

        public double? StealMultiplier => throw new InvalidOperationException();
    }

    static CrimeLedger Ledger(ICrimeFacts? facts = null) => new() { Facts = facts ?? new Facts([Settlers]), Formula = CrimeLedger.BountyFor };

    [Fact]
    public void A_witnessed_pickpocket_adds_the_victim_faction_amount_once_and_not_per_item()
    {
        var ledger = Ledger(new Facts([Settlers], pickpocketGold: 25));

        ledger.Report(CrimeKind.Pickpocket, Settlers, Gun, 2, Ref, witnessed: true);

        Assert.Equal(25, ledger.Bounty(Settlers));
    }

    [Fact]
    public void A_zero_faction_pickpocket_amount_adds_no_bounty_and_the_event_is_kept()
    {
        var ledger = Ledger(new Facts([Settlers], pickpocketGold: 0));

        ledger.Report(CrimeKind.Pickpocket, Settlers, Gun, 2, Ref, witnessed: true);

        Assert.Equal(0, ledger.Bounty(Settlers));
        Assert.Equal(CrimeKind.Pickpocket, ledger.Events[0].Kind);
    }

    [Fact]
    public void A_faction_that_ignores_pickpocket_adds_no_bounty_but_still_logs_and_tallies_the_event()
    {
        var ledger = Ledger(new Facts([Settlers], pickpocketGold: 25, ignoresPickpocket: true));

        ledger.Report(CrimeKind.Pickpocket, Settlers, Gun, 2, Ref, witnessed: true);

        Assert.Equal(0, ledger.Bounty(Settlers));
        Assert.Equal(CrimeKind.Pickpocket, ledger.Events[0].Kind);
        Assert.Equal(20, ledger.StolenValue(Settlers, witnessed: true));
    }

    [Fact]
    public void A_witnessed_theft_from_a_faction_adds_value_times_count_times_the_setting_as_bounty()
    {
        var ledger = Ledger(new Facts([Settlers], value: 50, multiplier: 0.5));

        var crime = ledger.Report(CrimeKind.Theft, Settlers, Gun, 2, Ref, witnessed: true);

        Assert.Equal(new CrimeEvent(CrimeKind.Theft, Settlers, Settlers, Gun, 2, 100, true, Ref), crime);
        Assert.Equal(50, ledger.Bounty(Settlers));
        Assert.Equal(100, ledger.StolenValue(Settlers, witnessed: true));
        Assert.Equal(0, ledger.StolenValue(Settlers, witnessed: false));
    }

    [Fact]
    public void An_unwitnessed_theft_is_tallied_but_adds_no_bounty()
    {
        var ledger = Ledger(new Facts([Settlers], value: 50, multiplier: 1));

        ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: false);

        Assert.Equal(0, ledger.Bounty(Settlers));
        Assert.Equal(50, ledger.StolenValue(Settlers, witnessed: false));
    }

    [Fact]
    public void An_absent_steal_setting_adds_no_bounty_and_invents_no_default()
    {
        var ledger = Ledger(new Facts([Settlers], value: 50, multiplier: null));

        ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: true);

        Assert.Equal(0, ledger.Bounty(Settlers));
        Assert.Equal(50, ledger.StolenValue(Settlers, witnessed: true));
    }

    [Fact]
    public void A_trespass_with_no_flat_amount_is_recorded_but_adds_no_bounty_row_or_stolen_value()
    {
        var ledger = Ledger(new Facts([Settlers], value: 50, multiplier: 1));

        ledger.Report(CrimeKind.Trespass, Settlers, Gun, 1, Ref, witnessed: true);

        Assert.Equal(0, ledger.Bounty(Settlers));
        Assert.Single(ledger.Events);
        Assert.Empty(ledger.Factions);
        Assert.Equal(0, ledger.StolenValue(Settlers, witnessed: true));
    }

    [Fact]
    public void A_theft_from_an_owner_that_is_no_faction_is_kept_as_an_event_only()
    {
        var ledger = Ledger();

        var crime = ledger.Report(CrimeKind.Theft, Npc, Gun, 1, Ref, witnessed: true);

        Assert.Null(crime.Faction);
        Assert.Single(ledger.Events);
        Assert.Empty(ledger.Factions);
        Assert.Equal(0, ledger.Bounty(Npc));
    }

    [Fact]
    public void A_report_with_no_facts_or_throwing_facts_is_still_recorded_with_value_zero()
    {
        foreach (var facts in new ICrimeFacts?[] { null, new ThrowingFacts() })
        {
            var ledger = new CrimeLedger { Facts = facts };

            var crime = ledger.Report(CrimeKind.Theft, Settlers, Gun, 3, Ref, witnessed: true);

            Assert.Equal(new CrimeEvent(CrimeKind.Theft, Settlers, null, Gun, 3, 0, true, Ref), crime);
            Assert.Single(ledger.Events);
        }
    }

    [Fact]
    public void A_count_below_one_is_taken_as_one_and_a_huge_value_saturates()
    {
        var ledger = Ledger(new Facts([Settlers], value: int.MaxValue, multiplier: 1));

        var crime = ledger.Report(CrimeKind.Theft, Settlers, Gun, 0, Ref, witnessed: true);
        ledger.Report(CrimeKind.Theft, Settlers, Gun, 5, Ref, witnessed: true);

        Assert.Equal(1, crime.Count);
        Assert.Equal(int.MaxValue, ledger.Bounty(Settlers));
        Assert.Equal(int.MaxValue, ledger.StolenValue(Settlers, witnessed: true));
    }

    [Theory]
    [InlineData(5, 0.5, 3)]
    [InlineData(4, 0.5, 2)]
    [InlineData(100, 1.0, 100)]
    [InlineData(0, 1.0, 0)]
    [InlineData(100, 0.0, 0)]
    [InlineData(100, -1.0, 0)]
    [InlineData(100, double.NaN, 0)]
    [InlineData(100, double.PositiveInfinity, 0)]
    public void Bounty_rounds_away_from_zero_and_ignores_a_setting_that_cannot_apply(int value, double multiplier, int expected)
    {
        Assert.Equal(expected, CrimeLedger.BountyFor(value, multiplier));
    }

    [Fact]
    public void Bounty_is_zero_with_no_setting()
    {
        Assert.Equal(0, CrimeLedger.BountyFor(100, null));
    }

    [Fact]
    public void Only_the_newest_events_are_kept_past_the_cap_while_tallies_keep_counting()
    {
        var ledger = Ledger(new Facts([Settlers], value: 1, multiplier: 1));

        for (var i = 0; i < CrimeLedger.MaxEvents + 10; i++)
            ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, "r" + i, witnessed: true);

        Assert.Equal(CrimeLedger.MaxEvents, ledger.Events.Count);
        Assert.Equal("r10", ledger.Events[0].Ref);
        Assert.Equal(CrimeLedger.MaxEvents + 10, ledger.Bounty(Settlers));
    }

    [Fact]
    public void An_empty_ledger_writes_no_section_and_clear_empties_it()
    {
        var ledger = Ledger();
        Assert.Empty(ledger.Sections());

        ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: true);
        Assert.False(ledger.IsEmpty);
        ledger.Clear();

        Assert.True(ledger.IsEmpty);
    }

    [Fact]
    public void The_ledger_round_trips_through_its_section()
    {
        var saved = Ledger(new Facts([Settlers, Gunners], value: 20, multiplier: 0.5));
        saved.Report(CrimeKind.Theft, Settlers, Gun, 2, Ref, witnessed: true);
        saved.Report(CrimeKind.Theft, Gunners, Gun, 1, "r2", witnessed: false);
        saved.Report(CrimeKind.Pickpocket, Npc, Gun, 1, Npc, witnessed: true);

        var loaded = new CrimeLedger();
        loaded.Apply(saved.Sections());

        Assert.Equal(saved.Events, loaded.Events);
        Assert.Equal(saved.Factions.OrderBy(pair => pair.Key), loaded.Factions.OrderBy(pair => pair.Key));
        Assert.Equal(20, loaded.Bounty(Settlers));
        Assert.Equal(20, loaded.StolenValue(Gunners, witnessed: false));
    }

    [Fact]
    public void Applying_a_save_without_the_section_clears_the_ledger()
    {
        var ledger = Ledger();
        ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: true);

        ledger.Apply(new Dictionary<string, byte[]>());

        Assert.True(ledger.IsEmpty);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"version\":2,\"events\":[],\"factions\":[]}")]
    [InlineData("{\"version\":1,\"factions\":[]}")]
    [InlineData("{\"version\":1,\"events\":[]}")]
    [InlineData("{\"version\":1,\"events\":[{\"kind\":\"Arson\",\"owner\":\"a\",\"item\":\"b\",\"count\":1,\"value\":1,\"witnessed\":true}],\"factions\":[]}")]
    [InlineData("{\"version\":1,\"events\":[{\"kind\":\"Theft\",\"owner\":\"a\",\"item\":\"b\",\"count\":0,\"value\":1,\"witnessed\":true}],\"factions\":[]}")]
    [InlineData("{\"version\":1,\"events\":[],\"factions\":[{\"faction\":\"f\",\"bounty\":-1,\"witnessed\":0,\"unwitnessed\":0}]}")]
    public void A_malformed_or_unknown_version_section_throws_and_keeps_the_current_ledger(string json)
    {
        var ledger = Ledger();
        ledger.Report(CrimeKind.Theft, Settlers, Gun, 1, Ref, witnessed: true);

        Assert.Throws<FormatException>(() =>
            ledger.Apply(new Dictionary<string, byte[]> { [CrimeLedger.SectionName] = Encoding.UTF8.GetBytes(json) }));

        Assert.Single(ledger.Events);
    }

    static ActivationTarget Loose(string? owner, bool noCrime = false, int rank = 0, int count = 3) =>
        new(Ref, Gun, "Weapon", new HashSet<string>(), count, owner, noCrime, rank);

    [Fact]
    public void Taking_an_owned_item_the_player_may_not_have_records_an_unwitnessed_theft()
    {
        var ledger = Ledger(new Facts([Settlers], value: 7, multiplier: 1));
        var pickup = new ItemPickup(new PlayerItems(), _ => false, _ => { }, crimes: ledger);

        pickup.Activate(Loose(Settlers));

        var crime = Assert.Single(ledger.Events);
        Assert.Equal(new CrimeEvent(CrimeKind.Theft, Settlers, Settlers, Gun, 3, 21, false, Ref), crime);
        Assert.Equal(0, ledger.Bounty(Settlers));
    }

    [Fact]
    public void Taking_an_unowned_allowed_or_no_crime_item_records_nothing()
    {
        var ledger = Ledger();
        var items = new PlayerItems();

        new ItemPickup(items, _ => false, _ => { }, crimes: ledger).Activate(Loose(null));
        new ItemPickup(items, _ => true, _ => { }, crimes: ledger).Activate(Loose(Settlers));
        new ItemPickup(items, _ => false, _ => { }, crimes: ledger).Activate(Loose(Settlers, noCrime: true));

        Assert.True(ledger.IsEmpty);
    }

    [Fact]
    public void A_pickup_without_a_ledger_still_works()
    {
        var items = new PlayerItems();

        var result = new ItemPickup(items, _ => false, _ => { }).Activate(Loose(Settlers));

        Assert.Equal(ActivationStatus.Handled, result.Status);
        Assert.Equal(3, items.CountOf(Gun));
    }

    [Fact]
    public void A_runtime_joined_faction_turns_the_same_pickup_from_theft_into_a_plain_take()
    {
        var factions = new PlayerFactions();
        var ledger = Ledger();
        var pickup = new ItemPickup(new PlayerItems(), target => factions.Rank(target.Owner!, null) >= target.OwnerRank, _ => { }, crimes: ledger);

        pickup.Activate(Loose(Settlers, rank: 1));
        Assert.Single(ledger.Events);

        factions.SetRank(Settlers, 1);
        pickup.Activate(Loose(Settlers, rank: 1));
        Assert.Single(ledger.Events);

        factions.Leave(Settlers);
        pickup.Activate(Loose(Settlers, rank: 1));
        Assert.Equal(2, ledger.Events.Count);
    }

    sealed class ChestRecords(string? owner, bool owned) : IContainerRecords
    {
        public ContainerSetup? Read(string refKey, string baseKey) =>
            new("Footlocker", owned, false, new[] { new InventoryStack(Gun, 4) }, owner);
    }

    static PlacedContainerInventory Open(string? owner, bool owned, PlayerItems? player, CrimeLedger? crimes) =>
        PlacedContainerInventory.Open(Ref, "000123:Fallout4.esm", new ChestRecords(owner, owned), new ContainerLedger(), player ?? new PlayerItems(), out _, crimes: crimes)!;

    [Fact]
    public void Taking_from_a_stolen_container_records_the_count_but_putting_back_does_not()
    {
        var ledger = Ledger(new Facts([Settlers], value: 5, multiplier: 1));
        var player = new PlayerItems();
        var container = Open(Settlers, owned: true, player, ledger);

        Assert.Equal(TransferStatus.Done, container.Transfer(ContainerSide.Container, Gun, 3).Status);
        var crime = Assert.Single(ledger.Events);
        Assert.Equal(3, crime.Count);
        Assert.Equal(15, crime.Value);
        Assert.False(crime.Witnessed);

        Assert.Equal(TransferStatus.Done, container.Transfer(ContainerSide.Player, Gun, 1).Status);
        Assert.Single(ledger.Events);
    }

    [Fact]
    public void A_refused_transfer_an_unowned_container_and_a_container_with_no_owner_record_nothing()
    {
        var ledger = Ledger();

        Assert.Equal(TransferStatus.Refused, Open(Settlers, true, null, ledger).Transfer(ContainerSide.Container, Gun, 99).Status);
        Open(Settlers, owned: false, null, ledger).Transfer(ContainerSide.Container, Gun, 1);
        Open(null, owned: true, null, ledger).Transfer(ContainerSide.Container, Gun, 1);

        Assert.True(ledger.IsEmpty);
    }

    sealed class Items(int value) : IItemRecords
    {
        public ItemRecord? Find(string id) => new(id, "MISC", "Item " + id, value, 1);
    }

    sealed class Actors : IActorRecords
    {
        public ActorSetup? Read(string refKey, string baseKey) => new("Vic", false, [new(Gun, 3)], true);
    }

    [Fact]
    public void A_caught_pickpocket_records_a_witnessed_theft_and_a_clean_one_records_nothing()
    {
        var target = new PickpocketTarget(0, 0, true, false, false, false, false);
        ActorInventory Pick(CrimeLedger ledger, int roll)
        {
            var context = new PickpocketContext(new PickpocketSettings(), 5, 5, new Items(10), target, true, () => roll, Crimes: ledger);
            return ActorInventory.OpenPickpocket("npc1", "base", new Actors(), new ContainerLedger(), new PlayerItems(), "00000f:fo4.esm", _ => 1, context, out _)!;
        }

        var caughtLedger = Ledger();
        Assert.Equal(TransferStatus.Caught, Pick(caughtLedger, 99).Transfer(ContainerSide.Container, Gun, 2).Status);
        var crime = Assert.Single(caughtLedger.Events);
        Assert.Equal(CrimeKind.Pickpocket, crime.Kind);
        Assert.True(crime.Witnessed);
        Assert.Equal(2, crime.Count);
        Assert.Null(crime.Faction);

        var cleanLedger = Ledger();
        Assert.Equal(TransferStatus.Done, Pick(cleanLedger, 0).Transfer(ContainerSide.Container, Gun, 1).Status);
        Assert.True(cleanLedger.IsEmpty);
    }

    static UiDataSources Sources(float? steal)
    {
        var reader = new FakeRecordReader();
        reader.Add(Settlers, "FACT", "Settlers");
        reader.Add(Npc, "NPC_", "Vic");
        reader.Add(Gun, "WEAP", "Pipe").Scalars["WEAP\\Value"] = "50";
        if (steal is { } value)
            reader.Add("000010:a.esm", "GMST", "fCrimeGoldSteal").Scalars[PickpocketSettings.FloatPath] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new UiDataSources(reader);
    }

    [Fact]
    public void Record_facts_read_the_faction_item_value_and_steal_setting_from_the_records()
    {
        var facts = new RecordCrimeFacts(() => Sources(0.5f));

        Assert.Equal(Settlers, facts.FactionOf(Settlers));
        Assert.Null(facts.FactionOf(Npc));
        Assert.Equal(50, facts.ValueOf(Gun));
        Assert.Equal(0.5, facts.StealMultiplier);
    }

    [Fact]
    public void Record_facts_without_the_setting_or_the_sources_answer_unknown_and_never_throw()
    {
        var noSetting = new RecordCrimeFacts(() => Sources(null));
        Assert.Null(noSetting.StealMultiplier);

        foreach (var facts in new[] { new RecordCrimeFacts(() => null), new RecordCrimeFacts(() => throw new InvalidOperationException()) })
        {
            Assert.Null(facts.FactionOf(Settlers));
            Assert.Equal(0, facts.ValueOf(Gun));
            Assert.Null(facts.StealMultiplier);
        }
    }
}
