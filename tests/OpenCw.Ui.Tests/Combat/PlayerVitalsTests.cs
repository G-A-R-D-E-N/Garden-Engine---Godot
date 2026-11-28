using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.DevConsole;
using OpenCommonwealth.Services.Ui.Gallery;
using OpenCommonwealth.Services.Ui.Vats;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PlayerVitalsTests
{
    const string HealthAv = "000010:Test.esm", ApAv = "000011:Test.esm", RadsAv = "000012:Test.esm",
        Heal = "000020:Test.esm", Regen = "000021:Test.esm", Poison = "000022:Test.esm", Stim = "000023:Test.esm",
        Rads = "000024:Test.esm", Burst = "000025:Test.esm", Peak = "000027:Test.esm", Missing = "000026:Test.esm",
        Tonic = "000030:Test.esm", Mixed = "000031:Test.esm", Slow = "000032:Test.esm", Stimpak = "000033:Test.esm",
        StimHostile = "000034:Test.esm", VanillaFood = "00002B:Test.esm", VanillaRegen = "00002C:Test.esm", RecoverHeal = "00002D:Test.esm", StimDfob = "000040:Test.esm", RadAwayDfob = "000041:Test.esm",
        LuckAv = "000013:Test.esm", OtherAv = "000028:Test.esm", RadsCompanion = "000029:Test.esm", RadsDetrimental = "00002A:Test.esm",
        BuffLuck = "00002E:Test.esm", BuffLuckDetrimental = "00002F:Test.esm", BuffHealthDetrimental = "000035:Test.esm", BuffHostile = "000036:Test.esm",
        BuffRads = "000037:Test.esm", BuffAp = "000038:Test.esm", Conditioned = "000039:Test.esm",
        RadAway = "000050:Test.esm", Buffout = "000051:Test.esm";

    static FakeRecordReader Reader()
    {
        var reader = new FakeRecordReader();
        reader.Add(HealthAv, "AVIF", "Health");
        reader.Add(ApAv, "AVIF", "ActionPoints");
        reader.Add(RadsAv, "AVIF", "Rads");
        reader.Add(LuckAv, "AVIF", "Luck");
        Mgef(reader, Heal, 0, 0, HealthAv);
        Mgef(reader, Regen, 0, 0, ApAv);
        Mgef(reader, Poison, 0, 4, HealthAv);
        Mgef(reader, Peak, 34, 0, HealthAv);
        Mgef(reader, Stim, 31, 0, null);
        Mgef(reader, StimHostile, 31, 1, null);
        Mgef(reader, Rads, 0, 0, RadsAv);
        Mgef(reader, Burst, 0, 1, HealthAv);
        Mgef(reader, VanillaFood, 0, 0x380800, HealthAv);
        Mgef(reader, VanillaRegen, 0, 0x200800, ApAv);
        Mgef(reader, RecoverHeal, 0, 0x2, HealthAv);
        Mgef(reader, OtherAv, 0, 0, LuckAv);
        Mgef(reader, RadsCompanion, 0, 0, RadsAv, 2);
        Mgef(reader, RadsDetrimental, 0, 0x4000804, RadsAv);
        Mgef(reader, BuffLuck, 34, 0, LuckAv);
        Mgef(reader, BuffLuckDetrimental, 34, 0x4, LuckAv);
        Mgef(reader, BuffHealthDetrimental, 34, 0x4, HealthAv);
        Mgef(reader, BuffHostile, 34, 0x1, LuckAv);
        Mgef(reader, BuffRads, 34, 0, RadsAv);
        Mgef(reader, BuffAp, 34, 0x800, ApAv);
        Mgef(reader, Conditioned, 34, 0, LuckAv, 1);
        return reader;
    }

    static void Mgef(FakeRecordReader reader, string id, uint archetype, uint flags, string? actorValue, int conditions = 0)
    {
        var record = reader.Add(id, "MGEF", "mg_" + id);
        record.Conditions = conditions;
        record.Scalars["MGEF\\Archetype"] = archetype.ToString();
        record.Scalars["MGEF\\Flags"] = flags.ToString();
        record.Scalars["MGEF\\ActorValue"] = actorValue ?? string.Empty;
    }

    static PlayerVitals Vitals(double health = 50) => new(100, health, 90, 40);

    static ConsumableEffect Effect(string id, double magnitude = 20, int duration = 0, bool conditional = false) =>
        new(id, magnitude, duration, conditional);

    [Fact]
    public void ConstructionClampsToTheMaximaAndCleansBadInput()
    {
        var vitals = new PlayerVitals(100, 150, double.NaN, -5);
        Assert.Equal((100d, 100d), (vitals.Health, vitals.MaxHealth));
        Assert.Equal((0d, 0d), (vitals.ActionPoints, vitals.MaxActionPoints));
    }

    [Fact]
    public void HealthAndActionPointsClampAtZeroAndTheMaximumAndRaiseChangedOnlyWhenTheValueMoves()
    {
        var vitals = Vitals();
        var changes = 0;
        vitals.Changed += () => changes++;
        vitals.AddHealth(70);
        Assert.Equal(100, vitals.Health);
        vitals.AddHealth(10);
        vitals.AddHealth(double.NaN);
        Assert.Equal(1, changes);
        vitals.AddHealth(-500);
        Assert.Equal(0, vitals.Health);
        vitals.AddActionPoints(500);
        Assert.Equal(90, vitals.ActionPoints);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void SpendingActionPointsClampsAtZeroAndIgnoresNonPositiveCosts()
    {
        var vitals = Vitals();
        vitals.SpendActionPoints(15);
        Assert.Equal(25, vitals.ActionPoints);
        vitals.SpendActionPoints(0);
        vitals.SpendActionPoints(-9);
        vitals.SpendActionPoints(double.PositiveInfinity);
        Assert.Equal(25, vitals.ActionPoints);
        vitals.SpendActionPoints(99);
        Assert.Equal(0, vitals.ActionPoints);
    }

    [Fact]
    public void AnMgefIsReadThroughTheRecordReaderWithItsActorValueEditorId()
    {
        var info = new RecordMagicEffects(Reader()).Of(Heal);
        Assert.Equal(new MagicEffectInfo(0, 0, "Health"), info);
        Assert.Equal(new MagicEffectInfo(31, 0, null), new RecordMagicEffects(Reader()).Of(Stim));
        Assert.Equal(new MagicEffectInfo(0, 0, "Rads", 2), new RecordMagicEffects(Reader()).Of(RadsCompanion));
    }

    [Fact]
    public void ARecordThatIsNotAnMgefOrHasAnUnreadableFieldIsUnreadable()
    {
        var reader = Reader();
        reader.Records[Heal].Scalars["MGEF\\Flags"] = "not a number";
        var records = new RecordMagicEffects(reader);
        Assert.Null(records.Of(Heal));
        Assert.Null(records.Of(HealthAv));
        Assert.Null(records.Of(Missing));
        reader.Records[Regen].Scalars.Remove("MGEF\\Archetype");
        Assert.Null(records.Of(Regen));
        reader.Records[Rads].Conditions = null;
        Assert.Null(records.Of(Rads));
    }

    [Fact]
    public void AnInstantHealthValueModifierAddsItsMagnitudeAndClampsToTheMaximum()
    {
        var vitals = Vitals(50);
        Assert.True(VitalsEffects.Apply(vitals, [Effect(Heal, 20)], new RecordMagicEffects(Reader())));
        Assert.Equal(70, vitals.Health);
        Assert.True(VitalsEffects.Apply(vitals, [Effect(Heal, 500)], new RecordMagicEffects(Reader())));
        Assert.Equal(100, vitals.Health);
    }

    [Fact]
    public void AnInstantActionPointValueModifierRaisesActionPoints()
    {
        var vitals = Vitals();
        Assert.True(VitalsEffects.Apply(vitals, [Effect(Regen, 25)], new RecordMagicEffects(Reader())));
        Assert.Equal(65, vitals.ActionPoints);
        Assert.Equal(50, vitals.Health);
    }

    [Fact]
    public void ATimedValueModifierRestoresMagnitudeTimesDurationOverTheDurationForHealthAndActionPoints()
    {
        var vitals = Vitals(50);
        var records = new RecordMagicEffects(Reader());
        Assert.True(VitalsEffects.Apply(vitals, [Effect(Heal, 4, 10), Effect(Regen, 7, 5)], records));
        Assert.Equal((50d, 40d, 40d), (vitals.Health, vitals.ActionPoints, vitals.PendingHealth));
        vitals.Tick(5);
        Assert.Equal((70d, 75d), (vitals.Health, vitals.ActionPoints));
        vitals.Tick(60);
        Assert.Equal((90d, 75d, 0d), (vitals.Health, vitals.ActionPoints, vitals.PendingHealth));
    }

    [Fact]
    public void ATimedActionPointRestoreStopsAtTheMaximumAndAHostileTimedEffectQueuesNothing()
    {
        var vitals = new PlayerVitals(100, 50, 90, 80);
        var records = new RecordMagicEffects(Reader());
        Assert.True(VitalsEffects.Apply(vitals, [Effect(Regen, 7, 5)], records));
        vitals.Tick(5);
        Assert.Equal(90, vitals.ActionPoints);
        Assert.False(VitalsEffects.Apply(vitals, [Effect(Poison, 4, 10), Effect(Burst, 4, 10)], records));
        vitals.Tick(10);
        Assert.Equal(50, vitals.Health);
    }

    [Fact]
    public void VanillaShapedFlagsApplyAndTheRecoverBitIsNeitherRequiredNorChecked()
    {
        var vitals = Vitals(10);
        var records = new RecordMagicEffects(Reader());
        Assert.True(VitalsEffects.Apply(vitals, [Effect(VanillaFood, 2, 10), Effect(VanillaRegen, 2, 5), Effect(RecoverHeal, 1, 10)], records));
        vitals.Tick(10);
        Assert.Equal((40d, 50d), (vitals.Health, vitals.ActionPoints));
        Assert.Empty(VitalsEffects.Unapplied([Effect(VanillaFood, 2, 10), Effect(VanillaRegen, 2, 5)], records));
    }

    [Theory]
    [InlineData("negativeDuration")]
    [InlineData("hostileTimed")]
    [InlineData("conditional")]
    [InlineData("detrimentalFlag")]
    [InlineData("hostileFlag")]
    [InlineData("stimpakWithoutDuration")]
    [InlineData("stimpakConditional")]
    [InlineData("stimpakHostile")]
    [InlineData("stimpakZeroMagnitude")]
    [InlineData("peakValueModifierArchetype")]
    [InlineData("otherActorValue")]
    [InlineData("peakWithoutDuration")]
    [InlineData("peakHostile")]
    [InlineData("peakDetrimentalHealth")]
    [InlineData("peakUntrackedValue")]
    [InlineData("negativePeakDuration")]
    [InlineData("effectWithItsOwnConditions")]
    [InlineData("peakWithItsOwnConditions")]
    [InlineData("negativeMagnitude")]
    [InlineData("zeroMagnitude")]
    [InlineData("nanMagnitude")]
    [InlineData("unknownEffect")]
    public void AnEffectOutsideTheEvidencedShapeChangesNothing(string shape)
    {
        var effect = shape switch
        {
            "negativeDuration" => Effect(Heal, 20, -1),
            "hostileTimed" => Effect(Poison, 4, 10),
            "conditional" => Effect(Heal, 20, 0, true),
            "detrimentalFlag" => Effect(Poison),
            "hostileFlag" => Effect(Burst),
            "stimpakWithoutDuration" => Effect(Stim, 6, 0),
            "stimpakConditional" => Effect(Stim, 6, 5, true),
            "stimpakHostile" => Effect(StimHostile, 6, 5),
            "stimpakZeroMagnitude" => Effect(Stim, 0, 5),
            "peakValueModifierArchetype" => Effect(Peak, 50),
            "otherActorValue" => Effect(OtherAv, 60),
            "peakWithoutDuration" => Effect(BuffLuck, 2, 0),
            "peakHostile" => Effect(BuffHostile, 2, 300),
            "peakDetrimentalHealth" => Effect(BuffHealthDetrimental, 10, 300),
            "peakUntrackedValue" => Effect(BuffRads, 10, 300),
            "negativePeakDuration" => Effect(BuffLuck, 2, -5),
            "effectWithItsOwnConditions" => Effect(RadsCompanion, 200, 5),
            "peakWithItsOwnConditions" => Effect(Conditioned, 1, 180),
            "negativeMagnitude" => Effect(Heal, -20),
            "zeroMagnitude" => Effect(Heal, 0),
            "nanMagnitude" => Effect(Heal, double.NaN),
            _ => Effect(Missing),
        };
        var vitals = Vitals();
        Assert.False(VitalsEffects.Apply(vitals, [effect], new RecordMagicEffects(Reader())));
        Assert.Equal((50d, 40d, 0d, 0d, 100d), (vitals.Health, vitals.ActionPoints, vitals.Rads, vitals.PendingRads, vitals.MaxHealth));
        Assert.Empty(vitals.Buffs);
    }

    [Fact]
    public void AListAppliesItsSupportedEffectsAndReportsTheRestAndAnEmptyListIsNotApplied()
    {
        var vitals = Vitals();
        var records = new RecordMagicEffects(Reader());
        var list = new[] { Effect(Heal, 20), Effect(OtherAv, 5), Effect(Stim, 6, 5) };
        Assert.True(VitalsEffects.Apply(vitals, list, records));
        Assert.Equal((70d, 40d), (vitals.Health, vitals.ActionPoints));
        Assert.Equal([$"mg_{OtherAv}: actor value Luck not tracked"], VitalsEffects.Unapplied(list, records));
        Assert.Equal(30, vitals.PendingHealth);
        Assert.False(VitalsEffects.Apply(vitals, [], records));
        Assert.Equal(["no effects"], VitalsEffects.Unapplied([], records));
        Assert.Equal((70d, 40d), (vitals.Health, vitals.ActionPoints));
    }

    [Theory]
    [InlineData(Poison, 0, "hostile or detrimental")]
    [InlineData(Stim, 0, "stimpak effect has no duration")]
    [InlineData(StimHostile, 5, "hostile or detrimental")]
    [InlineData(Peak, 0, "peak modifier has no duration")]
    [InlineData(RadsCompanion, 5, "magic effect has its own conditions")]
    [InlineData(BuffRads, 300, "actor value Rads not tracked")]
    [InlineData(OtherAv, 0, "actor value Luck not tracked")]
    [InlineData(Missing, 0, "magic effect unreadable")]
    [InlineData(Heal, -1, "duration is negative")]
    [InlineData(BuffHostile, 300, "hostile or detrimental")]
    public void EachUnappliedEffectNamesItsReason(string effect, int duration, string reason)
    {
        var list = new[] { Effect(effect, 20, duration) };
        var label = effect == Missing ? effect : "mg_" + effect;
        Assert.Equal([$"{label}: {reason}"], VitalsEffects.Unapplied(list, new RecordMagicEffects(Reader())));
    }

    sealed class Setup
    {
        public readonly PlayerItems Items = new();
        public readonly PlayerVitals Vitals = new(100, 50, 90, 40);
        public readonly InventoryActions Actions;
        public readonly InventoryEquipBinding Binding;
        public readonly FakeRecordReader Source;

        public Setup()
        {
            var reader = Reader();
            Source = reader;
            reader.Add(Tonic, "ALCH", name: "Tonic");
            reader.Add(Mixed, "ALCH", name: "Mixed");
            reader.Add(Slow, "ALCH", "SlowEd", name: "Slow");
            reader.Add(Stimpak, "ALCH", name: "Stimpak");
            reader.Add(RadAway, "ALCH", "RadAwayEd", name: "RadAway");
            reader.Add(Buffout, "ALCH", "BuffoutEd", name: "Buffout");
            var effects = new Dictionary<string, IReadOnlyList<ConsumableEffect>>
            {
                [Tonic] = [Effect(Heal, 30)],
                [Mixed] = [Effect(Heal, 30), Effect(OtherAv, 5)],
                [Slow] = [Effect(Peak, 50)],
                [Stimpak] = [Effect(Stim, 6, 5), Effect(RadsCompanion, 200, 5)],
                [RadAway] = [Effect(Rads, 60, 5), Effect(Heal, 10, 0, true)],
                [Buffout] = [Effect(BuffLuck, 2, 300), Effect(Peak, 50, 300), Effect(BuffAp, 20, 300)],
            };
            var weapons = new WeaponController(Items);
            var host = new WeaponInventoryHost(weapons, Items, _ => null, Vitals, new RecordMagicEffects(reader), id => effects.GetValueOrDefault(id));
            var equipment = new PlayerEquipment();
            Actions = new InventoryActions(Items, new RecordItems(reader), reader, equipment, host);
            Binding = new InventoryEquipBinding(Items, new RecordItems(reader), reader, equipment, weapons, host, id => effects.GetValueOrDefault(id));
            Items.Add(Tonic, 1);
            Items.Add(Mixed, 1);
            Items.Add(Slow, 1);
            Items.Add(Stimpak, 3);
            Items.Add(RadAway, 2);
            Items.Add(Buffout, 1);
            reader.Add(StimDfob, "DFOB", PlayerVitalsBinding.StimpakObject).Scalars["DFOB\\Object"] = Stimpak;
        }
    }

    [Fact]
    public void UsingAnItemThroughTheHostHealsAndConsumesIt()
    {
        var s = new Setup();
        Assert.True(s.Actions.Use(Tonic).Done);
        Assert.Equal(80, s.Vitals.Health);
        Assert.Equal(0, s.Items.CountOf(Tonic));
    }

    [Fact]
    public void UsingAnItemWithOneSupportedEffectAppliesItConsumesOneAndReportsTheRest()
    {
        var s = new Setup();
        s.Items.Add(Mixed, 1);
        var result = s.Actions.Use(Mixed);
        Assert.True(result.Done);
        Assert.Equal($"used; not applied: mg_{OtherAv}: actor value Luck not tracked", result.Message);
        Assert.Equal([$"mg_{OtherAv}: actor value Luck not tracked"], result.Skipped);
        Assert.Equal(80, s.Vitals.Health);
        Assert.Equal(1, s.Items.CountOf(Mixed));
    }

    [Fact]
    public void UsingAnItemWithNoSupportedEffectRefusesWithReasonsAndKeepsTheItem()
    {
        var s = new Setup();
        var result = s.Actions.Use(Slow);
        Assert.False(result.Done);
        Assert.Equal($"effects not applied: mg_{Peak}: peak modifier has no duration", result.Message);
        Assert.Single(result.Skipped);
        Assert.Equal(1, s.Items.CountOf(Slow));
        Assert.Equal((50d, 40d), (s.Vitals.Health, s.Vitals.ActionPoints));
    }

    [Fact]
    public void UseRoutedThroughTheEquipBindingHealsAndRefreshesTheModel()
    {
        var s = new Setup();
        s.Items.Add(Tonic, 1);
        var model = s.Binding.NewModel();
        Assert.True(model.Items.Single(item => item.Id == Tonic).Usable);
        Assert.Equal(2, model.Items.Single(item => item.Id == Tonic).Count);
        Assert.True(s.Binding.Handle(new InventoryEvent(InventoryEventKind.Use, Tonic))!.Done);
        Assert.Equal(80, s.Vitals.Health);
        Assert.Equal(1, model.Items.Single(item => item.Id == Tonic).Count);
        Assert.StartsWith("effects not applied", s.Binding.Handle(new InventoryEvent(InventoryEventKind.Use, Slow))!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostWithoutVitalsRefusesEveryEffect()
    {
        var s = new Setup();
        var bare = new WeaponInventoryHost(new WeaponController(s.Items), s.Items, _ => null);
        Assert.Null(bare.EffectsOf(Tonic));
        Assert.False(bare.ApplyEffects(Tonic, [Effect(Heal)]));
        Assert.Equal(["no vitals store"], bare.Unapplied(Tonic, [Effect(Heal)]));
    }

    static VatsTarget Target(params VatsPart[] parts) => new("A", "A", 10, 100, true, new List<VatsResistance>(), parts);

    static VatsPart Part(string id, int cost, double y) => new(id, id, 70, 100, false, cost, new VatsPoint(960, y));

    [Fact]
    public void AcceptingAVatsQueueSpendsItsTotalCostFromTheStore()
    {
        var fake = new FakeVatsTargets { ActionPoints = 50, InitialTargetId = "A" };
        fake.Targets.Add(Target(Part("head", 20, 300), Part("torso", 15, 500)));
        var model = new VatsMenuModel(fake);
        var vitals = Vitals();
        new PlayerVitalsBinding(vitals, _ => null, _ => throw new InvalidOperationException()).Attach(model);
        model.Begin();
        model.Arm();
        model.Handle(VatsCommand.Queue);
        model.Handle(VatsCommand.PartDown);
        model.Handle(VatsCommand.Queue);
        Assert.Equal(35, model.QueuedCost);
        Assert.Equal(40, vitals.ActionPoints);
        model.Handle(VatsCommand.Accept);
        Assert.Equal(VatsPhase.Playback, model.Phase);
        Assert.Equal(5, vitals.ActionPoints);
    }

    [Fact]
    public void ACancelledVatsSessionSpendsNothing()
    {
        var fake = new FakeVatsTargets { ActionPoints = 50, InitialTargetId = "A" };
        fake.Targets.Add(Target(Part("head", 20, 300)));
        var model = new VatsMenuModel(fake);
        var vitals = Vitals();
        new PlayerVitalsBinding(vitals, _ => null, _ => throw new InvalidOperationException()).Attach(model);
        model.Begin();
        model.Arm();
        model.Handle(VatsCommand.Queue);
        model.Handle(VatsCommand.Return);
        model.Handle(VatsCommand.Return);
        Assert.Equal(VatsPhase.Closed, model.Phase);
        Assert.Equal(40, vitals.ActionPoints);
    }

    [Fact]
    public void AnAidRequestUsesTheMappedItemAndReportsTheResult()
    {
        var s = new Setup();
        var status = new PlayerStatusModel(new SamplePlayerStatus());
        var seen = new List<(AidKind, InventoryActionResult)>();
        var binding = new PlayerVitalsBinding(s.Vitals, kind => kind == AidKind.Stimpak ? Tonic : null, s.Actions.Use);
        binding.AidResolved += (kind, result) => seen.Add((kind, result));
        binding.Attach(status);
        Assert.True(status.RequestAid(AidKind.Stimpak));
        Assert.Equal(80, s.Vitals.Health);
        Assert.True(seen.Single().Item2.Done);
    }

    [Fact]
    public void AnAidRequestForAnUnsupportedItemIsRefusedAndChangesNothing()
    {
        var s = new Setup();
        var status = new PlayerStatusModel(new SamplePlayerStatus());
        var seen = new List<InventoryActionResult>();
        var binding = new PlayerVitalsBinding(s.Vitals, kind => kind == AidKind.Stimpak ? Slow : null, s.Actions.Use);
        binding.AidResolved += (_, result) => seen.Add(result);
        binding.Attach(status);
        status.RequestAid(AidKind.Stimpak);
        Assert.False(seen.Single().Done);
        Assert.Equal(50, s.Vitals.Health);
        Assert.Equal(1, s.Items.CountOf(Slow));
    }

    [Fact]
    public void AnAidRequestWithNoMappedItemIsRefusedWithoutUsingAnything()
    {
        var s = new Setup();
        var status = new PlayerStatusModel(new SamplePlayerStatus());
        var seen = new List<InventoryActionResult>();
        var binding = new PlayerVitalsBinding(s.Vitals, _ => null, _ => throw new InvalidOperationException());
        binding.AidResolved += (_, result) => seen.Add(result);
        binding.Attach(status);
        Assert.True(status.RequestAid(AidKind.Stimpak));
        Assert.Equal(InventoryActionStatus.Refused, seen.Single().Status);
    }

    [Fact]
    public void AQueuedHealDeliversItsTotalAtAConstantRateAndNeverPastTheMaximum()
    {
        var vitals = Vitals(50);
        vitals.QueueHeal(30, 5);
        vitals.Tick(2.5);
        Assert.Equal((65d, 15d), (vitals.Health, vitals.PendingHealth));
        vitals.Tick(10);
        Assert.Equal((80d, 0d), (vitals.Health, vitals.PendingHealth));
        vitals.Tick(1);
        Assert.Equal(80, vitals.Health);
        var nearlyFull = Vitals(90);
        nearlyFull.QueueHeal(30, 5);
        nearlyFull.Tick(5);
        Assert.Equal((100d, 0d), (nearlyFull.Health, nearlyFull.PendingHealth));
    }

    [Fact]
    public void QueuedHealsStackAndNonFiniteOrNonPositiveInputQueuesNothing()
    {
        var vitals = Vitals(10);
        vitals.QueueHeal(10, 2);
        vitals.QueueHeal(20, 4);
        vitals.Tick(1);
        Assert.Equal((20d, 20d), (vitals.Health, vitals.PendingHealth));
        vitals.Tick(1.5);
        Assert.Equal((32.5, 7.5), (vitals.Health, vitals.PendingHealth));
        var idle = Vitals(50);
        idle.QueueHeal(double.NaN, 5);
        idle.QueueHeal(10, 0);
        idle.QueueHeal(-1, 5);
        idle.QueueHeal(10, double.PositiveInfinity);
        idle.Tick(double.NaN);
        idle.Tick(-1);
        idle.Tick(100);
        Assert.Equal((50d, 0d), (idle.Health, idle.PendingHealth));
    }

    [Fact]
    public void AStimpakEffectQueuesPercentOfMaximumHealthPerSecondForItsDurationAndScalesWithTheMaximum()
    {
        var vitals = new PlayerVitals(200, 20, 0, 0);
        Assert.True(VitalsEffects.Apply(vitals, [Effect(Stim, 6, 5)], new RecordMagicEffects(Reader())));
        Assert.Equal((20d, 60d), (vitals.Health, vitals.PendingHealth));
        vitals.Tick(5);
        Assert.Equal(80, vitals.Health);
    }

    [Fact]
    public void UsingAStimpakHealsOverTimeConsumesOneAndSkipsItsConditionedRadiationEffectLeavingRadsAlone()
    {
        var s = new Setup();
        s.Vitals.AddRads(100);
        var result = s.Actions.Use(Stimpak);
        Assert.True(result.Done);
        Assert.Equal([$"mg_{RadsCompanion}: magic effect has its own conditions"], result.Skipped);
        Assert.Equal((100d, 0d), (s.Vitals.Rads, s.Vitals.PendingRads));
        Assert.Equal(2, s.Items.CountOf(Stimpak));
        Assert.Equal((50d, 30d), (s.Vitals.Health, s.Vitals.PendingHealth));
        s.Vitals.Tick(2.5);
        Assert.Equal(65, s.Vitals.Health);
        s.Vitals.Tick(5);
        Assert.Equal((80d, 100d), (s.Vitals.Health, s.Vitals.Rads));
    }

    [Fact]
    public void TheAidItemIsTheOneTheDefaultObjectNamesAndAMissingOrEmptyLinkYieldsNothing()
    {
        var s = new Setup();
        Assert.Equal(Stimpak, PlayerVitalsBinding.AidItem(s.Source, AidKind.Stimpak));
        Assert.Equal(new AidSupply("Stimpak", 3), PlayerVitalsBinding.Supply(s.Source, s.Items, AidKind.Stimpak));
        Assert.Null(PlayerVitalsBinding.AidItem(s.Source, AidKind.RadAway));
        s.Source.Add(RadAwayDfob, "DFOB", PlayerVitalsBinding.RadAwayObject).Scalars["DFOB\\Object"] = "";
        Assert.Null(PlayerVitalsBinding.Supply(s.Source, s.Items, AidKind.RadAway));
    }

    [Fact]
    public void TheStatusAidButtonUsesAStimpakFromTheInventoryAndTheCountDrops()
    {
        var s = new Setup();
        var live = new LivePlayerStatus(null, kind => kind == AidKind.Stimpak ? PlayerVitalsBinding.Supply(s.Source, s.Items, kind) : null);
        var model = new PlayerStatusModel(live);
        var binding = new PlayerVitalsBinding(s.Vitals, kind => PlayerVitalsBinding.AidItem(s.Source, kind),
            id => s.Binding.Handle(new InventoryEvent(InventoryEventKind.Use, id))!);
        binding.Attach(model);
        var aid = Assert.Single(model.Aid);
        Assert.Equal(("USE STIMPAK (3)", true), (aid.Label, aid.Enabled));
        Assert.True(model.RequestAid(AidKind.Stimpak));
        Assert.Equal(30, s.Vitals.PendingHealth);
        Assert.Equal("USE STIMPAK (2)", Assert.Single(model.Aid).Label);
    }

    [Fact]
    public void AStatusSourceWithoutAnAidSupplyStaysReadOnlyAndOffersNoAid()
    {
        var live = new LivePlayerStatus();
        Assert.True(live.ReadOnly);
        Assert.Empty(new PlayerStatusModel(live).Aid);
        var empty = new LivePlayerStatus(null, kind => kind == AidKind.Stimpak ? new AidSupply("Stimpak", 0) : null);
        Assert.False(Assert.Single(new PlayerStatusModel(empty).Aid).Enabled);
    }

    [Fact]
    public void ThePipBoyTorsoRowReadsTheLivePlayerVitalsAndShowsAQueuedHealAsPending()
    {
        var vitals = new PlayerVitals(100, 50, 90, 40);
        var model = new PlayerStatusModel(new LivePlayerStatus(null, null, vitals));

        var torso = Assert.Single(model.Condition);
        Assert.Equal(("50/100", 0.5), (torso.Text, torso.Fraction));
        Assert.Equal(0.5, torso.PendingFraction);

        vitals.QueueHeal(30, 5);
        Assert.Equal(0.8, Assert.Single(model.Condition).PendingFraction, 3);
        Assert.Empty(new PlayerStatusModel(new LivePlayerStatus()).Condition);
    }

    [Fact]
    public void RadAwayLowersRadsOverItsDurationNeverBelowZeroAndTheCountDrops()
    {
        var s = new Setup();
        s.Vitals.AddRads(500);
        var result = s.Actions.Use(RadAway);
        Assert.True(result.Done);
        Assert.Equal([$"mg_{Heal}: conditional effect"], result.Skipped);
        Assert.Equal(1, s.Items.CountOf(RadAway));
        Assert.Equal((500d, -300d), (s.Vitals.Rads, s.Vitals.PendingRads));
        s.Vitals.Tick(2.5);
        Assert.Equal(350, s.Vitals.Rads);
        s.Vitals.Tick(10);
        Assert.Equal((200d, 0d), (s.Vitals.Rads, s.Vitals.PendingRads));
        var low = Vitals();
        low.AddRads(100);
        Assert.True(VitalsEffects.Apply(low, [Effect(Rads, 60, 5)], new RecordMagicEffects(Reader())));
        low.Tick(5);
        Assert.Equal(0, low.Rads);
    }

    [Fact]
    public void ADetrimentalRadiationEffectRaisesRadsInstantlyOrOverItsDuration()
    {
        var vitals = Vitals();
        var records = new RecordMagicEffects(Reader());
        Assert.True(VitalsEffects.Apply(vitals, [Effect(RadsDetrimental, 5)], records));
        Assert.Equal(5, vitals.Rads);
        Assert.True(VitalsEffects.Apply(vitals, [Effect(RadsDetrimental, 2, 5)], records));
        Assert.Equal((5d, 10d), (vitals.Rads, vitals.PendingRads));
        vitals.Tick(5);
        Assert.Equal(15, vitals.Rads);
    }

    [Fact]
    public void RadsIgnoreNonFiniteInputAndZeroQueuesAndNeverGoNegative()
    {
        var vitals = Vitals();
        vitals.AddRads(double.NaN);
        vitals.QueueRads(0, 5);
        vitals.QueueRads(double.NaN, 5);
        vitals.QueueRads(5, 0);
        vitals.Tick(10);
        vitals.AddRads(-50);
        Assert.Equal((0d, 0d), (vitals.Rads, vitals.PendingRads));
    }

    [Fact]
    public void APeakBuffHoldsItsFlatAmountForTheDurationThenExpiresAndBuffsOfOneValueSum()
    {
        var vitals = Vitals();
        var records = new RecordMagicEffects(Reader());
        Assert.True(VitalsEffects.Apply(vitals, [Effect(BuffLuck, 2, 300), Effect(BuffLuck, 2, 300), Effect(BuffLuckDetrimental, 1, 180)], records));
        Assert.Equal(3, vitals.Buff("luck"));
        vitals.Tick(179);
        Assert.Equal(3, vitals.Buff("Luck"));
        vitals.Tick(2);
        Assert.Equal(4, vitals.Buff("Luck"));
        vitals.Tick(200);
        Assert.Equal(0, vitals.Buff("Luck"));
        Assert.Empty(vitals.Buffs);
    }

    [Fact]
    public void AHealthAndActionPointBuffRaiseTheMaximaUntilTheyExpireThenClampTheCurrentValues()
    {
        var vitals = Vitals(50);
        Assert.True(VitalsEffects.Apply(vitals, [Effect(Peak, 50, 300), Effect(BuffAp, 20, 300)], new RecordMagicEffects(Reader())));
        Assert.Equal((150d, 110d, 50d, 40d), (vitals.MaxHealth, vitals.MaxActionPoints, vitals.Health, vitals.ActionPoints));
        vitals.AddHealth(200);
        vitals.AddActionPoints(200);
        Assert.Equal((150d, 110d), (vitals.Health, vitals.ActionPoints));
        vitals.Tick(300);
        Assert.Equal((100d, 90d, 100d, 90d), (vitals.MaxHealth, vitals.MaxActionPoints, vitals.Health, vitals.ActionPoints));
    }

    [Fact]
    public void AStimpakScalesWithABuffedMaximumHealth()
    {
        var vitals = Vitals(20);
        Assert.True(VitalsEffects.Apply(vitals, [Effect(Peak, 100, 300), Effect(Stim, 6, 5)], new RecordMagicEffects(Reader())));
        Assert.Equal(60, vitals.PendingHealth);
    }

    [Fact]
    public void ABuffWithNoAmountOrDurationIsNotAdded()
    {
        var vitals = Vitals();
        vitals.AddBuff("Luck", 0, 10);
        vitals.AddBuff("Luck", 2, 0);
        vitals.AddBuff("Luck", double.NaN, 10);
        vitals.AddBuff("Luck", 2, double.PositiveInfinity);
        Assert.Empty(vitals.Buffs);
    }

    [Fact]
    public void UsingABuffChemThroughTheHostAddsItsBuffsAndConsumesOne()
    {
        var s = new Setup();
        Assert.True(s.Actions.Use(Buffout).Done);
        Assert.Equal((2d, 150d, 110d, 0), (s.Vitals.Buff("Luck"), s.Vitals.MaxHealth, s.Vitals.MaxActionPoints, s.Items.CountOf(Buffout)));
        s.Vitals.Tick(300);
        Assert.Equal((0d, 100d, 90d), (s.Vitals.Buff("Luck"), s.Vitals.MaxHealth, s.Vitals.MaxActionPoints));
    }

    [Fact]
    public void TheConsoleUsesAnItemAndPrintsTheVitalsResultAndTheRefusals()
    {
        var s = new Setup();
        var console = new VitalsConsoleHost(s.Vitals, s.Items, s.Actions.Use, s.Source);
        Assert.Equal("Health 50/100 (pending 0); AP 40/90; Rads 0 (pending 0); buffs: none", console.Execute("vitals", null).Lines[0].Text);
        console.Execute("rads 500", null);
        var used = console.Execute("use RadAwayEd", null);
        Assert.StartsWith("RadAway: used; not applied", used.Lines[0].Text, StringComparison.Ordinal);
        Assert.Contains("carrying 1", used.Lines[0].Text, StringComparison.Ordinal);
        Assert.Contains("Rads 500 (pending -300)", used.Lines[^1].Text, StringComparison.Ordinal);
        Assert.Contains("Rads 200", console.Execute("tick 10", null).Lines[0].Text, StringComparison.Ordinal);
        Assert.Contains("Health 20/100", console.Execute("hurt 30", null).Lines[0].Text, StringComparison.Ordinal);
        Assert.Equal(1, s.Items.CountOf(RadAway));
        Assert.StartsWith("Gave 2 Buffout", console.Execute("give BuffoutEd 2", null).Lines[0].Text, StringComparison.Ordinal);
        Assert.Equal(3, s.Items.CountOf(Buffout));
        Assert.Equal(ConsoleSeverity.Error, console.Execute("give NoSuchItem", null).Lines[0].Severity);
        Assert.Equal(ConsoleSeverity.Error, console.Execute("hurt x", null).Lines[0].Severity);
        Assert.Equal(ConsoleSeverity.Error, console.Execute("nonsense", null).Lines[0].Severity);
        var refused = console.Execute("use SlowEd", null);
        Assert.Equal(ConsoleSeverity.Warning, refused.Lines[0].Severity);
        Assert.Contains("peak modifier has no duration", refused.Lines[1].Text, StringComparison.Ordinal);
        Assert.Equal(["use"], console.Complete("u"));
    }

    [Fact]
    public void TheComposedConsoleChainRoutesVitalsAndItsCommandsToTheVitalsHostAndTheRestToTheInnerHosts()
    {
        var s = new Setup();
        var chain = new CompanionConsoleHost(new VitalsConsoleHost(s.Vitals, s.Items, s.Actions.Use, s.Source, new InMemoryConsoleHost()), new OpenCommonwealth.Services.Interaction.CompanionRoster());
        Assert.Equal("Health 50/100 (pending 0); AP 40/90; Rads 0 (pending 0); buffs: none", chain.Execute("vitals", null).Lines[0].Text);
        Assert.Contains("Health 20/100", chain.Execute("hurt 30", null).Lines[0].Text, StringComparison.Ordinal);
        Assert.Equal(20, s.Vitals.Health);
        Assert.Equal("hi", chain.Execute("echo hi", null).Lines[0].Text);
        Assert.Equal("No companion.", chain.Execute("companion status", null).Lines[0].Text);
        Assert.Equal(ConsoleSeverity.Error, chain.Execute("nonsense", null).Lines[0].Severity);
        Assert.Equal(["use", "vitals"], chain.Complete("u").Concat(chain.Complete("v")).ToArray());
        Assert.Contains("echo", chain.Complete(string.Empty));
    }
}
