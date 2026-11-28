using System;
using OpenCommonwealth.Services.Papyrus;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ScriptCacheLayoutTests
{
    [Theory]
    [InlineData("../../outside")]
    [InlineData("C:/outside")]
    [InlineData("/outside")]
    [InlineData("quest/../outside")]
    [InlineData("quest\\..\\outside")]
    public void UnsafeNamesCannotEscapeTheCache(string name)
    {
        Assert.Throws<ArgumentException>(() => ScriptCacheLayout.PexPathIn("incoming", name));
    }
}
