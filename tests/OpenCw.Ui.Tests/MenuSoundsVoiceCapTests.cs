using OpenCommonwealth.Services;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class MenuSoundsVoiceCapTests
{
    [Fact]
    public void SeventeenthTakeFailsUntilOneSlotIsReleased()
    {
        var voices = new VoiceLimit(16);
        for (int i = 0; i < 16; i++)
            Assert.True(voices.TryTake());
        Assert.False(voices.TryTake());
        voices.Release();
        Assert.True(voices.TryTake());
    }
}
