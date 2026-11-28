using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Dialogs;
using OpenCommonwealth.Services.Ui.Lockpick;
using OpenCommonwealth.Services.Ui.Terminals;
using OpenCommonwealth.Services.Ui.Vats;

namespace OpenCommonwealth.Services.Ui.Gallery;

public sealed class SampleTerminal : ITerminalSource
{
    public const string OpenId = "sample_open";
    public const string LockedId = "sample_locked";

    private bool _unlocked;

    private static readonly Dictionary<string, TerminalNode> Nodes = new()
    {
        ["root"] = new TerminalNode("root", "ROBCO INDUSTRIES (TM) TERMLINK", "Welcome, sample user.",
            "Choose a record. Every entry here is sample data.",
            new[]
            {
                new TerminalEntry("records", "Personnel records", TerminalEntryKind.Submenu, TargetNode: "records"),
                new TerminalEntry("log", "Read the maintenance log", TerminalEntryKind.Text,
                    PageText: "Maintenance log, sample entry one.\nThe fusion core was replaced.\nThe door motor still sticks.\n\nSample entry two.\nNo faults reported since."),
                new TerminalEntry("door", "Open the sample door", TerminalEntryKind.Redraw, Response: "Door opened."),
                new TerminalEntry("image", "View the floor plan", TerminalEntryKind.Image),
                new TerminalEntry("odd", "Run a diagnostic", TerminalEntryKind.Unsupported),
                new TerminalEntry("off", "Disable the alarm", TerminalEntryKind.Redraw, Unavailable: "Access restricted"),
            }),
        ["records"] = new TerminalNode("records", "PERSONNEL RECORDS", "Records.",
            "Select a person.",
            new[]
            {
                new TerminalEntry("a", "Sample Person A", TerminalEntryKind.Text, PageText: "Sample Person A.\nRole: engineer.\nStatus: on duty."),
                new TerminalEntry("b", "Sample Person B", TerminalEntryKind.Text, PageText: "Sample Person B.\nRole: guard.\nStatus: on leave."),
                new TerminalEntry("top", "Return to the top menu", TerminalEntryKind.ReturnToTop),
            }),
    };

    public TerminalOpening Open(string terminalId) => terminalId switch
    {
        OpenId => new TerminalOpening(TerminalOpenStatus.Open, "root"),
        LockedId when _unlocked => new TerminalOpening(TerminalOpenStatus.Open, "root"),
        LockedId => new TerminalOpening(TerminalOpenStatus.Locked, "root", Hack: new SampleHacking()),
        _ => new TerminalOpening(TerminalOpenStatus.Unavailable, Reason: "No such sample terminal."),
    };

    public TerminalNode? Node(string terminalId, string nodeId) => Nodes.GetValueOrDefault(nodeId);

    public bool IsValid(string terminalId) => true;

    public void EntryRun(string terminalId, string nodeId, string entryId)
    {
    }

    public void HackFinished(string terminalId, HackingOutcome outcome)
    {
        if (outcome == HackingOutcome.Accepted)
            _unlocked = true;
    }
}

public sealed class SampleHacking : IHackingSession
{
    private const string Password = "CORD";

    private readonly char[] _cells = new char[384];
    private readonly List<(int Start, string Word)> _words = new();
    private int _attempts = 4;
    private bool _dudUsed;

    public SampleHacking()
    {
        const string filler = ".,;:!?%$#@*-+=";
        var random = new Random(7);
        for (int i = 0; i < _cells.Length; i++)
            _cells[i] = filler[random.Next(filler.Length)];
        Place(6, "COLD");
        Place(72, Password);
        Place(132, "HOLD");
        Place(200, "COOL");
        Place(264, "FORD");
        Place(330, "WORD");
        Bracket(100, "(..)");
        Bracket(290, "[..]");
    }

    public HackingGeometry Geometry => HackingGeometry.Standard;

    public int MaxAttempts => 4;

    public int AttemptsRemaining => _attempts;

    public bool StillValid => true;

    public char CellAt(int index) => _cells[index];

    public HackingHit? HitAt(int index)
    {
        foreach (var (start, word) in _words)
            if (index >= start && index < start + word.Length)
                return new HackingHit(start, word.Length, HackingHitKind.Word);
        foreach (int start in new[] { 100, 290 })
            if (index >= start && index < start + 4 && _cells[start] != '.')
                return new HackingHit(start, 4, HackingHitKind.Bracket);
        return null;
    }

    public HackingGuess Select(HackingHit hit)
    {
        if (hit.Kind == HackingHitKind.Bracket)
        {
            for (int i = 0; i < hit.Length; i++)
                _cells[hit.Start + i] = '.';
            if (_dudUsed)
            {
                _attempts = MaxAttempts;
                return new HackingGuess(HackingGuessKind.AttemptsRestored);
            }
            _dudUsed = true;
            int dud = _words.FindIndex(w => w.Word != Password);
            var (start, word) = _words[dud];
            for (int i = 0; i < word.Length; i++)
                _cells[start + i] = '.';
            _words.RemoveAt(dud);
            return new HackingGuess(HackingGuessKind.DudRemoved);
        }
        string guess = new(_cells, hit.Start, hit.Length);
        if (guess == Password)
            return new HackingGuess(HackingGuessKind.Correct);
        if (--_attempts <= 0)
            return new HackingGuess(HackingGuessKind.LockedOut);
        int likeness = 0;
        for (int i = 0; i < guess.Length; i++)
            likeness += guess[i] == Password[i] ? 1 : 0;
        return new HackingGuess(HackingGuessKind.Wrong, likeness);
    }

    private void Place(int start, string word)
    {
        _words.Add((start, word));
        for (int i = 0; i < word.Length; i++)
            _cells[start + i] = word[i];
    }

    private void Bracket(int start, string text)
    {
        for (int i = 0; i < text.Length; i++)
            _cells[start + i] = text[i];
    }
}

public sealed class SampleLockpick : ILockpickSession
{
    private const double Sweet = 20;
    private readonly List<string> _cues = new();
    private double _pick;
    private double _turn;
    private double _condition = 100;
    private int _pins = 4;
    private bool _stressed;
    private LockpickPhase _phase = LockpickPhase.Active;

    public SampleLockpick(LockLevel level = LockLevel.Advanced)
    {
        Level = level;
    }

    public LockLevel Level { get; }

    public LockpickView View => new(Level, _pick, _turn, _condition, _pins, _stressed, _phase);

    public bool StillValid => true;

    public void MovePick(double degrees) => _pick = Math.Clamp(_pick + degrees, -90, 90);

    public void Step(double turnInput, double seconds)
    {
        if (_phase != LockpickPhase.Active)
            return;
        double allowed = 90 * (1 - Math.Min(Math.Abs(_pick - Sweet) / 40, 1));
        if (turnInput > 0)
        {
            _turn = Math.Min(90, _turn + 120 * seconds);
            _stressed = _turn > allowed;
            if (_stressed)
            {
                _turn = allowed;
                _condition -= 60 * seconds;
                if (_condition <= 0)
                    BreakPin();
            }
            else if (_turn >= 90)
                _phase = LockpickPhase.Solved;
        }
        else
        {
            _stressed = false;
            _turn = Math.Max(0, _turn - 240 * seconds);
        }
    }

    public IReadOnlyList<string> DrainCues()
    {
        var cues = _cues.ToArray();
        _cues.Clear();
        return cues;
    }

    private void BreakPin()
    {
        _cues.Add("pin_break");
        _pins--;
        _condition = 100;
        _turn = 0;
        _stressed = false;
        if (_pins <= 0)
            _phase = LockpickPhase.OutOfPins;
    }
}

public sealed class SampleVats : IVatsTargets
{
    private static VatsPart Part(string id, string name, int chance, int cost, double x, double y) =>
        new(id, name, chance, 100, false, cost, new VatsPoint(x, y));

    private static readonly IReadOnlyList<VatsResistance> Resistances = new[]
    {
        new VatsResistance("DR", "12"),
        new VatsResistance("ER", "8"),
        new VatsResistance("RR", "100", true),
    };

    private readonly VatsTarget[] _targets =
    {
        new("raider", "Sample Raider", 8, 80, true, Resistances, new[]
        {
            Part("head", "Head", 62, 25, 960, 300),
            Part("torso", "Torso", 88, 20, 960, 480),
            Part("larm", "Left arm", 70, 15, 820, 500),
            Part("rarm", "Right arm", 70, 15, 1100, 500),
            Part("legs", "Legs", 80, 15, 960, 700),
        }),
        new("ghoul", "Sample Ghoul", 12, 100, true, Resistances, new[]
        {
            Part("head", "Head", 48, 25, 1400, 330),
            Part("torso", "Torso", 75, 20, 1400, 500),
        }),
    };

    public string? InitialTargetId => "raider";

    public VatsSnapshot Snapshot() => new(_targets, 80, 100, 0.5, true, true);
}

public static class SampleDialogue
{
    public static DialogueNode Start { get; } = new("start", "Sample Settler",
        "Welcome, stranger. What brings you to the settlement?",
        new[]
        {
            new DialogueChoice("Tell me about this place.", "about"),
            new DialogueChoice("I can talk you into sharing supplies.", "persuade", DialogueChallenge.Hard),
            new DialogueChoice("What do you have for sale?", "trade", Kind: DialogueChoiceKind.Trade),
            new DialogueChoice("Goodbye.", "bye", Kind: DialogueChoiceKind.Exit),
        }, CanExit: true);

    public static DialogueNode About { get; } = new("about", "Sample Settler",
        "We farm, we trade, and we keep the raiders out.",
        new[]
        {
            new DialogueChoice("Anything else?", "back"),
            new DialogueChoice("Goodbye.", "bye", Kind: DialogueChoiceKind.Exit),
        }, CanExit: true);

    public static DialogueNode Persuaded { get; } = new("persuaded", "Sample Settler",
        "Fine. Take what you need from the store room.",
        new[] { new DialogueChoice("Thanks.", "bye", Kind: DialogueChoiceKind.Exit) }, CanExit: true);

    public static DialogueNode Next(string tag) => tag switch
    {
        "about" => About,
        "back" => Start,
        "persuade" => Persuaded,
        _ => Start,
    };
}
