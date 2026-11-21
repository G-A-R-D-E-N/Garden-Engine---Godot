using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public enum MapView { World, Local }

public enum MapAction { FastTravel, PlaceMarker, SwitchView, Legend }

public enum MapChoiceAction { Travel, Decline, PlaceMarker, MoveMarker, RemoveMarker }

public sealed record MapWaypoint(string ContextId, double X, double Y);

public sealed record MapSource(MapModel Model, string ContextId);

public readonly record struct TravelVerdict(bool Allowed, string? Reason = null)
{
    public static TravelVerdict Yes => new(true);

    public static TravelVerdict No(string? reason = null) => new(false, reason);
}

public interface IMapTravelRules
{
    TravelVerdict CanFastTravel(MapMarker marker);
}

public sealed class OpenMapTravelRules : IMapTravelRules
{
    public TravelVerdict CanFastTravel(MapMarker marker) => TravelVerdict.Yes;
}

public sealed record MapChoiceOption(string Label, MapChoiceAction Action);

public sealed record MapChoice(string Prompt, string? Reason, IReadOnlyList<MapChoiceOption> Options, int CancelIndex,
                               string? MarkerId, double X, double Y, int Generation);

public static class MapMarkerKinds
{
    private static readonly HashSet<string> NeverTravel = new() { "door", "quest", "questdoor", "player" };

    public static string Normalize(string? type) => (type ?? string.Empty).Replace("_", "").Replace(" ", "").ToLowerInvariant();

    public static bool IsWaypoint(string? type) => Normalize(type) == "waypoint";

    public static bool IsNeverTravelTarget(string? type) => NeverTravel.Contains(Normalize(type));
}

public static class MapTravelStructure
{
    public static bool CanTarget(MapView view, MapMarker marker) =>
        view == MapView.World && !MapMarkerKinds.IsWaypoint(marker.Type) && !MapMarkerKinds.IsNeverTravelTarget(marker.Type);
}

public sealed class MapTravelFlow
{
    public const double NoticeSeconds = 2.0;
    public const double SelectRadiusPixels = 50;
    public const string LiveContext = "world";
    public const string LegacyLocalContext = "local";
    public const string NotFoundText = "MARKER NOT FOUND";
    public const string UndiscoveredPrompt = "Cannot travel to an undiscovered location. Place a marker here?";
    public const string PlaceMarkerQuestion = "Place a marker here?";
    public const string CannotTravelPrompt = "Cannot travel there. " + PlaceMarkerQuestion;

    private readonly IMapTravelRules _rules;
    private MapSource? _world;
    private MapSource? _local;
    private MapView _view = MapView.Local;
    private bool _initialised;
    private string? _worldIdentity;
    private MapWaypoint? _waypoint;
    private int _generation;
    private MapChoice? _choice;
    private int _choiceIndex;
    private bool _legend;
    private double _noticeLeft;
    private string _lastInstruction = string.Empty;

    public MapTravelFlow(IMapTravelRules? rules = null)
    {
        _rules = rules ?? new OpenMapTravelRules();
    }

    public event Action<FastTravelRequest>? FastTravelRequested;

    public event Action<MapWaypoint?>? WaypointChanged;

    public event Action<string>? SoundRequested;

    public event Action<MapView>? ViewChanged;

    public event Action<string>? InstructionChanged;

    public event Action? Changed;

    public MapView View => _view;

    public MapWaypoint? Waypoint => _waypoint;

    public MapChoice? Choice => _choice;

    public int ChoiceIndex => _choiceIndex;

    public bool IsModalOpen => _choice is not null;

    public bool LegendVisible => _legend;

    public string Notice { get; private set; } = string.Empty;

    public MapSource? Current => _view == MapView.World ? _world : _local;

    public MapModel? Map => Current?.Model;

    public IReadOnlyList<MapModel> Models => new[] { _world?.Model, _local?.Model }.OfType<MapModel>().ToArray();

    public string? ContextId => Current?.ContextId;

    public bool SourceAvailable => Current is not null;

    public bool CanSwitch => (_view == MapView.World ? _local : _world) is not null;

    public string? UnavailableText => SourceAvailable ? null : _view == MapView.World ? "WORLD MAP UNAVAILABLE" : "LOCAL MAP UNAVAILABLE";

    public void SetSources(MapSource? world, MapSource? local, string? worldIdentity = null)
    {
        var identityChanged = worldIdentity is not null && _worldIdentity is not null && worldIdentity != _worldIdentity;
        var before = Map;
        _world = world;
        _local = local;
        if (worldIdentity is not null) _worldIdentity = worldIdentity;
        if (!_initialised || identityChanged)
        {
            _view = world is not null ? MapView.World : MapView.Local;
            _initialised = true;
        }
        if (identityChanged || !ReferenceEquals(before, Map)) ResetView();
        Raise();
    }

    public bool Switch()
    {
        if (!CanSwitch) return false;
        _view = _view == MapView.World ? MapView.Local : MapView.World;
        ResetView();
        Sound("Transition");
        ViewChanged?.Invoke(_view);
        Raise();
        return true;
    }

    public void Rebind()
    {
        _choice = null;
        _choiceIndex = 0;
        Raise();
    }

    public bool ToggleLegend()
    {
        if (!SourceAvailable || IsModalOpen) return false;
        _legend = !_legend;
        Raise();
        return true;
    }

    // Invariant: the live world and local maps share one coordinate space, so both sources carry LiveContext and a waypoint shows in whichever view holds its point.
    public void SetLiveSources(MapModel world, MapModel? local, string? worldIdentity = null) =>
        SetSources(new MapSource(world, LiveContext), local is null ? null : new MapSource(local, LiveContext), worldIdentity);

    public bool FindPlayer()
    {
        if (IsModalOpen || Map is not { } map || !map.CenterOnPlayer()) return false;
        UpdateSelection(map.ViewWidth / 2, map.ViewHeight / 2);
        Raise();
        return true;
    }

    public void SetWaypointExternal(MapWaypoint? waypoint)
    {
        _waypoint = waypoint is { ContextId: LegacyLocalContext } ? waypoint with { ContextId = LiveContext } : waypoint;
        _generation++;
        Raise();
    }

    public bool Pick(string id)
    {
        if (!SourceAvailable || IsModalOpen || Map is not { } map) return false;
        var changed = map.SelectedId != id;
        if (!map.SelectMarker(id, false)) return false;
        if (changed) Sound("MapRollover");
        Raise();
        return true;
    }

    public void UpdateSelection(double screenX, double screenY)
    {
        if (!SourceAvailable || IsModalOpen || Map is not { } map) return;
        var id = map.NearestToScreen(screenX, screenY, SelectRadiusPixels)?.Id;
        if (id == map.SelectedId) return;
        if (id is null) map.ClearSelection();
        else
        {
            map.SelectMarker(id, false);
            Sound("MapRollover");
        }
        Raise();
    }

    public bool Handle(MenuInputAction action, (double X, double Y)? cursor = null)
    {
        if (_choice is { } choice)
        {
            switch (action)
            {
                case MenuInputAction.Up: MoveChoice(-1); break;
                case MenuInputAction.Down: MoveChoice(1); break;
                case MenuInputAction.Accept: Choose(_choiceIndex); break;
                case MenuInputAction.Back: Choose(choice.CancelIndex); break;
            }
            return true;
        }
        if (action != MenuInputAction.Accept) return false;
        Accept(cursor);
        return true;
    }

    public bool Back()
    {
        if (_choice is not { } choice) return false;
        Choose(choice.CancelIndex);
        return true;
    }

    public void MoveChoice(int delta)
    {
        if (_choice is not { } choice) return;
        var next = Math.Clamp(_choiceIndex + delta, 0, choice.Options.Count - 1);
        if (next == _choiceIndex) return;
        _choiceIndex = next;
        Sound("Focus");
        Raise();
    }

    public void Accept((double X, double Y)? cursor = null)
    {
        if (!SourceAvailable || Map is not { } map) return;
        if (_choice is not null)
        {
            Choose(_choiceIndex);
            return;
        }
        var marker = map.SelectedId is { } id ? map.Marker(id) : null;
        var (cx, cy) = cursor ?? (map.CenterX, map.CenterY);
        if (marker is null || !MapTravelStructure.CanTarget(_view, marker) || (marker.Discovered && !marker.CanFastTravel))
        {
            PlaceOrMove(map, cx, cy);
            return;
        }
        if (!marker.Discovered)
        {
            Open(new MapChoice(UndiscoveredPrompt, null, Refused(), 1, marker.Id, marker.X, marker.Y, _generation));
            return;
        }
        var verdict = _rules.CanFastTravel(marker);
        if (!verdict.Allowed)
        {
            var prompt = string.IsNullOrWhiteSpace(verdict.Reason) ? CannotTravelPrompt : Sentence(verdict.Reason) + " " + PlaceMarkerQuestion;
            Open(new MapChoice(prompt, null, Refused(), 1, marker.Id, marker.X, marker.Y, _generation));
            return;
        }
        var options = new[]
        {
            new MapChoiceOption("YES", MapChoiceAction.Travel),
            new MapChoiceOption("NO", MapChoiceAction.Decline),
            new MapChoiceOption("PLACE MARKER", MapChoiceAction.PlaceMarker),
        };
        Open(new MapChoice("Travel to " + marker.Name + "?", null, options, 1, marker.Id, marker.X, marker.Y, _generation));
    }

    public bool Choose(int index)
    {
        if (_choice is not { } choice || index < 0 || index >= choice.Options.Count) return false;
        _choice = null;
        _choiceIndex = 0;
        if (choice.Generation != _generation || ContextId is not { } context)
        {
            Raise();
            return false;
        }
        switch (choice.Options[index].Action)
        {
            case MapChoiceAction.Travel:
                Sound("Confirm");
                FastTravelRequested?.Invoke(new FastTravelRequest(choice.MarkerId ?? string.Empty, choice.X, choice.Y));
                break;
            case MapChoiceAction.PlaceMarker:
            case MapChoiceAction.MoveMarker:
                Sound("Confirm");
                SetWaypoint(new MapWaypoint(context, choice.X, choice.Y));
                break;
            case MapChoiceAction.RemoveMarker:
                Sound("Confirm");
                SetWaypoint(null);
                break;
            default:
                Sound("Cancel");
                break;
        }
        Raise();
        return true;
    }

    public bool FocusMarker(string id)
    {
        foreach (var view in new[] { _view, _view == MapView.World ? MapView.Local : MapView.World })
        {
            var source = view == MapView.World ? _world : _local;
            if (source?.Model is not { } model || model.Marker(id) is not { } marker || !(marker.Discovered || model.ShowUndiscovered)) continue;
            var switched = view != _view;
            var (width, height) = Map is { } current ? (current.ViewWidth, current.ViewHeight) : (0.0, 0.0);
            _view = view;
            ResetView();
            if (switched) model.SetViewport(width, height);
            model.SetZoom(model.MinZoom + 0.5 * (model.MaxZoom - model.MinZoom));
            model.SelectMarker(id, true);
            Notice = string.Empty;
            _noticeLeft = 0;
            if (switched) ViewChanged?.Invoke(_view);
            Sound("MapRollover");
            Raise();
            return true;
        }
        Notice = NotFoundText;
        _noticeLeft = NoticeSeconds;
        Raise();
        return false;
    }

    public void Tick(double seconds)
    {
        if (_noticeLeft <= 0 || !(seconds > 0)) return;
        _noticeLeft -= seconds;
        if (_noticeLeft > 0) return;
        Notice = string.Empty;
        Raise();
    }

    public TravelVerdict VerdictFor(MapMarker marker)
    {
        if (!MapTravelStructure.CanTarget(_view, marker) || !marker.Discovered || !marker.CanFastTravel) return TravelVerdict.No();
        return _rules.CanFastTravel(marker);
    }

    public IReadOnlyList<MapAction> Actions()
    {
        var actions = new List<MapAction>();
        if (IsModalOpen) return actions;
        if (SourceAvailable && Map is { } map)
        {
            var marker = map.SelectedId is { } id ? map.Marker(id) : null;
            var canTravel = marker is not null && VerdictFor(marker).Allowed;
            if (canTravel) actions.Add(MapAction.FastTravel);
            if (!canTravel) actions.Add(MapAction.PlaceMarker);
        }
        if (CanSwitch) actions.Add(MapAction.SwitchView);
        if (SourceAvailable) actions.Add(MapAction.Legend);
        return actions;
    }

    public string InstructionText(Func<MapAction, string>? keyName = null)
    {
        var key = keyName ?? DefaultKey;
        return string.Join("   ", Actions().Select(a => key(a) + " " + LabelFor(a)));
    }

    private static string DefaultKey(MapAction action) => action switch
    {
        MapAction.SwitchView => "R",
        MapAction.Legend => "L",
        _ => "ENTER",
    };

    public string LabelFor(MapAction action) => action switch
    {
        MapAction.FastTravel => "FAST TRAVEL",
        MapAction.PlaceMarker => "PLACE MARKER",
        MapAction.SwitchView => _view == MapView.World ? "LOCAL MAP" : "WORLD MAP",
        _ => _legend ? "HIDE LEGEND" : "SHOW LEGEND",
    };

    private static MapChoiceOption[] Refused() => new[]
    {
        new MapChoiceOption("YES", MapChoiceAction.PlaceMarker),
        new MapChoiceOption("NO", MapChoiceAction.Decline),
    };

    private void PlaceOrMove(MapModel map, double x, double y)
    {
        var inside = x >= map.Bounds.MinX && x <= map.Bounds.MaxX && y >= map.Bounds.MinY && y <= map.Bounds.MaxY;
        if (!inside || ContextId is not { } context) return;
        if (_waypoint is null)
        {
            Sound("Confirm");
            SetWaypoint(new MapWaypoint(context, x, y));
            Raise();
            return;
        }
        var options = new[]
        {
            new MapChoiceOption("MOVE MARKER", MapChoiceAction.MoveMarker),
            new MapChoiceOption("REMOVE MARKER", MapChoiceAction.RemoveMarker),
            new MapChoiceOption("CANCEL", MapChoiceAction.Decline),
        };
        Open(new MapChoice("Move the marker?", null, options, 2, null, x, y, _generation));
    }

    private void SetWaypoint(MapWaypoint? waypoint)
    {
        _waypoint = waypoint;
        _generation++;
        WaypointChanged?.Invoke(waypoint);
    }

    private void Open(MapChoice choice)
    {
        _choice = choice;
        _choiceIndex = 0;
        Raise();
    }

    private static string Sentence(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length > 0 && ".!?".Contains(trimmed[^1]) ? trimmed : trimmed + ".";
    }

    private void ResetView()
    {
        _choice = null;
        _choiceIndex = 0;
        Map?.ClearSelection();
    }

    private void Sound(string name) => SoundRequested?.Invoke(name);

    private void Raise()
    {
        Changed?.Invoke();
        var line = InstructionText();
        if (line == _lastInstruction) return;
        _lastInstruction = line;
        InstructionChanged?.Invoke(line);
    }
}
