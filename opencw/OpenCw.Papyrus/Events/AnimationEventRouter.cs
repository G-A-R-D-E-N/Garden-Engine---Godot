using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Papyrus.Events;

public enum AnimationEventKind
{
    Other,
    SoundPlay,
    AttackSwing,
    AttackPreHit,
    AttackHit,
    AttackWindowStart,
    AttackWindowEnd,
}

public readonly record struct ClassifiedAnimationEvent(string RefKey, string Name, AnimationEventKind Kind, string Argument);

// Requires: main thread; Handle must not throw for a well-formed event and must not run script events.
public interface IAnimationEventConsumer
{
    void Handle(ClassifiedAnimationEvent raised);
}

public static class AnimationEventNames
{
    public const string SoundPlayPrefix = "SoundPlay.";

    public static AnimationEventKind Classify(string name, out string argument)
    {
        argument = "";
        if (name.StartsWith(SoundPlayPrefix, StringComparison.OrdinalIgnoreCase))
        {
            argument = name[SoundPlayPrefix.Length..].Trim();
            return argument.Length > 0 ? AnimationEventKind.SoundPlay : AnimationEventKind.Other;
        }
        // Requires: every attack name has a source; see CodeNotes 'Garden Engine/notes/r5-anim-events.md'.
        return name.ToLowerInvariant() switch
        {
            "weaponswing" or "weaponleftswing" => AnimationEventKind.AttackSwing,
            "prehitframe" => AnimationEventKind.AttackPreHit,
            "hitframe" => AnimationEventKind.AttackHit,
            "attackwinstart" => AnimationEventKind.AttackWindowStart,
            "attackwinend" => AnimationEventKind.AttackWindowEnd,
            _ => AnimationEventKind.Other,
        };
    }
}

// Requires: main thread; one raised event reaches every consumer in registration order and a throwing consumer is counted, not propagated.
public sealed class AnimationEventRouter(Action<string>? log = null)
{
    private readonly List<IAnimationEventConsumer> _consumers = [];

    public int Dispatched { get; private set; }

    public int Faulted { get; private set; }

    public int Consumers => _consumers.Count;

    public void Add(IAnimationEventConsumer consumer)
    {
        if (!_consumers.Contains(consumer)) _consumers.Add(consumer);
    }

    public void Remove(IAnimationEventConsumer consumer) => _consumers.Remove(consumer);

    public bool Dispatch(string refKey, string name)
    {
        if (string.IsNullOrWhiteSpace(refKey) || string.IsNullOrWhiteSpace(name)) return false;
        var raised = new ClassifiedAnimationEvent(refKey, name, AnimationEventNames.Classify(name, out var argument), argument);
        Dispatched++;
        foreach (var consumer in _consumers.ToArray())
        {
            try { consumer.Handle(raised); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Faulted++;
                log?.Invoke($"animation event {name} on {refKey}: {consumer.GetType().Name} failed: {error.Message}");
            }
        }
        return true;
    }
}

// Requires: terminal consumer; it must not be fed from ReferenceAnimations.Advance output, which would deliver the same event twice.
public sealed class PapyrusAnimationConsumer(ReferenceAnimations animations, Action<string>? warn = null) : IAnimationEventConsumer
{
    public void Handle(ClassifiedAnimationEvent raised)
    {
        int dropped = animations.Dropped;
        animations.Post(raised.RefKey, "", raised.Name);
        if (dropped == 0 && animations.Dropped > 0) warn?.Invoke($"animation events are being dropped: the Papyrus post queue is full ({ReferenceAnimations.PostCapacity}).");
    }
}

public sealed class AttackTiming : IAnimationEventConsumer
{
    private readonly HashSet<string> _open = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string, AnimationEventKind>? Phase;

    public bool IsWindowOpen(string refKey) => _open.Contains(refKey);

    public void Handle(ClassifiedAnimationEvent raised)
    {
        switch (raised.Kind)
        {
            case AnimationEventKind.AttackWindowStart: _open.Add(raised.RefKey); break;
            case AnimationEventKind.AttackWindowEnd: _open.Remove(raised.RefKey); break;
            case AnimationEventKind.AttackSwing or AnimationEventKind.AttackPreHit or AnimationEventKind.AttackHit: break;
            default: return;
        }
        Phase?.Invoke(raised.RefKey, raised.Kind);
    }

    public void Clear() => _open.Clear();
}
