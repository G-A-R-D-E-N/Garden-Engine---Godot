using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ReferenceNativeRegistryTests
{
    public static IEnumerable<object[]> SystemKeys()
    {
        yield return new object[] { ReferenceLinkNativeAdapters.SystemKey };
        yield return new object[] { ReferenceOpenNativeAdapters.SystemKey };
        yield return new object[] { ReferenceActivateNativeAdapters.SystemKey };
        yield return new object[] { ReferenceAnimationNativeAdapters.SystemKey };
        yield return new object[] { ReferenceTimerNativeAdapters.SystemKey };
        yield return new object[] { PlaceAtMeNativeAdapters.SystemKey };
    }

    [Theory]
    [MemberData(nameof(SystemKeys))]
    public void TheRecordedRegistryApproximatesTheReferenceSystem(string key)
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var registry = EvidenceRegistry.Load(File.ReadAllText(Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json")));
        var gate = new EvidenceGate(registry);

        var decision = gate.Gate(key);

        Assert.True(registry.Has(key));
        Assert.Equal(EvidenceStatus.Approximated, registry.StatusFor(key));
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void EveryReferenceNativeFamilyNamesItsSystemRowAndTheAdapterPackage()
    {
        var families = new[]
        {
            ReferenceLinkNativeAdapters.GetFamily,
            ReferenceOpenNativeAdapters.SetFamily,
            ReferenceOpenNativeAdapters.GetFamily,
            ReferenceActivateNativeAdapters.ActivateFamily,
            ReferenceAnimationNativeAdapters.PlayFamily,
            ReferenceAnimationNativeAdapters.PlayAndWaitFamily,
            ReferenceAnimationEventNativeAdapters.RegisterFamily,
            ReferenceAnimationEventNativeAdapters.UnregisterFamily,
            ReferenceTimerNativeAdapters.StartFamily,
            ReferenceTimerNativeAdapters.CancelFamily,
            PlaceAtMeNativeAdapters.PlaceFamily,
        };

        Assert.Equal(families.Length, families.Select(family => family.Native).Distinct().Count());
        Assert.All(families, family => Assert.StartsWith("system.", family.Key));
    }
}
