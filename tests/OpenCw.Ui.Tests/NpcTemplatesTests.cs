using System;
using System.Collections.Generic;
using OpenCommonwealth;
using Radicle.Interop;
using ConditionFunctions = OpenCommonwealth.ConditionFunctions;
using NpcTemplateSlot = Radicle.Interop.Native.NpcTemplateSlot;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class NpcTemplatesTests
{
    static FormKey Key(uint id) => new("Fallout4.esm", id);

    static readonly FormKey Npc = Key(0x10);

    sealed class Fake : INpcRecords
    {
        public uint? Flags = 0;
        public readonly Dictionary<(FormKey Npc, NpcTemplateSlot Slot), FormKey> Templates = new();
        public readonly Dictionary<FormKey, (FormKey Race, FormKey[] Keywords)> Sources = new();

        public uint? TemplateFlags(FormKey npc) => Flags;

        public FormKey? TemplateSource(FormKey npc, NpcTemplateSlot slot) => Templates.TryGetValue((npc, slot), out var owner) ? owner : null;

        public readonly Dictionary<FormKey, FormKey> Races = new();

        public FormKey? Race(FormKey npc) => Sources.TryGetValue(npc, out var source) ? source.Race : Races.TryGetValue(npc, out var race) ? race : null;

        public readonly Dictionary<FormKey, IReadOnlyDictionary<FormKey, int>> FactionSets = new();

        public IReadOnlyDictionary<FormKey, int>? Factions(FormKey npc) => FactionSets.GetValueOrDefault(npc);

        public IReadOnlyList<FormKey>? Keywords(FormKey record) => Sources.TryGetValue(record, out var source) ? source.Keywords : null;
    }

    [Fact]
    public void UnreadableTemplateFlagsLeaveEveryFactUnknown()
    {
        var facts = NpcTemplates.FactsOf(Npc, new Fake { Flags = null });
        Assert.Null(facts.Race);
        Assert.Null(facts.Factions);
        Assert.Null(facts.Keywords);
    }

    [Fact]
    public void ATemplateSuppliesTheBorrowedRaceAndKeywords()
    {
        var traitsParent = Key(0x20);
        var keywordsParent = Key(0x21);
        var templateRace = Key(0x30);
        var borrowed = Key(0x40);
        var inherited = Key(0x41);
        var records = new Fake { Flags = NpcTemplates.TraitsFlag | NpcTemplates.KeywordsFlag };
        records.Templates[(Npc, NpcTemplateSlot.Traits)] = traitsParent;
        records.Templates[(Npc, NpcTemplateSlot.Keywords)] = keywordsParent;
        records.Sources[traitsParent] = (templateRace, Array.Empty<FormKey>());
        records.Sources[keywordsParent] = (templateRace, new[] { borrowed });
        records.Sources[templateRace] = (templateRace, new[] { inherited });

        var facts = NpcTemplates.FactsOf(Npc, records);

        Assert.Equal<FormKey?>(templateRace, facts.Race);
        Assert.NotNull(facts.Keywords);
        Assert.Equal(2, facts.Keywords!.Count);
        Assert.Contains(borrowed, facts.Keywords);
        Assert.Contains(inherited, facts.Keywords);
    }

    [Fact]
    public void ATemplateSuppliesTheBorrowedFactions()
    {
        var parent = Key(0x22);
        var faction = Key(0x50);
        var records = new Fake { Flags = NpcTemplates.FactionsFlag };
        records.Templates[(Npc, NpcTemplateSlot.Factions)] = parent;
        records.FactionSets[parent] = new Dictionary<FormKey, int> { [faction] = 3 };
        records.FactionSets[Npc] = new Dictionary<FormKey, int> { [Key(0x51)] = 1 };

        var facts = NpcTemplates.FactsOf(Npc, records);

        Assert.NotNull(facts.Factions);
        Assert.Equal(3, Assert.Single(facts.Factions!).Value);
        Assert.True(facts.Factions!.ContainsKey(faction));
    }

    [Fact]
    public void AFactionsTemplateThatCannotBeFollowedLeavesFactionsUnknown()
    {
        var records = new Fake { Flags = NpcTemplates.FactionsFlag };
        records.FactionSets[Npc] = new Dictionary<FormKey, int> { [Key(0x51)] = 1 };

        Assert.Null(NpcTemplates.FactsOf(Npc, records).Factions);
    }

    [Fact]
    public void AnNpcWithoutTheFactionsFlagKeepsItsOwnFactions()
    {
        var records = new Fake { Flags = 0 };
        records.Templates[(Npc, NpcTemplateSlot.Factions)] = Key(0x22);
        records.FactionSets[Npc] = new Dictionary<FormKey, int> { [Key(0x51)] = 1 };
        records.FactionSets[Key(0x22)] = new Dictionary<FormKey, int> { [Key(0x50)] = 3 };

        Assert.True(NpcTemplates.FactsOf(Npc, records).Factions!.ContainsKey(Key(0x51)));
    }

    [Fact]
    public void AnNpcWithoutTheFactionsFlagReadsItsOwnCrimeFactionAndNoneIsKnown()
    {
        var records = new Fake { Flags = 0 };
        records.Templates[(Npc, NpcTemplateSlot.Factions)] = Key(0x22);
        var asked = new List<FormKey>();

        var own = NpcTemplates.CrimeFactionOf(Npc, records, key => { asked.Add(key); return Key(0x60); });
        var none = NpcTemplates.CrimeFactionOf(Npc, records, _ => null);

        Assert.Equal(new NpcCrimeFaction(true, Key(0x60)), own);
        Assert.Equal(new[] { Npc }, asked.ToArray());
        Assert.Equal(new NpcCrimeFaction(true, null), none);
    }

    [Fact]
    public void ATemplatedFactionsSlotSuppliesTheCrimeFactionOfItsOwner()
    {
        var records = new Fake { Flags = NpcTemplates.FactionsFlag };
        records.Templates[(Npc, NpcTemplateSlot.Factions)] = Key(0x22);

        var result = NpcTemplates.CrimeFactionOf(Npc, records, key => key == Key(0x22) ? Key(0x61) : Key(0x62));

        Assert.Equal(new NpcCrimeFaction(true, Key(0x61)), result);
    }

    [Fact]
    public void AnUnfollowableTemplateOrUnreadableFlagsLeaveTheCrimeFactionUnknownAndNeverAskTheRecord()
    {
        var asked = 0;
        FormKey? Own(FormKey key) { asked++; return Key(0x60); }

        Assert.Equal(new NpcCrimeFaction(false, null), NpcTemplates.CrimeFactionOf(Npc, new Fake { Flags = NpcTemplates.FactionsFlag }, Own));
        Assert.Equal(new NpcCrimeFaction(false, null), NpcTemplates.CrimeFactionOf(Npc, new Fake { Flags = null }, Own));
        Assert.Equal(0, asked);
    }

    [Fact]
    public void ARuntimeBaseIsAskedOnlyWhenTheDirectAnswerIsUnknownAndNeverTurnsUnknownIntoNone()
    {
        var asked = 0;
        NpcCrimeFaction Runtime(NpcCrimeFaction answer) { asked++; return answer; }

        var direct = new NpcCrimeFaction(true, Key(0x60));
        Assert.Equal(direct, NpcTemplates.FirstKnown(direct, () => Runtime(new NpcCrimeFaction(true, Key(0x61)))));
        Assert.Equal(0, asked);
        Assert.Equal(new NpcCrimeFaction(true, null), NpcTemplates.FirstKnown(direct with { Known = false }, () => Runtime(new NpcCrimeFaction(true, null))));
        Assert.Equal(1, asked);
        Assert.False(NpcTemplates.FirstKnown(default, () => Runtime(default)).Known);
    }

    [Fact]
    public void AStoredScriptCrimeFactionIsKnownNoneWhenNullAndUnknownWhenItDoesNotParse()
    {
        FormKey? Parse(string text) => text == "000060:Fallout4.esm" ? Key(0x60) : null;

        Assert.Equal(new NpcCrimeFaction(true, null), NpcTemplates.OverrideOf(null, Parse));
        Assert.Equal(new NpcCrimeFaction(true, Key(0x60)), NpcTemplates.OverrideOf("000060:Fallout4.esm", Parse));
        Assert.False(NpcTemplates.OverrideOf("garbage", Parse).Known);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AKeywordListTheTypedReadRefusedLeavesKeywordsUnknownAndHasKeywordUnevaluable(bool ownRefused, bool raceRefused)
    {
        var race = Key(0x30);
        var keyword = Key(0x40);
        var records = new Fake { Flags = 0 };
        if (!ownRefused) records.Sources[Npc] = (race, new[] { keyword });
        else records.Races[Npc] = race;
        if (!raceRefused) records.Sources[race] = (race, Array.Empty<FormKey>());

        var facts = NpcTemplates.FactsOf(Npc, records);
        Assert.Null(facts.Keywords);

        var rule = new ConditionRule(0, BitConverter.SingleToUInt32Bits(1f), ConditionFunctions.HasKeyword, 0, 0);
        var subject = new ConditionSubject(Npc, facts.Race, facts.Factions, facts.Keywords, 1, false, 12, _ => null);
        var input = PackageConditionValues.Value(rule, keyword, null, subject);
        Assert.Equal(RuleVerdict.Unevaluable, PackageRules.EvaluateConditions(new[] { rule }, new[] { input }).Verdict);
    }

    [Fact]
    public void AnNpcWithoutTheKeywordsFlagAnswersHasKeywordFromItsOwnAndItsRaceKeywords()
    {
        var race = Key(0x30);
        var own = Key(0x40);
        var inherited = Key(0x41);
        var records = new Fake { Flags = 0 };
        records.Templates[(Npc, NpcTemplateSlot.Keywords)] = Key(0x21);
        records.Sources[Npc] = (race, new[] { own });
        records.Sources[race] = (race, new[] { inherited });

        var facts = NpcTemplates.FactsOf(Npc, records);

        Assert.Equal(2, facts.Keywords!.Count);
        Assert.Contains(own, facts.Keywords);
        Assert.Contains(inherited, facts.Keywords);
        var subject = new ConditionSubject(Npc, facts.Race, facts.Factions, facts.Keywords, 1, false, 12, _ => null);
        var rule = new ConditionRule(0, BitConverter.SingleToUInt32Bits(1f), ConditionFunctions.HasKeyword, 0, 0);
        RuleVerdict Has(FormKey keyword) =>
            PackageRules.EvaluateConditions(new[] { rule }, new[] { PackageConditionValues.Value(rule, keyword, null, subject) }).Verdict;
        Assert.Equal(RuleVerdict.True, Has(inherited));
        Assert.Equal(RuleVerdict.False, Has(Key(0x42)));
    }

    [Fact]
    public void FactsAreCompleteOnlyWhenRaceFactionsAndKeywordsAllResolved()
    {
        var fake = new Fake();
        fake.Sources[Npc] = (Key(0x20), [Key(0x30)]);
        fake.Races[Key(0x20)] = Key(0x20);
        Assert.False(NpcTemplates.FactsOf(Npc, fake).IsComplete);
        fake.FactionSets[Npc] = new Dictionary<FormKey, int>();
        fake.Sources[Key(0x20)] = (Key(0x20), [Key(0x31)]);
        Assert.True(NpcTemplates.FactsOf(Npc, fake).IsComplete);
        Assert.False(NpcFacts.Unknown.IsComplete);
    }
}
