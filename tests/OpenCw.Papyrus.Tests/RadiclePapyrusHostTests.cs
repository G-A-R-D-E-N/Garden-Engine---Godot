#if RADICLE_HOST_AVAILABLE
using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Host;
using Radicle.Interop.Native;
using Xunit;

namespace OpenCw.Papyrus.Tests;

// Invariant: the Host skeleton proves the binding and the library agree on the pinned ABI, a runtime can be created with the default-allow gate, a PEX set loads, and a function receives its native callback.
public class RadiclePapyrusHostTests
{
    [Fact]
    public void TheBindingAndTheLoadedLibraryAgreeOnThePinnedAbi()
    {
        Assert.Equal((uint)RadiclePin.AbiVersion, RadicleNative.CompiledAbiVersion);
        Assert.Equal((uint)RadiclePin.AbiVersion, RadicleNative.AbiVersion());
        Assert.Equal(434u, (uint)RadiclePin.AbiVersion);
    }

    [Fact]
    public void TheSkeletonCreatesARuntimeRunsAFunctionAndReceivesItsNativeCallback()
    {
        var calls = new List<(string Script, string Function, int ArgumentCount)>();
        using var host = RadiclePapyrusHost.Create();
        host.NativeCall = (script, function, arguments) =>
        {
            calls.Add((script, function, arguments.Count));
            return RadicleValue.Int(42);
        };

        host.AddPex(TestPex.CallNativeScript());

        var result = host.Run("Main", "Run");

        Assert.True(result.Ok, $"script status was {result.Status}");
        Assert.NotNull(result.Value);
        using var value = result.Value!;
        Assert.Equal(PapyrusValueKind.Int, value.Kind);
        Assert.Equal(42, value.AsInt());
        Assert.Equal(1, host.PexCount);
        var call = Assert.Single(calls);
        Assert.Equal("Tool", call.Script);
        Assert.Equal("Bonus", call.Function);
        Assert.Equal(0, call.ArgumentCount);
    }
}

// Invariant: the fixture is the smallest FO4 .pex the runtime can run, written in the format Radicle emits, so the test exercises the real parser and not a mock.
internal static class TestPex
{
    private static readonly string[] Table = { "", "Main", "Int", "Run", "Tool", "Bonus", "bonus" };

    public static byte[] CallNativeScript()
    {
        var body = new Writer();
        body.U16(Index(""));            // parent class name
        body.U16(Index(""));            // doc string
        body.U8(0);                     // is const
        body.U32(0);                    // user flags
        body.U16(Index(""));            // auto state name
        body.U16(0);                    // structs
        body.U16(0);                    // variables
        body.U16(0);                    // properties
        body.U16(1);                    // states
        body.U16(Index(""));            // state name
        body.U16(1);                    // functions in the state
        body.U16(Index("Run"));         // function name
        body.U16(Index("Int"));         // return type
        body.U16(Index(""));            // doc string
        body.U32(0);                    // user flags
        body.U8(0);                     // not global, not native
        body.U16(0);                    // parameters
        body.U16(1);                    // locals
        body.U16(Index("bonus"));
        body.U16(Index("Int"));
        body.U16(2);                    // instructions
        body.U8(25);                    // callstatic
        body.Value(2, Index("Tool"));
        body.Value(2, Index("Bonus"));
        body.Value(1, Index("bonus"));
        body.Value(3, 0u);              // zero varargs
        body.U8(26);                    // return
        body.Value(1, Index("bonus"));

        var bodyBytes = body.ToArray();
        var file = new Writer();
        file.U32(0xFA57C0DE);
        file.U8(3);                     // major
        file.U8(9);                     // minor
        file.U16(2);                    // game id, Fallout 4
        file.I64(0);                    // compilation time
        file.Latin1("Test.psc");
        file.Latin1("");
        file.Latin1("");
        file.U16((ushort)Table.Length);
        foreach (var entry in Table) file.Latin1(entry);
        file.U8(0);                     // no debug info
        file.U16(0);                    // user flags
        file.U16(1);                    // objects
        file.U16(Index("Main"));
        file.U32((uint)bodyBytes.Length);
        file.Append(bodyBytes);
        return file.ToArray();
    }

    private static ushort Index(string text)
    {
        var index = Array.IndexOf(Table, text);
        Assert.True(index >= 0, $"the test string table has no entry for \"{text}\"");
        return (ushort)index;
    }

    private sealed class Writer
    {
        private readonly List<byte> _bytes = new();

        public void U8(byte value) => _bytes.Add(value);

        public void U16(ushort value)
        {
            _bytes.Add((byte)value);
            _bytes.Add((byte)(value >> 8));
        }

        public void U32(uint value)
        {
            _bytes.Add((byte)value);
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)(value >> 16));
            _bytes.Add((byte)(value >> 24));
        }

        public void I64(long value)
        {
            U32((uint)value);
            U32((uint)((ulong)value >> 32));
        }

        public void Latin1(string text)
        {
            Assert.True(text.Length <= ushort.MaxValue);
            U16((ushort)text.Length);
            foreach (var ch in text) _bytes.Add((byte)ch);
        }

        public void Value(byte tag, ushort index)
        {
            U8(tag);
            if (tag == 1 || tag == 2) U16(index);
        }

        public void Value(byte tag, uint number)
        {
            U8(tag);
            if (tag == 3) U32(number);
        }

        public void Append(IReadOnlyList<byte> other) => _bytes.AddRange(other);

        public byte[] ToArray() => _bytes.ToArray();
    }
}
#endif
