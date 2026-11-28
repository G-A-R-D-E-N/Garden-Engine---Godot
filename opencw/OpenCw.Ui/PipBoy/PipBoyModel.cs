using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed class PipBoyModel
{
    private static readonly PipBoyTab[] TabOrder = Enum.GetValues<PipBoyTab>();

    private readonly Dictionary<PipBoyTab, IPipBoyTabSource> _sources = new();
    private readonly Dictionary<PipBoyTab, string> _cursors = new();
    private readonly Queue<PipBoyEvent> _queue = new();
    private IReadOnlyList<PipBoySearchGroup> _groups = Array.Empty<PipBoySearchGroup>();
    private IReadOnlyList<PipBoySearchHit> _hits = Array.Empty<PipBoySearchHit>();
    private bool _draining;

    public event Action<PipBoyEvent>? Raised;

    public PipBoyTab Tab { get; private set; } = PipBoyTab.Stats;

    public PipBoyPhase Phase { get; private set; } = PipBoyPhase.Closed;

    public bool InstantTransitions { get; set; } = true;

    public int MaxHitsPerTab { get; set; } = 50;

    public string SearchQuery { get; private set; } = string.Empty;

    public IReadOnlyList<PipBoySearchGroup> SearchGroups => _groups;

    public IReadOnlyList<PipBoySearchHit> SearchHits => _hits;

    public int HitCursor { get; private set; } = -1;

    public IReadOnlyList<PipBoyTab> Tabs => TabOrder;

    public void Register(IPipBoyTabSource source)
    {
        _sources[source.Tab] = source;
        if (_cursors.TryGetValue(source.Tab, out var key)) source.Select(key);
    }

    public string? RememberedKey(PipBoyTab tab) =>
        _sources.TryGetValue(tab, out var source) && source.CurrentKey is { } live
            ? live
            : _cursors.GetValueOrDefault(tab);

    public bool Open(PipBoyTab? tab = null)
    {
        if (Phase == PipBoyPhase.Open)
        {
            if (tab is { } open) SelectTab(open);
            return false;
        }
        if (tab is { } first) Switch(first);
        if (Phase == PipBoyPhase.Opening) return false;
        SetPhase(PipBoyPhase.Opening);
        if (InstantTransitions) CompleteTransition();
        return true;
    }

    public bool Close()
    {
        if (Phase is PipBoyPhase.Closed or PipBoyPhase.Closing) return false;
        SetPhase(PipBoyPhase.Closing);
        if (InstantTransitions) CompleteTransition();
        return true;
    }

    public bool Toggle() => Phase is PipBoyPhase.Open or PipBoyPhase.Opening ? Close() : Open();

    public bool CompleteTransition()
    {
        switch (Phase)
        {
            case PipBoyPhase.Opening:
                SetPhase(PipBoyPhase.Open);
                return true;
            case PipBoyPhase.Closing:
                SetPhase(PipBoyPhase.Closed);
                return true;
            default:
                return false;
        }
    }

    public bool SelectTab(PipBoyTab tab) => Phase == PipBoyPhase.Open && Switch(tab);

    public bool CycleTab(int delta)
    {
        if (Phase != PipBoyPhase.Open) return false;
        var index = Array.IndexOf(TabOrder, Tab);
        var next = ((index + delta) % TabOrder.Length + TabOrder.Length) % TabOrder.Length;
        return Switch(TabOrder[next]);
    }

    public void Search(string? query)
    {
        var tokens = ListSearch.Tokenize(query);
        HitCursor = -1;
        if (tokens.Length == 0)
        {
            SearchQuery = string.Empty;
            _groups = Array.Empty<PipBoySearchGroup>();
            _hits = Array.Empty<PipBoySearchHit>();
            return;
        }
        SearchQuery = string.Join(' ', tokens);
        var groups = new List<PipBoySearchGroup>();
        foreach (var tab in TabOrder)
        {
            if (!_sources.TryGetValue(tab, out var source)) continue;
            var found = source.Search(tokens).Take(MaxHitsPerTab).ToArray();
            if (found.Length > 0) groups.Add(new PipBoySearchGroup(tab, found));
        }
        _groups = groups;
        _hits = groups.SelectMany(group => group.Hits).ToArray();
    }


    public bool JumpToHit(int index) => index >= 0 && index < _hits.Count && Jump(_hits[index], index);

    public bool JumpTo(PipBoySearchHit hit) => Jump(hit, -1);

    public bool StepHit(int delta)
    {
        if (_hits.Count == 0) return false;
        var direction = delta >= 0 ? 1 : -1;
        var next = HitCursor < 0
            ? (delta >= 0 ? 0 : _hits.Count - 1)
            : ((HitCursor + delta) % _hits.Count + _hits.Count) % _hits.Count;
        for (var tried = 0; tried < _hits.Count; tried++)
        {
            if (JumpToHit(next)) return true;
            next = ((next + direction) % _hits.Count + _hits.Count) % _hits.Count;
        }
        return false;
    }

    public PipBoySavedState ExportState()
    {
        foreach (var (tab, source) in _sources)
        {
            if (source.CurrentKey is { } key) _cursors[tab] = key;
        }
        return new PipBoySavedState(Tab, new Dictionary<PipBoyTab, string>(_cursors));
    }

    public void ImportState(PipBoySavedState state)
    {
        _cursors.Clear();
        foreach (var (tab, key) in state.Cursors)
        {
            if (Enum.IsDefined(tab) && !string.IsNullOrEmpty(key)) _cursors[tab] = key;
        }
        Tab = Enum.IsDefined(state.Tab) ? state.Tab : PipBoyTab.Stats;
        foreach (var (tab, source) in _sources)
        {
            if (_cursors.TryGetValue(tab, out var key)) source.Select(key);
        }
    }

    private bool Jump(PipBoySearchHit hit, int hitIndex)
    {
        if (Phase != PipBoyPhase.Open || !_sources.TryGetValue(hit.Tab, out var source)) return false;
        if (!(source is IPipBoyJumpSource jump ? jump.Jump(hit.Key) : source.Select(hit.Key))) return false;
        _cursors[hit.Tab] = hit.Key;
        Switch(hit.Tab);
        if (hitIndex < 0) hitIndex = IndexOfHit(hit);
        if (hitIndex >= 0) HitCursor = hitIndex;
        return true;
    }

    private int IndexOfHit(PipBoySearchHit hit)
    {
        for (var i = 0; i < _hits.Count; i++)
        {
            if (_hits[i].Tab == hit.Tab && _hits[i].Key == hit.Key) return i;
        }
        return -1;
    }

    private bool Switch(PipBoyTab to)
    {
        if (to == Tab || !Enum.IsDefined(to)) return false;
        if (_sources.TryGetValue(Tab, out var leaving) && leaving.CurrentKey is { } key) _cursors[Tab] = key;
        var from = Tab;
        Tab = to;
        if (_sources.TryGetValue(to, out var entering) && _cursors.TryGetValue(to, out var saved)) entering.Select(saved);
        if (Phase == PipBoyPhase.Open) Raise(new PipBoyTabChanged(from, to));
        return true;
    }

    private void SetPhase(PipBoyPhase phase)
    {
        Phase = phase;
        Raise(new PipBoyPhaseChanged(phase));
    }

    private void Raise(PipBoyEvent raised)
    {
        _queue.Enqueue(raised);
        if (_draining) return;
        _draining = true;
        try
        {
            while (_queue.TryDequeue(out var next)) Raised?.Invoke(next);
        }
        finally
        {
            _draining = false;
        }
    }
}
