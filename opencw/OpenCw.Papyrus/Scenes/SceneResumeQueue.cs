using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus.Scenes;

// Invariant: a saved run resumes once its actors resolve or after MaxWaitSeconds (our value, UNVERIFIED).
public sealed class SceneResumeQueue
{
    public const double MaxWaitSeconds = 30;

    private sealed record Entry(SceneRunState State, double Age);

    private readonly List<Entry> _pending = new();
    private readonly List<SceneRunState> _failed = new();

    public int Pending => _pending.Count;

    public IReadOnlyList<SceneRunState> States => _pending.Select(entry => entry.State).Concat(_failed).ToArray();

    public IReadOnlyList<string> Refusals { get; private set; } = new List<string>();

    public void Enqueue(IEnumerable<SceneRunState> states)
    {
        foreach (var state in states)
        {
            if (_pending.Any(entry => entry.State.Scene == state.Scene)) continue;
            _failed.RemoveAll(failed => failed.Scene == state.Scene);
            _pending.Add(new Entry(state, 0));
        }
    }

    public void Clear()
    {
        _pending.Clear();
        _failed.Clear();
        Refusals = new List<string>();
    }

    public int Tick(double seconds, SceneRuntime runtime)
    {
        var resumed = 0;
        var refusals = new List<string>(Refusals);
        foreach (var entry in _pending.ToArray())
        {
            var age = entry.Age + seconds;
            SceneStartResult result;
            try
            {
                if (!runtime.Ready(entry.State) && age < MaxWaitSeconds)
                {
                    _pending[_pending.IndexOf(entry)] = entry with { Age = age };
                    continue;
                }
                _pending.Remove(entry);
                result = runtime.Resume(entry.State);
            }
            catch (Exception error)
            {
                _pending.Remove(entry);
                _failed.Add(entry.State);
                refusals.Add($"scene {entry.State.Scene} resume threw {error.GetType().Name}: {error.Message}");
                continue;
            }
            if (result.Outcome is SceneStartOutcome.Started) resumed++;
            else if (result.Outcome is not SceneStartOutcome.AlreadyPlaying) refusals.Add(result.Detail);
        }
        Refusals = refusals;
        return resumed;
    }
}
