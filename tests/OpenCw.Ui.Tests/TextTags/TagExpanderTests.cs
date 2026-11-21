using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Terminals;
using OpenCommonwealth.Services.Ui.TextTags;
using Radicle.Interop;
using Xunit;
using ItemType = Radicle.Interop.Native.TerminalItemType;

namespace OpenCw.Ui.Tests;

public class TagExpanderTests
{
    const string Quest = "000A00:A.esp";
    const string Base = "000900:A.esp";
    static readonly string Ref = RecordKey.Format("A.esp", 0x800);

    sealed class Resolver(Func<string, string?, string?, string?> answer) : ITagResolver
    {
        public List<(string Kind, string? Value, string? Sub)> Calls { get; } = new();

        public string? Resolve(string kind, string? value, string? sub)
        {
            Calls.Add((kind, value, sub));
            return answer(kind, value, sub);
        }
    }

    sealed class Names(params QuestAliasName[] aliases) : IQuestAliasNames
    {
        public IReadOnlyList<QuestAliasName>? Aliases(string questKey) => questKey == Quest ? aliases : null;
    }

    sealed class Fills(Dictionary<(string, uint), string> filled) : IAliasFills
    {
        public string? DisplayName(string questKey, uint aliasId) => filled.GetValueOrDefault((questKey, aliasId));
    }

    sealed class Display(Dictionary<string, string> quests) : ITextDisplaySource
    {
        public string? QuestFor(string refKey) => quests.GetValueOrDefault(refKey);
    }

    static ITagResolver Fixed(string answer) => new Resolver((_, _, _) => answer);

    static QuestAliasResolver Aliases(params (string Name, uint Id, string? Filled)[] rows) =>
        new(new Names(rows.Select(r => new QuestAliasName(r.Id, r.Name)).ToArray()),
            new Fills(rows.Where(r => r.Filled is not null).ToDictionary(r => (Quest, r.Id), r => r.Filled!)), Quest);

    [Fact]
    public void Text_without_a_resolver_or_without_tags_is_unchanged()
    {
        Assert.Equal("a <Alias=X> b", TagExpander.Expand("a <Alias=X> b", null));
        Assert.Equal("plain text", TagExpander.Expand("plain text", Fixed("never")));
        Assert.Equal(string.Empty, TagExpander.Expand(null, Fixed("never")));
        Assert.Equal(string.Empty, TagExpander.Expand(string.Empty, Fixed("never")));
    }

    [Fact]
    public void A_resolved_tag_is_replaced_and_the_surrounding_text_is_kept()
    {
        var resolver = new Resolver((kind, value, sub) => $"[{kind}|{value}|{sub}]");
        Assert.Equal("Go see [Alias|Nick|] now", TagExpander.Expand("Go see <Alias=Nick> now", resolver));
    }

    [Fact]
    public void Several_tags_in_one_text_are_each_resolved_in_order()
    {
        var resolver = new Resolver((_, value, _) => value!.ToUpperInvariant());
        Assert.Equal("A and B", TagExpander.Expand("<Alias=a> and <Alias=b>", resolver));
    }

    [Fact]
    public void An_unresolved_tag_stays_verbatim()
    {
        var resolver = new Resolver((_, _, _) => null);
        Assert.Equal("Hi <Alias=Nobody.Foo> there", TagExpander.Expand("Hi <Alias=Nobody.Foo> there", resolver));
    }

    [Fact]
    public void An_empty_resolver_answer_replaces_the_tag_with_nothing()
    {
        Assert.Equal("ab", TagExpander.Expand("a<Alias=X>b", Fixed(string.Empty)));
    }

    [Fact]
    public void A_sub_before_or_after_the_value_is_split_out()
    {
        var resolver = new Resolver((_, _, _) => "x");
        TagExpander.Expand("<Token=Key.Sub> <Token.Sub=Key>", resolver);
        Assert.Equal(("Token", "Key", "Sub"), resolver.Calls[0]);
        Assert.Equal(("Token", "Key", "Sub"), resolver.Calls[1]);
    }

    [Fact]
    public void A_tag_without_a_value_or_sub_passes_nulls()
    {
        var resolver = new Resolver((_, _, _) => null);
        TagExpander.Expand("<Alias=Nick> <Alias.Sub> <Alias>", resolver);
        Assert.Equal(("Alias", "Nick", null), resolver.Calls[0]);
        Assert.Equal(("Alias", null, "Sub"), resolver.Calls[1]);
        Assert.Equal(2, resolver.Calls.Count);
    }

    [Fact]
    public void Cap_is_stripped_from_the_sub_and_upper_cases_the_replacement()
    {
        var resolver = new Resolver((_, _, sub) => sub is null ? "nick" : $"sub:{sub}");
        Assert.Equal("Nick", TagExpander.Expand("<Alias.Cap=Nick>", resolver));
        Assert.Equal("Nick", TagExpander.Expand("<Alias=Nick.Cap>", resolver));
        Assert.Equal("Sub:ShortName", TagExpander.Expand("<Alias=Nick.CapShortName>", resolver));
        Assert.Equal(("Alias", "Nick", null), resolver.Calls[0]);
    }

    [Fact]
    public void Cap_leaves_an_already_upper_case_or_empty_replacement_alone()
    {
        Assert.Equal("Nick", TagExpander.Expand("<Alias.Cap=X>", Fixed("Nick")));
        Assert.Equal("1st", TagExpander.Expand("<Alias.Cap=X>", Fixed("1st")));
        Assert.Equal(string.Empty, TagExpander.Expand("<Alias.Cap=X>", Fixed(string.Empty)));
    }

    [Fact]
    public void An_unmatched_open_bracket_keeps_the_rest_of_the_text()
    {
        Assert.Equal("a <Alias=X b", TagExpander.Expand("a <Alias=X b", Fixed("no")));
        Assert.Equal("1 > 0 and no", TagExpander.Expand("1 > 0 and <Alias=X>", Fixed("no")));
    }

    [Fact]
    public void A_stray_open_bracket_does_not_swallow_a_later_tag()
    {
        Assert.Equal("a < b and no", TagExpander.Expand("a < b and <Alias=X>", Fixed("no")));
    }

    [Fact]
    public void A_tag_with_an_empty_kind_stays_verbatim_without_asking_the_resolver()
    {
        var resolver = new Resolver((_, _, _) => "x");
        Assert.Equal("<=Nick> <> <.Cap>", TagExpander.Expand("<=Nick> <> <.Cap>", resolver));
        Assert.Empty(resolver.Calls);
    }

    [Fact]
    public void The_alias_resolver_answers_the_display_name_of_a_filled_alias()
    {
        var resolver = Aliases(("Nick", 3, "Nick Valentine"), ("Other", 4, null));
        Assert.Equal("Find Nick Valentine.", TagExpander.Expand("Find <Alias=Nick>.", resolver));
        Assert.Equal("Find Nick Valentine.", TagExpander.Expand("Find <Alias=nick>.", resolver));
    }

    [Fact]
    public void The_alias_resolver_leaves_unfilled_unknown_sub_and_other_kinds_verbatim()
    {
        var resolver = Aliases(("Nick", 3, "Nick Valentine"), ("Other", 4, null));
        Assert.Equal("<Alias=Other>", TagExpander.Expand("<Alias=Other>", resolver));
        Assert.Equal("<Alias=Missing>", TagExpander.Expand("<Alias=Missing>", resolver));
        Assert.Equal("<Alias=Nick.ShortName>", TagExpander.Expand("<Alias=Nick.ShortName>", resolver));
        Assert.Equal("<Global=Nick>", TagExpander.Expand("<Global=Nick>", resolver));
        Assert.Equal("<Alias>", TagExpander.Expand("<Alias>", resolver));
    }

    [Fact]
    public void The_alias_resolver_is_per_quest_and_a_quest_with_no_alias_data_resolves_nothing()
    {
        var names = new Names(new QuestAliasName(3, "Nick"));
        var fills = new Fills(new() { [(Quest, 3u)] = "Nick Valentine" });
        Assert.Equal("Nick Valentine", TagExpander.Expand("<Alias=Nick>", new QuestAliasResolver(names, fills, Quest)));
        Assert.Equal("<Alias=Nick>", TagExpander.Expand("<Alias=Nick>", new QuestAliasResolver(names, fills, "000B00:A.esp")));
    }

    [Fact]
    public void Reference_resolvers_exist_only_for_references_bound_to_a_quest()
    {
        var resolvers = new ReferenceTagResolvers(new Display(new() { [Ref] = Quest }), new Names(new QuestAliasName(3, "Nick")),
            new Fills(new() { [(Quest, 3u)] = "Nick Valentine" }));
        Assert.Equal("Nick Valentine", TagExpander.Expand("<Alias=Nick>", resolvers.For(Ref)));
        Assert.Null(resolvers.For("000801:A.esp"));
    }

    sealed class FakeRecords : ITerminalRecords
    {
        public TerminalRecord? Terminal(string baseKey) => baseKey == Base ? Term : null;

        public TerminalPlacement? Placement(string refKey) => new(Base, TerminalLockState.Unlocked);
    }

    sealed class NoWords : IHackingWords
    {
        public IReadOnlyList<string> OfLength(int length) => [];
    }

    static UiText T(string? s) => new(default, s, 0);

    static readonly TerminalRecord Term = new(new FormKey("A.esp", 0x900), T("Name"), T("Header <Alias=Nick>"), T("Welcome <Alias=Nick>"),
        new[] { new TerminalBodyEntry(T("Find <Alias=Nick>\r\nnow"), 0) },
        new[] { new TerminalMenuEntry(T("Call <Alias=Nick>"), T("Resp <Alias=Nick>"), T("Page <Alias=Nick>"), ItemType.DisplayText, null, 4, null, null, 0) },
        Array.Empty<TerminalHolotapeEntry>());

    [Fact]
    public void A_terminal_expands_body_and_item_text_through_its_reference_resolver()
    {
        var resolvers = new ReferenceTagResolvers(new Display(new() { [Ref] = Quest }), new Names(new QuestAliasName(3, "Nick")),
            new Fills(new() { [(Quest, 3u)] = "Nick Valentine" }));
        var source = new RecordTerminalSource(new FakeRecords(), new NoWords(), () => 0, tags: resolvers.For);
        var node = source.Node(Ref, Base)!;
        Assert.Equal("Find Nick Valentine\nnow", node.Body);
        Assert.Equal("Call Nick Valentine", node.Entries[0].Text);
        Assert.Equal("Resp Nick Valentine", node.Entries[0].Response);
        Assert.Equal("Page Nick Valentine", node.Entries[0].PageText);
        Assert.Equal("Header Nick Valentine", node.Header);
        Assert.Equal("Welcome Nick Valentine", node.Welcome);
    }

    [Fact]
    public void Response_page_header_and_welcome_keep_an_unresolved_tag_verbatim_and_without_a_resolver()
    {
        var unknown = new ReferenceTagResolvers(new Display(new() { [Ref] = Quest }), new Names(new QuestAliasName(3, "Other")), new Fills(new()));
        foreach (var node in new[]
        {
            new RecordTerminalSource(new FakeRecords(), new NoWords(), () => 0, tags: unknown.For).Node(Ref, Base)!,
            new RecordTerminalSource(new FakeRecords(), new NoWords(), () => 0).Node(Ref, Base)!,
        })
        {
            Assert.Equal("Resp <Alias=Nick>", node.Entries[0].Response);
            Assert.Equal("Page <Alias=Nick>", node.Entries[0].PageText);
            Assert.Equal("Header <Alias=Nick>", node.Header);
            Assert.Equal("Welcome <Alias=Nick>", node.Welcome);
        }
    }

    static readonly string NpcKey = RecordKey.Format("A.esp", 0xC00);

    static ReferenceTagResolvers Bound(HashSet<string> running, params AliasFill[] fills)
    {
        var binder = new AliasBinder(fills, running.Contains, key => key == NpcKey ? "Nick Valentine" : null);
        return new ReferenceTagResolvers(binder, new Names(new QuestAliasName(7, "Console"), new QuestAliasName(3, "Nick")), binder);
    }

    [Fact]
    public void A_terminal_bound_to_an_alias_of_a_running_quest_expands_another_alias_of_that_quest_and_follows_the_quest_state()
    {
        var running = new HashSet<string> { Quest };
        var resolvers = Bound(running, new AliasFill(Quest, 7, "Console", Ref, true), new AliasFill(Quest, 3, "Nick", NpcKey, false));
        var source = new RecordTerminalSource(new FakeRecords(), new NoWords(), () => 0, tags: resolvers.For);
        Assert.Equal("Find Nick Valentine\nnow", source.Node(Ref, Base)!.Body);
        Assert.Equal("Call Nick Valentine", source.Node(Ref, Base)!.Entries[0].Text);
        Assert.Equal("Find <Alias=Nick>\nnow", source.Node(RecordKey.Format("A.esp", 0x801), Base)!.Body);
        running.Remove(Quest);
        Assert.Equal("Find <Alias=Nick>\nnow", source.Node(Ref, Base)!.Body);
    }

    [Fact]
    public void Activation_text_expands_through_the_same_provider_and_stays_verbatim_for_an_unbound_reference()
    {
        var resolvers = Bound(new HashSet<string> { Quest }, new AliasFill(Quest, 7, "Console", Ref, true), new AliasFill(Quest, 3, "Nick", NpcKey, false));
        Assert.Equal("Talk to Nick Valentine", TagExpander.Expand("Talk to <Alias=Nick>", resolvers.For(Ref)));
        Assert.Equal("Talk to <Alias=Nick>", TagExpander.Expand("Talk to <Alias=Nick>", resolvers.For(RecordKey.Format("A.esp", 0x801))));
        Assert.Equal("Talk to <Alias=Unknown>", TagExpander.Expand("Talk to <Alias=Unknown>", resolvers.For(Ref)));
    }

    [Fact]
    public void A_reference_bound_in_two_running_quests_resolves_through_the_quest_that_knows_the_alias()
    {
        const string Other = "000B00:A.esp";
        var running = new HashSet<string> { Quest, Other };
        var resolvers = Bound(running, new AliasFill(Other, 7, "Console", Ref, true), new AliasFill(Quest, 7, "Console", Ref, true), new AliasFill(Quest, 3, "Nick", NpcKey, false));
        Assert.Equal("Nick Valentine", TagExpander.Expand("<Alias=Nick>", resolvers.For(Ref)));
        running.Remove(Quest);
        Assert.Equal("<Alias=Nick>", TagExpander.Expand("<Alias=Nick>", resolvers.For(Ref)));
    }

    const string OtherQuest = "000B00:A.esp";
    static readonly string PiperKey = RecordKey.Format("A.esp", 0xC01);

    sealed class QuestNames(Dictionary<string, QuestAliasName[]> byQuest) : IQuestAliasNames
    {
        public IReadOnlyList<QuestAliasName>? Aliases(string questKey) => byQuest.GetValueOrDefault(questKey);
    }

    static ReferenceTagResolvers TwoQuests(params AliasFill[] fills)
    {
        var binder = new AliasBinder(fills, _ => true, key => key == NpcKey ? "Nick Valentine" : key == PiperKey ? "Piper Wright" : null);
        var names = new QuestNames(new()
        {
            [Quest] = [new QuestAliasName(7, "Console"), new QuestAliasName(3, "Nick"), new QuestAliasName(5, "Contact")],
            [OtherQuest] = [new QuestAliasName(7, "Console"), new QuestAliasName(4, "Piper"), new QuestAliasName(6, "Contact"), new QuestAliasName(8, "Nick")],
        });
        return new ReferenceTagResolvers(binder, names, binder);
    }

    [Fact]
    public void A_tag_that_only_the_second_running_quest_knows_resolves_instead_of_staying_literal()
    {
        var resolvers = TwoQuests(
            new AliasFill(Quest, 7, "Console", Ref, true), new AliasFill(OtherQuest, 7, "Console", Ref, true),
            new AliasFill(Quest, 3, "Nick", NpcKey, false), new AliasFill(OtherQuest, 4, "Piper", PiperKey, false));
        Assert.Equal("Nick Valentine and Piper Wright", TagExpander.Expand("<Alias=Nick> and <Alias=Piper>", resolvers.For(Ref)));
    }

    [Fact]
    public void A_tag_the_running_quests_fill_differently_stays_literal_and_one_they_fill_alike_resolves()
    {
        var resolvers = TwoQuests(
            new AliasFill(Quest, 7, "Console", Ref, true), new AliasFill(OtherQuest, 7, "Console", Ref, true),
            new AliasFill(Quest, 5, "Contact", NpcKey, false), new AliasFill(OtherQuest, 6, "Contact", PiperKey, false),
            new AliasFill(Quest, 3, "Nick", NpcKey, false), new AliasFill(OtherQuest, 8, "Nick", NpcKey, false));
        Assert.Equal("<Alias=Contact>", TagExpander.Expand("<Alias=Contact>", resolvers.For(Ref)));
        Assert.Equal("Nick Valentine", TagExpander.Expand("<Alias=Nick>", resolvers.For(Ref)));
    }

    [Fact]
    public void A_terminal_without_tag_wiring_or_without_a_bound_reference_shows_text_unexpanded()
    {
        var plain = new RecordTerminalSource(new FakeRecords(), new NoWords(), () => 0);
        Assert.Equal("Find <Alias=Nick>\nnow", plain.Node(Ref, Base)!.Body);
        var unbound = new RecordTerminalSource(new FakeRecords(), new NoWords(), () => 0, tags: _ => null);
        Assert.Equal("Call <Alias=Nick>", unbound.Node(Ref, Base)!.Entries[0].Text);
    }
}
