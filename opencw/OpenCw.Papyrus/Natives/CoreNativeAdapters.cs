using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class CoreNativeAdapters(IScriptHost host, EvidenceGate gate, Func<double> randomUnit, Func<double?> gameDays, Func<string?> playerRef, Action<string> log)
{
    public const string SystemKey = "system.core";
    public static readonly GameplayNativeFamily AbsFamily = NativeBinding.Family("core", SystemKey, "Math", "abs");
    public static readonly GameplayNativeFamily RandomFamily = NativeBinding.Family("core", SystemKey, "Utility", "RandomFloat");
    public static readonly GameplayNativeFamily TimeFamily = NativeBinding.Family("core", SystemKey, "Utility", "GetCurrentGameTime");
    public static readonly GameplayNativeFamily PlayerFamily = NativeBinding.Family("core", SystemKey, "Game", "GetPlayer");
    public static readonly GameplayNativeFamily WarningFamily = NativeBinding.Family("core", SystemKey, "Game", "Warning");
    public static readonly GameplayNativeFamily ErrorFamily = NativeBinding.Family("core", SystemKey, "Game", "Error");
    public static readonly GameplayNativeFamily StackFamily = NativeBinding.Family("core", SystemKey, "Debug", "TraceStack");

    private static readonly (string Name, Func<double, double> Body, bool Whole)[] MathUnary =
    {
        ("sqrt", Math.Sqrt, false), ("floor", Math.Floor, true), ("Ceiling", Math.Ceiling, true),
        ("sin", degrees => Math.Sin(degrees * Math.PI / 180), false), ("cos", degrees => Math.Cos(degrees * Math.PI / 180), false),
        ("tan", degrees => Math.Tan(degrees * Math.PI / 180), false),
        ("asin", value => Math.Asin(value) * 180 / Math.PI, false), ("acos", value => Math.Acos(value) * 180 / Math.PI, false),
        ("atan", value => Math.Atan(value) * 180 / Math.PI, false),
        ("DegreesToRadians", degrees => degrees * Math.PI / 180, false), ("RadiansToDegrees", radians => radians * 180 / Math.PI, false),
    };

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "bound to Garden's clock, random source and log",
        NativeTargetKind.Static, null,
        MathUnary.Select(math => new NativeBinding.Native(NativeBinding.Family("core", SystemKey, "Math", math.Name), Unary(math.Name, math.Body, math.Whole)))
            .Concat(new[]
            {
                new NativeBinding.Native(NativeBinding.Family("core", SystemKey, "Math", "pow"), Pow),
                new NativeBinding.Native(AbsFamily, Abs), new NativeBinding.Native(RandomFamily, RandomFloat), new NativeBinding.Native(TimeFamily, GameTime),
                new NativeBinding.Native(PlayerFamily, Player), new NativeBinding.Native(WarningFamily, Sink("warning")),
                new NativeBinding.Native(ErrorFamily, Sink("error")), new NativeBinding.Native(StackFamily, Sink("trace")),
            }).ToArray());

    private static Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> Unary(string name, Func<double, double> body, bool whole) => (receiver, arguments) =>
    {
        if (arguments.Count != 1) throw new InvalidOperationException($"Math.{name} takes one number");
        var result = body(Real(arguments, 0));
        if (!double.IsFinite(result)) throw new InvalidOperationException($"Math.{name} of {Real(arguments, 0)} is not a finite number");
        return whole ? PapyrusValue.FromInt((long)result) : PapyrusValue.FromFloat(result);
    };

    private static PapyrusValue Pow(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count != 2) throw new InvalidOperationException("Math.pow takes a base and an exponent");
        var result = Math.Pow(Real(arguments, 0), Real(arguments, 1));
        return double.IsFinite(result) ? PapyrusValue.FromFloat(result) : throw new InvalidOperationException($"Math.pow of {Real(arguments, 0)} and {Real(arguments, 1)} is not a finite number");
    }

    private static double Real(IReadOnlyList<PapyrusValue> arguments, int index)
    {
        var value = arguments[index];
        var number = value.Kind switch
        {
            PapyrusValueKind.Float => value.Real,
            PapyrusValueKind.Int => value.Integer,
            _ => double.NaN,
        };
        return double.IsFinite(number) ? number : throw new InvalidOperationException("the argument is not a finite number");
    }

    private PapyrusValue Abs(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count != 1) throw new InvalidOperationException("Math.abs takes one number");
        return PapyrusValue.FromFloat(Math.Abs(Real(arguments, 0)));
    }

    private PapyrusValue RandomFloat(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count != 2) throw new InvalidOperationException("Utility.RandomFloat takes a minimum and a maximum");
        double min = Real(arguments, 0), max = Real(arguments, 1);
        return PapyrusValue.FromFloat(min + Math.Clamp(randomUnit(), 0, 1) * (max - min));
    }

    private PapyrusValue GameTime(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments) =>
        gameDays() is { } days ? PapyrusValue.FromFloat(days) : throw new InvalidOperationException("no game clock is running");

    private PapyrusValue Player(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments) =>
        playerRef() is { Length: > 0 } key ? PapyrusValue.FromObject(new EngineObjectReference(key) { ScriptType = "Actor" }) : throw new InvalidOperationException("the PlayerRef record is not resolved");

    private Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> Sink(string level) => (receiver, arguments) =>
    {
        log($"{level}: {(arguments.Count > 0 ? arguments[0].ToString() : "")}");
        return PapyrusValue.None;
    };
}
