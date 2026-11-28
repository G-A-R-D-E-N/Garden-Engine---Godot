using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed record LevelUpChoice(string EntryId, string Name, int Rank, int From, PerkChartKind Kind)
{
    public string Line => Kind == PerkChartKind.Attribute
        ? string.Create(CultureInfo.InvariantCulture, $"{Name} {From} -> {Rank}")
        : string.Create(CultureInfo.InvariantCulture, $"{Name} rank {Rank}");
}

public sealed class LevelUpSession
{
    public const double MessageSeconds = 2.0;
    public const string AllSpentText = "All perk points spent.";

    private readonly IProgressionService _service;
    private readonly List<LevelUpChoice> _choices = new();
    private readonly List<string> _banner = new();
    private double _messageLeft;
    private int _lastLevel;
    private bool _closed;

    private LevelUpSession(IProgressionService service)
    {
        _service = service;
        var snapshot = service.Snapshot;
        _lastLevel = snapshot.Level;
        Chart = new PerkChartController(snapshot.PerkPoints > 0 ? PerkChartMode.Spend : PerkChartMode.Browse);
        Chart.SetSnapshot(snapshot.Chart);
        Chart.ChoiceRequested += OnChoice;
        Chart.CloseRequested += Close;
        Chart.SoundRequested += RaiseSound;
        service.Changed += OnServiceChanged;
        OpenSounds = snapshot.PerkPoints > 0 ? new[] { "menu.open", "levelup.fanfare" } : new[] { "menu.open" };
    }

    public event Action<string>? SoundRequested;

    public event Action<int, IReadOnlyList<LevelUpChoice>>? Closed;

    public event Action? Changed;

    public PerkChartController Chart { get; }

    public IReadOnlyList<string> OpenSounds { get; }

    public bool IsClosed => _closed;

    public string Message { get; private set; } = string.Empty;

    public IReadOnlyList<LevelUpChoice> Choices => _choices.ToArray();

    public IReadOnlyList<string> Summary => _choices.Select(c => c.Line).ToArray();

    public IReadOnlyList<string> BannerLines => _banner.ToArray();

    public bool AllSpent => _choices.Count > 0 && _service.Snapshot.PerkPoints == 0;

    public string StatusLine => Message.Length > 0 ? Message : AllSpent ? AllSpentText : string.Empty;

    public string LevelLine => Chart.HeaderLine;

    public string PointsLine => string.Create(CultureInfo.InvariantCulture, $"Perk points: {Chart.PerkPoints}");

    public bool AtCap => _service.Snapshot.LevelCap is { } cap && _service.Snapshot.Level >= cap;

    public bool ShowXpBar => !AtCap;

    public double XpFraction
    {
        get
        {
            var s = _service.Snapshot;
            if (AtCap || !(s.XpForNextLevel > 0)) return 1;
            return Math.Clamp(s.XpIntoLevel / s.XpForNextLevel, 0, 1);
        }
    }

    public string XpText
    {
        get
        {
            var s = _service.Snapshot;
            if (AtCap || !(s.XpForNextLevel > 0)) return "Max level";
            var percent = (int)Math.Floor(XpFraction * 100);
            return string.Create(CultureInfo.InvariantCulture,
                $"{(long)Math.Floor(s.XpIntoLevel)} / {(long)Math.Floor(s.XpForNextLevel)} XP ({percent}%)");
        }
    }

    public IReadOnlyList<string> SpecialStrip
    {
        get
        {
            var s = _service.Snapshot.Chart;
            return Enumerable.Range(0, PerkChartController.AttributeColumns).Select(c =>
            {
                var head = s.Entries.FirstOrDefault(e => e.Kind == PerkChartKind.Attribute && e.Column == c);
                return string.Create(CultureInfo.InvariantCulture, $"{head?.Name ?? "#" + (c + 1)} {(c < s.Attributes.Count ? s.Attributes[c].ToString(CultureInfo.InvariantCulture) : "--")}");
            }).ToArray();
        }
    }

    public IReadOnlyList<MenuHint> Hints => Chart.Hints;

    public static LevelUpSession? TryOpen(IProgressionService? service, bool anotherModalOpen, bool treePaused)
    {
        if (service is null || anotherModalOpen || treePaused || !service.Snapshot.IsValid) return null;
        return new LevelUpSession(service);
    }

    public bool Handle(MenuInputAction action) => !_closed && Chart.Handle(action);

    public void Tick(double seconds)
    {
        if (_closed || !(seconds > 0)) return;
        Chart.Tick(seconds);
        if (_messageLeft > 0)
        {
            _messageLeft -= seconds;
            if (_messageLeft <= 0)
            {
                Message = string.Empty;
                Changed?.Invoke();
            }
        }
    }

    public void Close()
    {
        if (_closed) return;
        var points = _service.Snapshot.PerkPoints;
        var choices = _choices.ToArray();
        Detach();
        SoundRequested?.Invoke("menu.close");
        Closed?.Invoke(points, choices);
        _choices.Clear();
        _banner.Clear();
    }

    public void Discard()
    {
        if (_closed) return;
        Detach();
        _choices.Clear();
        _banner.Clear();
    }

    private void Detach()
    {
        _closed = true;
        _service.Changed -= OnServiceChanged;
        Chart.ChoiceRequested -= OnChoice;
        Chart.CloseRequested -= Close;
        Chart.SoundRequested -= RaiseSound;
        Chart.Dismiss();
    }

    private void RaiseSound(string name) => SoundRequested?.Invoke(name);

    private void OnServiceChanged()
    {
        if (_closed) return;
        Apply(_service.Snapshot);
        Changed?.Invoke();
    }

    private void Apply(ProgressionSnapshot snapshot)
    {
        Chart.SetMode(snapshot.PerkPoints > 0 ? PerkChartMode.Spend : PerkChartMode.Browse);
        Chart.SetSnapshot(snapshot.Chart);
        if (snapshot.Level > _lastLevel)
        {
            _banner.Add(string.Create(CultureInfo.InvariantCulture, $"Level {snapshot.Level} reached"));
            _lastLevel = snapshot.Level;
        }
    }

    private void OnChoice(string entryId, int rank)
    {
        var before = _service.Snapshot;
        var entry = before.Chart.Entries.FirstOrDefault(e => e.Id == entryId);
        var wasAvailable = before.Chart.Entries.Where(e => e.Available).Select(e => e.Id).ToHashSet();
        var result = _service.TryChoose(entryId, rank);
        if (result != ChooseResult.Ok)
        {
            Say(Reason(result));
            Chart.Unlock();
            Changed?.Invoke();
            return;
        }
        var after = _service.Snapshot;
        Apply(after);
        if (entry is null)
        {
            Chart.Unlock();
            Changed?.Invoke();
            return;
        }
        _choices.Add(new LevelUpChoice(entry.Id, entry.Name, rank, entry.CurrentRank, entry.Kind));
        Chart.MarkNew(after.Chart.Entries.Where(e => e.Available && !wasAvailable.Contains(e.Id) && e.Id != entryId).Select(e => e.Id));
        if (entry.Kind == PerkChartKind.Attribute) SoundRequested?.Invoke("special.up");
        Changed?.Invoke();
    }

    private void Say(string text)
    {
        Message = text;
        _messageLeft = MessageSeconds;
    }

    private static string Reason(ChooseResult result) => result switch
    {
        ChooseResult.NoPoints => "No perk points left",
        ChooseResult.Locked => "Requirements not met",
        ChooseResult.AlreadyMax => "Already at maximum",
        _ => "That choice is no longer available",
    };
}

public sealed class LevelUpWatcher
{
    public const double OpenDelaySeconds = 1.5;
    public const double RetrySeconds = 1.0;
    public const double ReminderSeconds = 300.0;
    public const string ReminderText = "Perk points available. Open the level-up menu.";

    private readonly Func<bool> _canInterrupt;
    private bool _pending;
    private double _wait;
    private double _remind;

    public LevelUpWatcher(Func<bool> canInterrupt)
    {
        _canInterrupt = canInterrupt;
    }

    public event Action? OpenRequested;

    public event Action<int>? ReminderDue;

    public bool AutoOpen { get; set; } = true;

    public bool MenuOpen { get; set; }

    public int PerkPoints { get; set; }

    public bool IsPending => _pending;

    public static string GainedNotice(int level, int points) =>
        string.Create(CultureInfo.InvariantCulture, $"Level {level} reached. Perk points: {points}");

    public void LevelGained(int points)
    {
        PerkPoints = points;
        if (!AutoOpen || MenuOpen) return;
        _pending = true;
        _wait = OpenDelaySeconds;
    }

    public void CancelPending() => _pending = false;

    public void Tick(double seconds)
    {
        if (!(seconds > 0)) return;
        if (MenuOpen) _pending = false;
        if (_pending)
        {
            _wait -= seconds;
            if (_wait <= 0)
            {
                if (_canInterrupt())
                {
                    _pending = false;
                    OpenRequested?.Invoke();
                }
                else
                {
                    _wait = RetrySeconds;
                }
            }
        }
        if (PerkPoints <= 0 || MenuOpen)
        {
            _remind = 0;
            return;
        }
        _remind += seconds;
        if (_remind < ReminderSeconds) return;
        _remind = 0;
        ReminderDue?.Invoke(PerkPoints);
    }
}
