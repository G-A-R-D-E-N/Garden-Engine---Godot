using System;
using System.Collections.Generic;
using System.Text;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class DroppedItemSaveTests
{
    private const string Cell = "Cell:0001";
    private const string Rifle = "Rifle:Fallout4.esm";
    private static readonly ItemState Modded = new(["mod_a", "mod_b"], 0.5, 2);

    private static DroppedRef Place(DroppedItemStates store, ItemState state, string cell = Cell, int count = 1, double x = 1.5)
    {
        var drop = new DroppedRef(store.Remember(state), cell, Rifle, "Weapon", count, state, x, 2.25, -3, 0.75);
        store.Place(drop);
        return drop;
    }

    private static IReadOnlyDictionary<string, byte[]> Section(string json) =>
        new Dictionary<string, byte[]> { [DroppedItemStates.SectionName] = Encoding.UTF8.GetBytes(json) };

    [Fact]
    public void ARoundTripKeepsPoseCountCellAndInstanceState()
    {
        var store = new DroppedItemStates();
        var plain = Place(store, ItemState.Plain, count: 3);
        var modded = Place(store, Modded);

        var loaded = new DroppedItemStates();
        loaded.Apply(store.Sections());

        Assert.Equal(2, loaded.Placed.Count);
        Assert.Contains(plain, loaded.Placed);
        var back = Assert.Single(loaded.Placed, drop => drop.Token == modded.Token);
        Assert.Equal(Modded, back.State);
        Assert.Equal((Cell, Rifle, "Weapon", 1, 1.5, 2.25, -3.0, 0.75), (back.Cell, back.Id, back.BaseType, back.Count, back.X, back.Y, back.Z, back.Yaw));
    }

    [Fact]
    public void ALoadedModdedDropReturnsItsStateOnPickupAndLeavesTheRegistry()
    {
        var store = new DroppedItemStates();
        var modded = Place(store, Modded);
        var loaded = new DroppedItemStates();
        loaded.Apply(store.Sections());

        Assert.Equal(Modded, loaded.Take(modded.Token));

        Assert.True(loaded.IsEmpty);
        Assert.Empty(loaded.Sections());
    }

    [Fact]
    public void PickingUpAPlainDropRemovesItFromTheSavedSection()
    {
        var store = new DroppedItemStates();
        var plain = Place(store, ItemState.Plain);

        Assert.Null(store.Take(plain.Token));

        Assert.True(store.IsEmpty);
        Assert.Empty(store.Sections());
    }

    [Fact]
    public void AnEmptyRegistryWritesNoSectionAndASaveWithoutOneClearsTheRegistry()
    {
        var store = new DroppedItemStates();
        Assert.Empty(store.Sections());
        Place(store, ItemState.Plain);

        store.Apply(new Dictionary<string, byte[]>());

        Assert.True(store.IsEmpty);
    }

    [Fact]
    public void TokensMintedAfterALoadNeverReuseASavedToken()
    {
        var store = new DroppedItemStates();
        Place(store, ItemState.Plain);
        var second = Place(store, ItemState.Plain);
        var loaded = new DroppedItemStates();
        loaded.Apply(store.Sections());

        var fresh = loaded.Remember(ItemState.Plain);

        Assert.NotEqual(second.Token, fresh);
        Assert.DoesNotContain(loaded.Placed, drop => drop.Token == fresh);
        Assert.True(string.CompareOrdinal(fresh, second.Token) > 0);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("[{\"token\":\"Built:000001\",\"cell\":\"c\",\"id\":\"i\",\"type\":\"t\",\"count\":1,\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}]")]
    [InlineData("[{\"token\":\"Dropped:ZZ\",\"cell\":\"c\",\"id\":\"i\",\"type\":\"t\",\"count\":1,\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}]")]
    [InlineData("[{\"token\":\"Dropped:00000001\",\"cell\":\"\",\"id\":\"i\",\"type\":\"t\",\"count\":1,\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}]")]
    [InlineData("[{\"token\":\"Dropped:00000001\",\"cell\":\"c\",\"id\":\"i\",\"type\":\"t\",\"count\":0,\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}]")]
    [InlineData("[{\"token\":\"Dropped:00000001\",\"cell\":\"c\",\"id\":\"i\",\"type\":\"t\",\"count\":1,\"x\":\"a\",\"y\":0,\"z\":0,\"yaw\":0}]")]
    [InlineData("[{\"token\":\"Dropped:00000001\",\"cell\":\"c\",\"id\":\"i\",\"type\":\"t\",\"count\":1,\"x\":0,\"y\":0,\"z\":0}]")]
    [InlineData("[{\"token\":\"Dropped:00000001\",\"cell\":\"c\",\"id\":\"i\",\"type\":\"t\",\"count\":1,\"condition\":2,\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}]")]
    [InlineData("[{\"token\":\"Dropped:00000001\",\"cell\":\"c\",\"id\":\"i\",\"type\":\"t\",\"count\":1,\"x\":1e39,\"y\":0,\"z\":0,\"yaw\":0}]")]
    [InlineData("[{\"token\":\"Dropped:00000001\",\"cell\":\"c\",\"id\":\"i\",\"type\":\"t\",\"count\":1,\"mods\":[\"a\",\"A\"],\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}]")]
    public void AMalformedSectionRefusesTheWholeSectionAndLeavesTheRegistryAlone(string json)
    {
        var store = new DroppedItemStates();
        var kept = Place(store, Modded);

        Assert.Throws<FormatException>(() => store.Apply(Section(json)));

        Assert.Equal(kept, Assert.Single(store.Placed));
        Assert.Equal(Modded, store.Take(kept.Token));
    }

    [Fact]
    public void ADuplicateTokenRefusesTheSectionEvenWhenEveryRowIsOtherwiseValid()
    {
        const string row = "{\"token\":\"Dropped:00000001\",\"cell\":\"c\",\"id\":\"i\",\"type\":\"t\",\"count\":1,\"x\":0,\"y\":0,\"z\":0,\"yaw\":0}";

        Assert.Throws<FormatException>(() => new DroppedItemStates().Apply(Section("[" + row + "," + row + "]")));
    }

    [Fact]
    public void ARowWithoutStateFieldsLoadsAsAPlainItem()
    {
        var store = new DroppedItemStates();

        store.Apply(Section("[{\"token\":\"Dropped:0000000A\",\"cell\":\"c\",\"id\":\"i\",\"type\":\"t\",\"count\":2,\"x\":1,\"y\":2,\"z\":3,\"yaw\":4}]"));

        var drop = Assert.Single(store.Placed);
        Assert.True(drop.State.IsPlain);
        Assert.Equal(2, drop.Count);
        Assert.Equal("Dropped:0000000B", store.Remember(ItemState.Plain));
    }

    [Fact]
    public void APoseAtTheFloatLimitLoads()
    {
        var store = new DroppedItemStates();

        store.Apply(Section("[{\"token\":\"Dropped:00000001\",\"cell\":\"c\",\"id\":\"i\",\"type\":\"t\",\"count\":1,\"x\":3.4028234663852886e38,\"y\":0,\"z\":0,\"yaw\":0}]"));

        Assert.Equal((double)float.MaxValue, Assert.Single(store.Placed).X);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e39)]
    public void PlaceRefusesAPoseThatIsNotAFiniteFloat(double x)
    {
        var store = new DroppedItemStates();
        var drop = new DroppedRef("Dropped:00000001", Cell, Rifle, "Weapon", 1, ItemState.Plain, x, 0, 0, 0);

        Assert.Throws<ArgumentException>(() => store.Place(drop));

        Assert.True(store.IsEmpty);
    }

    [Fact]
    public void ReconcileSpawnsSavedDropsOfTheCellMissingFromTheSceneAndFreesLiveOnesTheRegistryLost()
    {
        var store = new DroppedItemStates();
        var here = Place(store, ItemState.Plain);
        var alive = Place(store, ItemState.Plain);
        Place(store, ItemState.Plain, cell: "Cell:0002");

        var plan = store.Reconcile(Cell.ToLowerInvariant(), new[] { alive.Token, "Dropped:0000FFFF" });

        Assert.Equal(new[] { "Dropped:0000FFFF" }, plan.Free);
        Assert.Equal(new[] { here }, plan.Spawn);
    }

    [Fact]
    public void ReconcileWithNothingLiveRespawnsEveryDropOfTheCell()
    {
        var store = new DroppedItemStates();
        var one = Place(store, ItemState.Plain);
        var two = Place(store, Modded);

        var plan = store.Reconcile(Cell, Array.Empty<string>());

        Assert.Empty(plan.Free);
        Assert.Equal(new HashSet<DroppedRef> { one, two }, new HashSet<DroppedRef>(plan.Spawn));
    }

    [Fact]
    public void ClearEmptiesTheRegistryAndItsSectionButNeverReissuesATokenAndDropsTheKeptState()
    {
        var store = new DroppedItemStates();
        var first = Place(store, Modded);

        store.Clear();
        var second = Place(store, Modded);

        Assert.NotEqual(first.Token, second.Token);
        store.Clear();
        Assert.True(store.IsEmpty);
        Assert.Empty(store.Sections());
        Assert.Null(store.Take(first.Token));
        Assert.Null(store.Take(second.Token));
    }
}
