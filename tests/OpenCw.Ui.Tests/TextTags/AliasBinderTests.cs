using System.Text;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.TextTags;
using Radicle.Interop;
using Xunit;
using AliasKind = Radicle.Interop.Native.QuestAliasKind;
using FillType = Radicle.Interop.Native.QuestFillType;

namespace OpenCw.Ui.Tests;

public class AliasBinderTests
{
    const string QuestA = "000A00:A.esp";
    const string QuestB = "000B00:A.esp";
    static readonly FormKey Placed = new("A.esp", 0x800);
    static readonly FormKey Actor = new("A.esp", 0x900);
    static readonly string PlacedKey = RecordKey.Format(Placed);
    static readonly string ActorKey = RecordKey.Format(Actor);

    static QuestStoredAlias Alias(AliasKind kind, uint? id, string? name, FillType type, FormKey? target, bool supported = true) =>
        new(kind, id, name is null ? null : Encoding.UTF8.GetBytes(name), 0, null, null,
            new QuestStoredFill(supported, type, target, null, null, 0, null, 0, 0, 0), Array.Empty<QuestStoredCollectionEntry>());

    static AliasBinder Binder(HashSet<string> running, params AliasFill[] fills) =>
        new(fills, running.Contains, key => key == ActorKey ? "Nick Valentine" : null);

    [Fact]
    public void The_fill_table_lists_only_named_reference_aliases_with_a_specific_reference_or_unique_actor_fill()
    {
        var aliases = new[]
        {
            Alias(AliasKind.Reference, 1, "Console", FillType.SpecificReference, Placed),
            Alias(AliasKind.Reference, 2, "Nick", FillType.UniqueActor, Actor),
            Alias(AliasKind.Collection, 3, "Crowd", FillType.SpecificReference, Placed),
            Alias(AliasKind.Location, 4, "Place", FillType.SpecificReference, Placed),
            Alias(AliasKind.Reference, 5, "Search", FillType.MatchingClosest, Placed),
            Alias(AliasKind.Reference, 6, "Made", FillType.CreateReference, Placed),
            Alias(AliasKind.Reference, 7, "NoTarget", FillType.SpecificReference, null),
            Alias(AliasKind.Reference, 8, "Unsupported", FillType.SpecificReference, Placed, supported: false),
            Alias(AliasKind.Reference, null, "NoId", FillType.SpecificReference, Placed),
            Alias(AliasKind.Reference, 9, null, FillType.SpecificReference, Placed),
            Alias(AliasKind.Reference, 10, "", FillType.SpecificReference, Placed),
        };
        Assert.Equal(
            new[] { new AliasFill(QuestA, 1, "Console", PlacedKey, true), new AliasFill(QuestA, 2, "Nick", ActorKey, false) },
            AliasFillTable.From(QuestA, aliases).ToArray());
        Assert.Empty(AliasFillTable.From(QuestA, Array.Empty<QuestStoredAlias>()));
    }

    [Fact]
    public void A_reference_is_bound_only_while_its_quest_runs_and_the_answer_follows_start_and_stop()
    {
        var running = new HashSet<string>();
        var binder = Binder(running, new AliasFill(QuestA, 1, "Console", PlacedKey, true));
        Assert.Empty(binder.For(PlacedKey));
        Assert.Null(binder.QuestFor(PlacedKey));
        running.Add(QuestA);
        Assert.Equal(new[] { new AliasBinding(QuestA, 1, "Console") }, binder.For(PlacedKey));
        Assert.Equal(QuestA, binder.QuestFor(PlacedKey));
        running.Remove(QuestA);
        Assert.Empty(binder.For(PlacedKey));
        Assert.Null(binder.QuestFor(PlacedKey));
    }

    [Fact]
    public void An_unbound_reference_has_no_bindings()
    {
        var binder = Binder(new HashSet<string> { QuestA }, new AliasFill(QuestA, 1, "Console", PlacedKey, true));
        Assert.Empty(binder.For("000801:A.esp"));
        Assert.Null(binder.QuestFor("000801:A.esp"));
    }

    [Fact]
    public void A_reference_in_two_running_quests_belongs_to_the_first_quest_in_key_order_and_falls_through_when_it_stops()
    {
        var running = new HashSet<string> { QuestA, QuestB };
        var binder = Binder(running,
            new AliasFill(QuestB, 2, "Later", PlacedKey, true),
            new AliasFill(QuestA, 5, "Second", PlacedKey, true),
            new AliasFill(QuestA, 1, "First", PlacedKey, true));
        Assert.Equal(new[] { new AliasBinding(QuestA, 1, "First"), new AliasBinding(QuestA, 5, "Second"), new AliasBinding(QuestB, 2, "Later") }, binder.For(PlacedKey));
        Assert.Equal(QuestA, binder.QuestFor(PlacedKey));
        running.Remove(QuestA);
        Assert.Equal(QuestB, binder.QuestFor(PlacedKey));
    }

    [Fact]
    public void A_unique_actor_fill_gives_a_display_name_but_never_binds_a_reference()
    {
        var binder = Binder(new HashSet<string> { QuestA }, new AliasFill(QuestA, 2, "Nick", ActorKey, false));
        Assert.Empty(binder.For(ActorKey));
        Assert.Null(binder.QuestFor(ActorKey));
        Assert.Equal("Nick Valentine", binder.DisplayName(QuestA, 2));
    }

    [Fact]
    public void A_display_name_needs_a_known_quest_alias_and_a_name_for_its_target()
    {
        var binder = Binder(new HashSet<string>(), new AliasFill(QuestA, 1, "Console", PlacedKey, true), new AliasFill(QuestA, 2, "Nick", ActorKey, false));
        Assert.Null(binder.DisplayName(QuestA, 1));
        Assert.Null(binder.DisplayName(QuestA, 99));
        Assert.Null(binder.DisplayName(QuestB, 2));
        Assert.Equal("Nick Valentine", binder.DisplayName(QuestA, 2));
    }

    [Fact]
    public void An_alias_fill_matches_a_placed_reference_by_reference_and_a_unique_actor_by_base_form_while_the_quest_runs()
    {
        var running = new HashSet<string> { QuestA };
        var binder = Binder(running, new AliasFill(QuestA, 1, "Console", PlacedKey, true), new AliasFill(QuestA, 2, "Nick", ActorKey, false));
        Assert.True(binder.IsFilledBy(QuestA, 1, PlacedKey, ActorKey));
        Assert.False(binder.IsFilledBy(QuestA, 1, "000801:A.esp", ActorKey));
        Assert.True(binder.IsFilledBy(QuestA, 2, "000801:A.esp", ActorKey));
        Assert.False(binder.IsFilledBy(QuestA, 2, ActorKey, PlacedKey));
        Assert.Null(binder.IsFilledBy(QuestA, 99, PlacedKey, ActorKey));
        running.Clear();
        Assert.False(binder.IsFilledBy(QuestA, 2, "000801:A.esp", ActorKey));
        Assert.True(binder.IsFilledBy(QuestA, 2, "000801:A.esp", ActorKey, key => key == QuestA));
    }
}
