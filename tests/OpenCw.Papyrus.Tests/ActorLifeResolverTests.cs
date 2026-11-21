using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ActorLifeResolverTests
{
    private const string PlayerRef = "000014:Fallout4.esm";
    private static readonly PapyrusOwnerId Player = PapyrusOwnerId.ForPlacedReference("000007:Fallout4.esm", PlayerRef);
    private static readonly PapyrusOwnerId Live = PapyrusOwnerId.ForPlacedReference("000800:Npcs.esp", "000A00:Npcs.esp");
    private static readonly PapyrusOwnerId Away = PapyrusOwnerId.ForPlacedReference("000801:Npcs.esp", "000A01:Npcs.esp");

    private static ActorLifeResolver Resolver(bool? player, Dictionary<PapyrusOwnerId, bool> live, Dictionary<string, (string, bool)> stored) => new(
        key => key == PlayerRef, () => player, owner => live.TryGetValue(owner, out var dead) ? dead : null, key => stored.TryGetValue(key, out var record) ? record : null);

    // Invariant: the player, a live actor and a known unloaded actor must each be answered, and an unknown receiver must be unavailable rather than alive.
    [Fact]
    public void ThePlayerALiveActorAndAnUnloadedActorAreAnswered()
    {
        var live = new Dictionary<PapyrusOwnerId, bool> { [Live] = true };
        var stored = new Dictionary<string, (string, bool)> { [Away.Discriminator] = (Away.BaseFormKey, true) };
        var resolver = Resolver(false, live, stored);

        Assert.True(resolver.TryIsDead(Player, out var playerDead));
        Assert.False(playerDead);
        Assert.True(resolver.TryIsDead(Live, out var liveDead));
        Assert.True(liveDead);
        Assert.True(resolver.TryIsDead(Away, out var awayDead));
        Assert.True(awayDead);
        Assert.False(resolver.TryIsDead(PapyrusOwnerId.ForPlacedReference("000802:Npcs.esp", "000A02:Npcs.esp"), out var unknown));
        Assert.False(unknown);
        Assert.True(Resolver(true, live, stored).TryIsDead(Player, out playerDead));
        Assert.True(playerDead);
    }

    // Invariant: a player without attached vitals must be unavailable, and must not fall through to a ledger or live answer.
    [Fact]
    public void APlayerWithoutVitalsIsUnavailable()
    {
        var live = new Dictionary<PapyrusOwnerId, bool> { [Player] = true };
        var stored = new Dictionary<string, (string, bool)> { [PlayerRef] = (Player.BaseFormKey, true) };
        Assert.False(Resolver(null, live, stored).TryIsDead(Player, out _));
    }

    // Invariant: a ledger record with a different base form must not answer for the receiver; an empty ledger base is a legacy record and does.
    [Theory]
    [InlineData("000999:Npcs.esp", false)]
    [InlineData("000801:NPCS.ESP", true)]
    [InlineData("", true)]
    public void ALedgerRecordAnswersOnlyForItsOwnBaseForm(string ledgerBase, bool answered)
    {
        var stored = new Dictionary<string, (string, bool)> { [Away.Discriminator] = (ledgerBase, true) };
        Assert.Equal(answered, Resolver(false, new(), stored).TryIsDead(Away, out var dead));
        Assert.Equal(answered, dead);
    }

    // Invariant: an owner that shares only the discriminator, or only kind and base, must not be the same owner.
    [Fact]
    public void OnlyAFullOwnerMatchIsTheSameOwner()
    {
        Assert.True(ActorLifeResolver.SameOwner(Live, Live));
        Assert.True(ActorLifeResolver.SameOwner(Live, PapyrusOwnerId.ForPlacedReference("000800:NPCS.ESP", "000a00:npcs.esp")));
        Assert.False(ActorLifeResolver.SameOwner(Live, PapyrusOwnerId.ForPlacedReference("000801:Npcs.esp", Live.Discriminator)));
        Assert.False(ActorLifeResolver.SameOwner(Live, PapyrusOwnerId.ForDynamicReference(Live.BaseFormKey, Live.Discriminator)));
        Assert.False(ActorLifeResolver.SameOwner(Live, PapyrusOwnerId.ForPlacedReference(Live.BaseFormKey, "000A01:Npcs.esp")));
    }
}
