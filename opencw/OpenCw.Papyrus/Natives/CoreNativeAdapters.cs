using System;
using System.Collections.Generic;
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

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "bound to Garden's clock, random source and log",
        NativeTargetKind.Static, null,
        new NativeBinding.Native(AbsFamily, Abs), new NativeBinding.Native(RandomFamily, RandomFloat), new NativeBinding.Native(TimeFamily, GameTime),
        new NativeBinding.Native(PlayerFamily, Player), new NativeBinding.Native(WarningFamily, Sink("warning")),
        new NativeBinding.Native(ErrorFamily, Sink("error")), new NativeBinding.Native(StackFamily, Sink("trace")));

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
        playerRef() is { Length: > 0 } key ? PapyrusValue.FromObject(new EngineObjectReference(key)) : throw new InvalidOperationException("the PlayerRef record is not resolved");

    private Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> Sink(string level) => (receiver, arguments) =>
    {
        log($"{level}: {(arguments.Count > 0 ? arguments[0].ToString() : "")}");
        return PapyrusValue.None;
    };
}
