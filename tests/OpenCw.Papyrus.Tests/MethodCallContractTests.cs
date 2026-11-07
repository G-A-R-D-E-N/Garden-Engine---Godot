using System;
using OpenCommonwealth.Services.Papyrus.Census;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class MethodCallContractTests
{
    [Fact]
    public void AKnownReceiverTypeNamesTheTarget()
    {
        var target = MethodCallContract.Resolve("SetValue", "Actor");

        Assert.True(target.Resolved);
        Assert.Equal(MethodTargetState.Resolved, target.State);
        Assert.Equal("SetValue", target.CalledFunction);
        Assert.Equal("Actor", target.ReceiverType);
        Assert.Contains("Actor", target.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownReceiverTypeIsCountedNotGuessed()
    {
        foreach (var unknown in new[] { null, "", "   ", "None", "none" })
        {
            var target = MethodCallContract.Resolve("SetValue", unknown);

            Assert.False(target.Resolved);
            Assert.Equal(MethodTargetState.ReceiverTypeUnknown, target.State);
            Assert.Null(target.ReceiverType);
            Assert.Contains("not attributed", target.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AMissingFunctionNameIsCountedNotGuessed()
    {
        foreach (var missing in new[] { null, "", "  " })
        {
            var target = MethodCallContract.Resolve(missing, "Actor");

            Assert.False(target.Resolved);
            Assert.Equal(MethodTargetState.NoCalledFunction, target.State);
            Assert.Null(target.ReceiverType);
            Assert.Contains("names no function", target.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheSameOperandsAlwaysResolveTheSameWay()
    {
        var first = MethodCallContract.Resolve("AddForm", "LeveledItem");
        var second = MethodCallContract.Resolve("AddForm", "LeveledItem");

        Assert.Equal(first, second);
    }
}
