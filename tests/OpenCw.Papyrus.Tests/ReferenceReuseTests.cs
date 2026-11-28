using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ReferenceReuseTests
{
    private static readonly PapyrusOwnerId Owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");
    private static readonly PapyrusOwnerId Activator = PapyrusOwnerId.ForPlacedReference("Actor:Probe.esp", "Player:Probe.esp");
    private const string Metadata = "[{\"name\":\"First\",\"properties\":[{\"name\":\"Count\",\"type\":\"Int\",\"value\":\"7\"}]}]";

    [Theory]
    [InlineData(Metadata, true, "Radicle reported a partial reference VMAD decode")]
    [InlineData("[]", false, "the owner is already bound to different compiled script metadata")]
    [InlineData("[{\"name\":\"First\",\"properties\":[]}]", false, "the owner is already bound to different compiled script metadata")]
    [InlineData("[{\"name\":\"First\",\"properties\":[{\"name\":\"Count\",\"type\":\"Int\",\"value\":\"8\"}]}]", false, "the owner is already bound to different compiled script metadata")]
    [InlineData("[{\"name\":\"Second\",\"properties\":[]}]", false, "the owner is already bound to different compiled script metadata")]
    [InlineData("{}", false, "compiled script metadata has an invalid attachment or property shape")]
    [InlineData("[42]", false, "compiled script metadata has an invalid attachment or property shape")]
    [InlineData("[{\"name\":\"First\",\"properties\":[{\"name\":\"Count\",\"type\":\"Array\"}]}]", false, "the owner is already bound to different compiled script metadata")]
    public void CachedAttachmentCannotAcceptConflictingOrRefusedInput(string metadata, bool partial, string refusal)
    {
        foreach (var parked in new[] { false, true })
        {
            using var session = GameSessionTests.Session();
            var original = session.AttachReference("Cell", Owner, Metadata);
            Assert.Null(original.Refusal);
            var instance = Assert.Single(original.Instances);
            if (parked) session.DetachCell("Cell");
            var refused = session.AttachReference("Cell", Owner, metadata, partial);
            Assert.Equal(refusal, refused.Refusal);
            Assert.Empty(refused.Instances);
            Assert.Equal(1, session.RefusedReferences);
            Assert.Same(instance, Assert.Single(session.Host.Instances));
            Assert.Equal(PapyrusValue.FromInt(7), instance.Properties["Count"]);
            Assert.Equal(parked ? 1 : 0, session.Cells.ParkedInstances);
            Assert.Equal(0, session.FailedInitializations);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefusedReuseKeepsOriginalCountersWhileTheDetachedWaitResumes(bool completeFirst)
    {
        using var session = GameSessionTests.Session(waiting: true);
        var original = session.AttachReference("Cell", Owner, Metadata);
        var observed = new List<PapyrusValue>();
        session.Host.RegisterNative(new NativeId("External", "Answer"), _ => throw new NativeWait(42));
        session.Host.RegisterNative(new NativeId("External", "Observe"), args =>
        {
            observed.Add(args[0]);
            return PapyrusValue.None;
        });
        Assert.Equal(1, session.Activate(Owner, Activator).Pending);
        session.DetachCell("Cell");
        if (completeFirst) Assert.True(session.Host.CompleteLatent(42, PapyrusValue.FromInt(9)));
        Assert.Equal("the owner is already bound to different compiled script metadata", session.AttachReference("Cell", Owner, "[]").Refusal);
        Assert.Equal(1, session.Host.WaitingCount);
        if (!completeFirst) Assert.True(session.Host.CompleteLatent(42, PapyrusValue.FromInt(9)));
        session.Host.RunStep(32);
        Assert.Equal(PapyrusValue.FromInt(9), Assert.Single(observed));
        session.ReattachCell("Cell");
        Assert.Same(original, session.AttachReference("Cell", Owner, Metadata));
        session.Host.RunStep(32);
        Assert.Equal(PapyrusValue.FromInt(9), Assert.Single(observed));
        Assert.Equal(1, session.Events.Pending);
        Assert.Equal(0, session.Events.Delivered);
        Assert.Equal(1, session.RefusedReferences);
    }

    [Fact]
    public void EquivalentMetadataStillReusesTheExactBinding()
    {
        using var session = GameSessionTests.Session();
        var original = session.AttachReference("Cell", Owner, Metadata);
        var equivalent = Metadata.Replace("\"7\"", "\"007\"").Replace("{", "{ ");
        Assert.Same(original, session.AttachReference("Cell", Owner, equivalent));
        Assert.Single(session.Host.Instances);
        Assert.Equal(0, session.RefusedReferences);
    }

    [Fact]
    public void AnotherCellCannotClaimTheOriginalOwner()
    {
        using var session = GameSessionTests.Session();
        var original = session.AttachReference("FirstCell", Owner, Metadata);
        Assert.Equal("the owner is already bound to a different cell", session.AttachReference("SecondCell", Owner, Metadata).Refusal);
        Assert.Same(original, session.AttachReference("FirstCell", Owner, Metadata));
        Assert.Single(session.Host.Instances);
        Assert.Equal(1, session.RefusedReferences);
    }
}
