using System.Security.Cryptography;
using System.Text;

namespace OpenCommonwealth.Services;

public static class Sha256Hex
{
    public static string Of(ReadOnlySpan<byte> bytes, int hexBytes = 32) => Hex(SHA256.HashData(bytes), hexBytes);

    public static string OfText(string text, int hexBytes = 32) => Of(Encoding.UTF8.GetBytes(text), hexBytes);

    public static string OfStream(Stream stream, int hexBytes = 32) => Hex(SHA256.HashData(stream), hexBytes);

    public static string Hex(ReadOnlySpan<byte> digest, int hexBytes = 32) => Convert.ToHexString(digest[..hexBytes]).ToLowerInvariant();
}
