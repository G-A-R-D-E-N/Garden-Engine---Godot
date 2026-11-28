using OpenCommonwealth.Services.Papyrus.Natives;

namespace OpenCommonwealth.Services.Papyrus.Events;

public static class ScriptEventTables
{
    public static void Clear(ScriptTimers timers, ScriptTimers gameTimers, ReferenceAnimations animations, AttackTiming attackTiming, EventRegistrations registrations, CustomEventRegistry customEvents)
    {
        timers.Clear();
        gameTimers.Clear();
        animations.Clear();
        attackTiming.Clear();
        registrations.Clear();
        customEvents.Clear();
        customEvents.ForgetAll();
    }
}
