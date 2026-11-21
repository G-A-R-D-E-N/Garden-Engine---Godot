using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;
using LeveledPickReason = Radicle.Interop.LeveledPickReason;

namespace OpenCw.Ui.Tests;

public class ReadFailureRecoveryTests
{
    const string Raider = "000010:Fallout4.esm";
    const string Outfit = "000020:Fallout4.esm";
    const string Armor = "000021:Fallout4.esm";
    const string Death = "000030:Fallout4.esm";
    const string Gun = "000100:a.esm";
    const string Caps = "00000f:fo4.esm";
    const string Id = "000001:a.esm";
    const string ModA = "000B01:Fallout4.esm";

    static readonly InventoryStack[] None = [];

    static readonly PickpocketSettings Esm = new(
        ActorSkillBase: 20, ActorSkillMult: 1, TargetSkillBase: 20, TargetSkillMult: -0.25f,
        AmountBase: 0, AmountMult: -0.1f, WeightBase: 0, WeightMult: -4, MinChance: 0, MaxChance: 90, PlantChanceMult: 1);

    static readonly PickpocketTarget Sleeper = new(0, 0, true, false, false, false, false);

    sealed class Items : IItemRecords
    {
        public int? Value { get; set; }

        public double? Weight { get; set; }

        public ItemRecord? Find(string id) => new(id, "MISC", "Item " + id, Value, Weight);
    }

    sealed class Actors : IActorRecords
    {
        public ActorSetup? Read(string refKey, string baseKey) => new("Vic", false, [new(Gun, 3), new(Caps, 5)]);
    }

    static ActorInventory OpenPickpocket(Items items, PickpocketTarget target)
    {
        var pick = new PickpocketContext(Esm, 5, 5, items, target, true, () => 0);
        var inventory = ActorInventory.OpenPickpocket("npc1", "base", new Actors(), new ContainerLedger(), new PlayerItems(), Caps, _ => 1, pick, out var refusal);
        Assert.Equal(string.Empty, refusal);
        return inventory!;
    }

    [Fact]
    public void A_failed_outfit_or_death_item_read_leaves_the_loot_plan_undecided_until_the_source_recovers()
    {
        var reader = new FakeRecordReader();
        var npc = reader.Add(Raider, "NPC_");
        npc.Links["DOFT"] = [Outfit];
        npc.Links["INAM"] = [Death];
        reader.Add(Outfit, "OTFT").Links["INAM"] = [Armor];

        reader.Failing.Add(Outfit);
        Assert.Null(NpcLootPlan.Plan(Raider, reader, id => id, _ => None));

        reader.Failing.Clear();
        reader.Failing.Add(Raider);
        Assert.Null(NpcLootPlan.Plan(Raider, reader, id => id, _ => None));

        reader.Failing.Clear();
        var plan = NpcLootPlan.Plan(Raider, reader, id => id, _ => None);
        Assert.NotNull(plan);
        Assert.Equal(new[] { new InventoryStack(Armor, 1) }, plan!.Worn);
        Assert.Equal(Death, plan.DeathItem);
    }

    [Fact]
    public void An_actor_with_no_outfit_link_is_still_a_decided_plan()
    {
        var reader = new FakeRecordReader();
        reader.Add(Raider, "NPC_");

        var plan = NpcLootPlan.Plan(Raider, reader, id => id, _ => None);

        Assert.NotNull(plan);
        Assert.Empty(plan!.Worn);
        Assert.Null(plan.DeathItem);
    }

    [Fact]
    public void Only_a_pick_or_a_genuine_empty_roll_is_a_complete_leveled_roll()
    {
        Assert.False(RadicleContainerRecords.RollFailed(LeveledPickReason.Picked));
        Assert.False(RadicleContainerRecords.RollFailed(LeveledPickReason.ChanceNone));
        Assert.False(RadicleContainerRecords.RollFailed(LeveledPickReason.NoEntryAtLevel));
        Assert.True(RadicleContainerRecords.RollFailed(LeveledPickReason.ListMissing));
        Assert.True(RadicleContainerRecords.RollFailed(LeveledPickReason.NotLeveledList));
    }

    [Fact]
    public void A_pickpocket_chance_with_an_unknown_value_or_weight_is_unknown_and_the_take_is_refused()
    {
        var items = new Items { Value = 10 };
        var inventory = OpenPickpocket(items, Sleeper);

        Assert.Equal(-1, inventory.Chance(ContainerSide.Container, Gun));
        Assert.Equal(ActorInventory.UnknownChanceText, ((IContainerInventory)inventory).Refusal(ContainerSide.Container, Gun, 1));
        Assert.Equal(TransferStatus.Refused, inventory.Transfer(ContainerSide.Container, Gun, 1).Status);
        Assert.Equal(3, inventory.ContainerStacks.Single(stack => stack.Id == Gun).Count);

        items.Value = null;
        items.Weight = 1;
        Assert.Equal(-1, inventory.Chance(ContainerSide.Container, Gun));

        items.Value = 10;
        Assert.Equal(39, inventory.Chance(ContainerSide.Container, Gun));
        Assert.Equal(string.Empty, ((IContainerInventory)inventory).Refusal(ContainerSide.Container, Gun, 1));
        Assert.Equal(TransferStatus.Done, inventory.Transfer(ContainerSide.Container, Gun, 1).Status);
    }

    [Fact]
    public void A_knocked_out_target_does_not_need_a_chance_to_be_robbed()
    {
        var inventory = OpenPickpocket(new Items(), Sleeper with { KnockState = 3 });

        Assert.Equal(string.Empty, ((IContainerInventory)inventory).Refusal(ContainerSide.Container, Gun, 1));
        Assert.Equal(TransferStatus.Done, inventory.Transfer(ContainerSide.Container, Gun, 1).Status);
    }

    [Fact]
    public void A_failed_signature_read_is_not_remembered_as_a_missing_item()
    {
        var reader = new FakeRecordReader();
        reader.Add(Id, "WEAP", "Gun").Scalars["WEAP\\Value"] = "5";
        var items = new RecordItems(reader);

        reader.Failing.Add(Id);
        Assert.Null(items.Find(Id));

        reader.Failing.Clear();
        Assert.Equal(5, items.Find(Id)!.Value);
    }

    [Fact]
    public void A_failed_weight_read_is_read_again_and_a_complete_item_is_kept()
    {
        var reader = new FakeRecordReader();
        var record = reader.Add(Id, "WEAP", "Gun");
        record.Scalars["WEAP\\Value"] = "5";
        record.Scalars["WEAP\\Weight"] = "2.5";
        var items = new RecordItems(reader);

        reader.Failing.Add(Id + "|WEAP\\Weight");
        var partial = items.Find(Id)!;
        Assert.Equal(5, partial.Value);
        Assert.Null(partial.Weight);

        reader.Failing.Clear();
        Assert.Equal(2.5, items.Find(Id)!.Weight);
        var calls = reader.SignatureCalls;
        items.Find(Id);
        Assert.Equal(calls, reader.SignatureCalls);
    }

    [Fact]
    public void A_confirmed_absent_item_is_still_remembered_without_another_read()
    {
        var reader = new FakeRecordReader();
        var items = new RecordItems(reader);

        Assert.Null(items.Find(Id));
        var calls = reader.SignatureCalls;
        Assert.Null(items.Find(Id));

        Assert.Equal(calls, reader.SignatureCalls);
    }

    [Fact]
    public void A_failed_default_mod_read_is_not_cached_so_a_later_copy_gets_the_mods()
    {
        var reader = new FakeRecordReader();
        var rifle = reader.Add(Id, "WEAP", name: "Rifle");
        rifle.Lists["WEAP\\ObjectTemplateDefault"] = ["1"];
        rifle.Lists["WEAP\\ObjectTemplateIncludeCount"] = ["1"];
        rifle.Lists["WEAP\\ObjectTemplateIncludeMod"] = ["2817"];
        rifle.RawForms[0xB01] = ModA;
        reader.Add(ModA, "OMOD");
        var defaults = new DefaultItemMods();

        reader.Failing.Add(Id + "|WEAP\\ObjectTemplateIncludeMod");
        Assert.True(defaults.For(reader, Id).IsPlain);

        reader.Failing.Clear();
        Assert.Equal(new[] { ModA }, defaults.For(reader, Id).Mods);
    }
}
