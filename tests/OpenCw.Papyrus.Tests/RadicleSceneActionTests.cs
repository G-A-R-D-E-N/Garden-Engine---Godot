using OpenCommonwealth.Services.Papyrus.Scenes;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class RadicleSceneActionTests
{
    private const uint IgnoreForCompletion = 1u << 19;
    private static readonly FormKey PackageA = new("Test.esm", 0x100);
    private static readonly FormKey PackageB = new("Test.esm", 0x101);

    private static SceneAction Action(SceneActionType type, SceneActionTarget? target, uint flags = IgnoreForCompletion) =>
        new((ushort)type, null, 1, 7, flags, 0, 0, Array.Empty<ScenePayload>()) { Target = target };

    private static (SceneActionDef? Def, string Refusal) Read(SceneAction action)
    {
        var refusals = new List<string>();
        var def = RadicleSceneSource.ReadAction(action, refusals);
        return (def, string.Join("; ", refusals));
    }

    [Fact]
    public void ATimerWithOnlyADurationIsKept()
    {
        var (def, refusal) = Read(Action(SceneActionType.Timer, new TimerActionTarget(false, 4f, null, null)));

        Assert.Empty(refusal);
        Assert.Equal(SceneActionKind.Timer, def!.Kind);
        Assert.Equal(4.0, def.Seconds);
    }

    [Fact]
    public void ATimerThatSetsAQuestStageIsRefused()
    {
        var (def, refusal) = Read(Action(SceneActionType.Timer, new TimerActionTarget(false, 4f, null, 20)));

        Assert.Null(def);
        Assert.Contains("sets a quest stage", refusal);
    }

    [Fact]
    public void AMalformedTimerWithoutAQuestStageIsRefused()
    {
        var (def, refusal) = Read(Action(SceneActionType.Timer, new TimerActionTarget(true, 4f, null, null)));

        Assert.Null(def);
        Assert.Contains("malformed", refusal);
    }

    [Fact]
    public void APackageActionWithOnePackageIsKept()
    {
        var (def, refusal) = Read(Action(SceneActionType.Package, new PackageActionTarget(false, new[] { PackageA })));

        Assert.Empty(refusal);
        Assert.Equal(PackageA.ToString(), def!.Target);
    }

    [Fact]
    public void AMalformedPackageActionIsRefusedEvenWithOnePackage()
    {
        var (def, refusal) = Read(Action(SceneActionType.Package, new PackageActionTarget(true, new[] { PackageA })));

        Assert.Null(def);
        Assert.Contains("malformed", refusal);
    }

    [Fact]
    public void APackageActionWithSeveralPackagesIsRefused()
    {
        var (def, refusal) = Read(Action(SceneActionType.Package, new PackageActionTarget(false, new[] { PackageA, PackageB })));

        Assert.Null(def);
        Assert.Contains("several packages", refusal);
    }

    [Fact]
    public void ANullPackageEntryNextToARealOneStillCountsAsSeveralPackages()
    {
        var payload = new[] { new ScenePayload("PNAM", new byte[4]), new ScenePayload("PNAM", new byte[] { 0, 1, 0, 0 }) };
        var action = Action(SceneActionType.Package, new PackageActionTarget(false, new[] { PackageA })) with { Payload = payload };

        var (def, refusal) = Read(action);

        Assert.Null(def);
        Assert.Contains("several packages", refusal);
    }

    [Fact]
    public void APackageActionThatMustCompleteThePhaseIsRefused()
    {
        var (def, refusal) = Read(Action(SceneActionType.Package, new PackageActionTarget(false, new[] { PackageA }), 0));

        Assert.Null(def);
        Assert.Contains("package-finished", refusal);
    }

    private static PlayerDialogueActionTarget Menu(bool malformed = false) =>
        new(malformed, new SceneReplyLinks(null, null, null, null), new SceneReplyLinks(null, null, null, null), new SceneReplyLinks(null, null, null, null),
            new SceneReplyLinks(null, null, null, null), Array.Empty<int>(), null, null);

    [Fact]
    public void APlayerDialogueActionIsKeptAsAHostOnlyAction()
    {
        var refusals = new List<string>();
        var host = new List<string>();

        var def = RadicleSceneSource.ReadAction(Action(SceneActionType.PlayerDialogue, Menu(), 0), refusals, host);

        Assert.Empty(refusals);
        Assert.Equal(SceneActionKind.PlayerDialogue, def!.Kind);
        Assert.Contains("player dialogue action", Assert.Single(host));
    }

    [Theory]
    [InlineData(1u << 19, "ignored for completion")]
    [InlineData(1u << 7, "dialogue subtype")]
    public void APlayerDialogueActionWithAnUnknownMeaningIsRefused(uint flags, string reason)
    {
        var refusals = new List<string>();

        var def = RadicleSceneSource.ReadAction(Action(SceneActionType.PlayerDialogue, Menu(), flags), refusals, new List<string>());

        Assert.Null(def);
        Assert.Contains(reason, Assert.Single(refusals));
    }

    [Fact]
    public void AMalformedPlayerDialogueActionIsRefused()
    {
        var refusals = new List<string>();

        Assert.Null(RadicleSceneSource.ReadAction(Action(SceneActionType.PlayerDialogue, Menu(true), 0), refusals, new List<string>()));
        Assert.Contains("unreadable", Assert.Single(refusals));
    }

    [Fact]
    public void AnUnflaggedPackageSpanningPhasesIsHostOnlyAndOneInASinglePhaseStaysRefused()
    {
        var spanning = Action(SceneActionType.Package, new PackageActionTarget(false, new[] { PackageA }), 0) with { EndPhase = 9 };
        var single = Action(SceneActionType.Package, new PackageActionTarget(false, new[] { PackageA }), 0);
        var refusals = new List<string>();
        var host = new List<string>();

        Assert.NotNull(RadicleSceneSource.ReadAction(spanning, refusals, host));
        Assert.Null(RadicleSceneSource.ReadAction(single, refusals, host));

        Assert.Contains("spans phases", Assert.Single(host));
        Assert.Contains("must complete the phase", Assert.Single(refusals));
    }
}
