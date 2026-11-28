using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class XpNativeAdapters(IScriptHost host, EvidenceGate gate, Action<long, bool> reward, Func<int, long> totalForLevel)
{
    public const string SystemKey = "system.xp";
    public static readonly GameplayNativeFamily RewardFamily = NativeBinding.Family("xp", SystemKey, "Game", "RewardPlayerXP");
    public static readonly GameplayNativeFamily LevelFamily = NativeBinding.Family("xp", SystemKey, "Game", "GetXPForLevel");

    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "bound to the player's experience store",
        NativeTargetKind.Static, null, new NativeBinding.Native(RewardFamily, Reward), new NativeBinding.Native(LevelFamily, TotalFor));

    // Constraint: the direct flag is accepted and the amount is applied as given, because Garden models no XP adjustment (entry points, Intelligence); a non-positive amount is ignored by the store.
    private PapyrusValue Reward(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count is < 1 or > 2 || arguments[0].Kind != PapyrusValueKind.Int || (arguments.Count == 2 && arguments[1].Kind != PapyrusValueKind.Bool))
            throw new InvalidOperationException("RewardPlayerXP takes an integer amount and an optional bool");
        reward(arguments[0].Integer, arguments.Count == 2 && arguments[1].Flag);
        return PapyrusValue.None;
    }

    private PapyrusValue TotalFor(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count != 1 || arguments[0].Kind != PapyrusValueKind.Int)
            throw new InvalidOperationException("GetXPForLevel takes one integer level");
        return PapyrusValue.FromInt(Math.Min(totalForLevel((int)Math.Clamp(arguments[0].Integer, int.MinValue, int.MaxValue)), int.MaxValue));
    }
}
