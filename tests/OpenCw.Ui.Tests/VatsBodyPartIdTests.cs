using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VatsBodyPartIdTests
{
    private static VatsBodyPart Part(byte type, float damageMult = 1f) =>
        new("Head", "Head", "Chest", type, 50, damageMult, 100, VatsPartFlags.None);

    [Fact]
    public void APartIsFoundByThePartTypeIdTheVatsQueueCarries()
    {
        IReadOnlyList<VatsBodyPart> parts = [Part(0, 1.5f), Part(3, 2.5f)];
        var found = VatsBodyParts.PartById(parts, "3");
        Assert.NotNull(found);
        Assert.Equal((byte)3, found!.PartType);
        Assert.Equal(2.5f, found.DamageMult);
    }

    [Fact]
    public void AnUnknownOrEmptyLookupGivesNoPart()
    {
        IReadOnlyList<VatsBodyPart> parts = [Part(0), Part(3)];
        Assert.Null(VatsBodyParts.PartById(parts, "7"));
        Assert.Null(VatsBodyParts.PartById(parts, ""));
        Assert.Null(VatsBodyParts.PartById([], "0"));
    }
}
