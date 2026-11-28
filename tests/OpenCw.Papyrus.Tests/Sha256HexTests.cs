using System.Security.Cryptography;
using OpenCommonwealth.Services;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class Sha256HexTests
{
    private const string Abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void FullDigestIsLowercaseHex()
    {
        Assert.Equal(Abc, Sha256Hex.OfText("abc"));
        Assert.Equal(Abc, Sha256Hex.Of("abc"u8));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(16)]
    public void TruncationCountsBytes(int bytes)
    {
        Assert.Equal(Abc[..(bytes * 2)], Sha256Hex.OfText("abc", bytes));
        Assert.Equal(Abc[..(bytes * 2)], Sha256Hex.Hex(SHA256.HashData("abc"u8), bytes));
    }

    [Fact]
    public void StreamsHashTheSameAsBytes()
    {
        using var stream = new MemoryStream("abc"u8.ToArray());
        Assert.Equal(Abc[..16], Sha256Hex.OfStream(stream, 8));
    }

    [Fact]
    public void FormatsAnyDigestLengthLikeTheOldLoopsDid()
    {
        var sha1 = SHA1.HashData("abc"u8);
        var loop = string.Concat(sha1.Select(b => b.ToString("x2")));
        Assert.Equal(loop[..16], Sha256Hex.Hex(sha1, 8));
        var md5 = MD5.HashData("abc"u8);
        Assert.Equal(Convert.ToHexString(md5).ToLower(), Sha256Hex.Hex(md5, 16));
    }
}
