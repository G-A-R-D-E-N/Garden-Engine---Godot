using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class AttackNoiseTests
{
    // Regression (finding 517): a gunshot must be audible to perception even when no pellet lands on an actor.
    [Theory]
    [InlineData(AttackKind.Shot, true)]
    [InlineData(AttackKind.Swing, false)]
    [InlineData(AttackKind.Throw, false)]
    [InlineData(AttackKind.Mine, false)]
    public void OnlyAGunshotMakesNoiseOfItsOwn(AttackKind kind, bool makes) => Assert.Equal(makes, AttackNoise.Makes(kind));
}
