using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class ReferenceTimerNativeAdapters(IScriptHost host, EvidenceGate gate, ScriptTimers timers, ScriptTimers? gameTimers = null)
{
    public const string SystemKey = "system.reftimers";
    public static readonly GameplayNativeFamily StartFamily =
        NativeBinding.Family("reftimers", SystemKey, "ScriptObject", "StartTimer");
    public static readonly GameplayNativeFamily CancelFamily =
        NativeBinding.Family("reftimers", SystemKey, "ScriptObject", "CancelTimer");

    public static readonly GameplayNativeFamily StartGameTimeFamily =
        NativeBinding.Family("reftimers", SystemKey, "ScriptObject", "StartTimerGameTime");
    public static readonly GameplayNativeFamily CancelGameTimeFamily =
        NativeBinding.Family("reftimers", SystemKey, "ScriptObject", "CancelTimerGameTime");

    // Requires: the natives are declared on ScriptObject; any attached script, quest and alias scripts included, owns its timers.
    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "bound to the per-script timer table",
        NativeTargetKind.ScriptInstance, null, Natives());

    // Requires: game-time intervals are game hours on the table that the runtime advances from the game clock; without that table the two natives stay unbound.
    private NativeBinding.Native[] Natives()
    {
        var natives = new List<NativeBinding.Native> { new(StartFamily, (r, a) => Start(timers, r, a)), new(CancelFamily, (r, a) => Cancel(timers, r, a)) };
        if (gameTimers is not null)
        {
            natives.Add(new(StartGameTimeFamily, (r, a) => Start(gameTimers, r, a)));
            natives.Add(new(CancelGameTimeFamily, (r, a) => Cancel(gameTimers, r, a)));
        }
        return natives.ToArray();
    }

    private static PapyrusValue Start(ScriptTimers timers, PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var instance = InstanceOf(receiver);
        if (arguments.Count is < 1 or > 2 || !IsNumber(arguments[0]) || (arguments.Count == 2 && arguments[1].Kind != PapyrusValueKind.Int))
            throw new InvalidOperationException("StartTimer takes a float interval and an optional int timer id");
        var interval = arguments[0].Kind == PapyrusValueKind.Int ? arguments[0].Integer : arguments[0].Real;
        var id = arguments.Count == 2 ? (int)arguments[1].Integer : 0;
        if (!timers.Start(instance, interval, id))
            throw new InvalidOperationException("the timer interval is not finite or the timer table is full");
        return PapyrusValue.None;
    }

    private static PapyrusValue Cancel(ScriptTimers timers, PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var instance = InstanceOf(receiver);
        if (arguments.Count > 1 || (arguments.Count == 1 && arguments[0].Kind != PapyrusValueKind.Int))
            throw new InvalidOperationException("CancelTimer takes an optional int timer id");
        timers.Cancel(instance, arguments.Count == 1 ? (int)arguments[0].Integer : 0);
        return PapyrusValue.None;
    }

    private static bool IsNumber(PapyrusValue value) => value.Kind is PapyrusValueKind.Float or PapyrusValueKind.Int;

    private static ScriptInstance InstanceOf(PapyrusValue receiver) =>
        receiver.Reference as ScriptInstance ?? throw new InvalidOperationException("the receiver is not an attached script");
}
