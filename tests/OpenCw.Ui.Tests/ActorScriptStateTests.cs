using System.Collections.Generic;
using System.Text;
using OpenCommonwealth;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Papyrus.Natives;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ActorScriptStateTests
{
    private const string Actor = "000A00:Npcs.esp";
    private const string Perk = "000F01:Base.esm";

    private static ActorState Spawn()
    {
        var state = new ActorState(10, null);
        state.SetHealth(20f);
        return state;
    }

    // Invariant: a flag written for an actor with no live state must be honoured, kept, and read back.
    [Fact]
    public void AWriteForAnUnloadedActorIsKeptAndRead()
    {
        var store = new ActorScriptState();
        var access = new ActorStateAccess(_ => null, () => new List<ActorState>(), store);

        Assert.True(access.TrySet(Actor, ActorStateFlag.NoBleedoutRecovery, true));
        Assert.True(access.TrySet(Actor, ActorStateFlag.CanSpeakWhenDown, true));

        Assert.True(access.TryGet(Actor, ActorStateQuery.NoBleedoutRecovery, out var value));
        Assert.True(value);
        Assert.True(store.CanSpeakWhenDown(Actor.ToLowerInvariant().Replace("npcs.esp", "NPCS.ESP")));
        Assert.False(new ActorStateAccess(_ => null, () => new List<ActorState>()).TrySet(Actor, ActorStateFlag.CanSpeakWhenDown, true));
    }

    // Invariant: a respawned or reloaded state must receive the stored flags, clearing as well as setting.
    [Fact]
    public void SeedingCopiesTheStoredFlagsOntoALiveStateAndClearsStaleOnes()
    {
        var store = new ActorScriptState();
        store.SetNoBleedoutRecovery(Actor, true);
        var respawned = Spawn();
        ActorStateAccess.Seed(respawned, Actor, store);
        Assert.True(respawned.NoBleedoutRecovery);
        Assert.False(respawned.CanSpeakWhenDown);

        var stale = Spawn();
        stale.CanSpeakWhenDown = true;
        store.Clear();
        ActorStateAccess.Seed(stale, Actor, store);
        Assert.False(stale.CanSpeakWhenDown);
        Assert.False(stale.NoBleedoutRecovery);
    }

    // Invariant: flags and the perk overlay must survive a save and load, and an empty store must write no section.
    [Fact]
    public void FlagsAndPerkOverlaySurviveASaveRoundTrip()
    {
        var store = new ActorScriptState();
        Assert.Empty(store.Sections());
        store.SetCanSpeakWhenDown(Actor, true);
        store.SetPerk(Actor, Perk, true);
        store.SetPerk(Actor, "000F02:Base.esm", false);

        var loaded = new ActorScriptState();
        loaded.Apply(store.Sections());

        Assert.True(loaded.CanSpeakWhenDown(Actor));
        Assert.False(loaded.NoBleedoutRecovery(Actor));
        Assert.True(loaded.PerkOverride(Actor, Perk));
        Assert.False(loaded.PerkOverride(Actor, "000F02:Base.esm"));
        Assert.Null(loaded.PerkOverride(Actor, "000F03:Base.esm"));
        loaded.Apply(new Dictionary<string, byte[]>());
        Assert.True(loaded.IsEmpty);
    }

    // Invariant: a malformed section must be refused and leave the current entries untouched.
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"actors\":[{\"speak\":true}]}")]
    [InlineData("{\"actors\":[{\"ref\":\"000A00:Npcs.esp\",\"perks\":{\"000F01:Base.esm\":\"yes\"}}]}")]
    [InlineData("{\"actors\":[{\"ref\":\"000A00:Npcs.esp\"},{\"ref\":\"000A00:Npcs.esp\"}]}")]
    public void AMalformedSectionIsRefusedAndKeepsTheCurrentState(string json)
    {
        var store = new ActorScriptState();
        store.SetNoBleedoutRecovery(Actor, true);
        Assert.Throws<System.FormatException>(() => store.Apply(new Dictionary<string, byte[]> { [ActorScriptState.SectionName] = Encoding.UTF8.GetBytes(json) }));
        Assert.True(store.NoBleedoutRecovery(Actor));
    }
}
