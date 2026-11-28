using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Terminals;

namespace OpenCw.Ui.Tests;

sealed class FakeTerminalSource : ITerminalSource
{
    public Dictionary<string, TerminalNode> Nodes { get; } = new();
    public TerminalOpening Opening { get; set; } = new(TerminalOpenStatus.Open, "root");
    public bool Valid { get; set; } = true;
    public List<string> Runs { get; } = new();
    public List<HackingOutcome> Hacks { get; } = new();
    public int NodeCalls { get; private set; }

    public TerminalOpening Open(string terminalId) => Opening;

    public TerminalNode? Node(string terminalId, string nodeId)
    {
        NodeCalls++;
        return Nodes.GetValueOrDefault(nodeId);
    }

    public bool IsValid(string terminalId) => Valid;

    public void EntryRun(string terminalId, string nodeId, string entryId) => Runs.Add(nodeId + "/" + entryId);

    public void HackFinished(string terminalId, HackingOutcome outcome)
    {
        Hacks.Add(outcome);
        if (outcome == HackingOutcome.Accepted)
            Opening = new TerminalOpening(TerminalOpenStatus.Open, "root");
    }

    public static TerminalNode Node(string id, string header, string welcome, string body, params TerminalEntry[] entries) =>
        new(id, header, welcome, body, entries);

    public static TerminalEntry Entry(string id, string text, TerminalEntryKind kind = TerminalEntryKind.Submenu,
        string? target = null, string? response = null, string? page = null, string? unavailable = null) =>
        new(id, text, kind, response, target, page, unavailable);
}

sealed class FakeHackingSession : IHackingSession
{
    public const string Password = "CORD";

    private readonly char[] _cells = new string('.', 384).ToCharArray();
    private readonly List<(int Start, string Word)> _words = new();

    public FakeHackingSession(int attempts = 4)
    {
        Attempts = attempts;
        MaxAttempts = attempts;
        Place(0, "COLD");
        Place(12, Password);
        Place(192, "HOLD");
        _cells[30] = '(';
        _cells[33] = ')';
    }

    public Queue<HackingGuess> Scripted { get; } = new();
    public List<HackingHit> Selected { get; } = new();
    public bool Valid { get; set; } = true;
    public int Attempts { get; set; }

    public HackingGeometry Geometry => HackingGeometry.Standard;
    public int MaxAttempts { get; }
    public int AttemptsRemaining => Attempts;
    public bool StillValid => Valid;

    public char CellAt(int index) => _cells[index];

    public HackingHit? HitAt(int index)
    {
        foreach (var (start, word) in _words)
        {
            if (index >= start && index < start + word.Length)
                return new HackingHit(start, word.Length, HackingHitKind.Word);
        }
        return index >= 30 && index <= 33 ? new HackingHit(30, 4, HackingHitKind.Bracket) : null;
    }

    public HackingGuess Select(HackingHit hit)
    {
        Selected.Add(hit);
        if (Scripted.Count > 0)
            return Scripted.Dequeue();
        string word = new string(_cells, hit.Start, hit.Length);
        if (word == Password)
            return new HackingGuess(HackingGuessKind.Correct);
        Attempts--;
        if (Attempts <= 0)
            return new HackingGuess(HackingGuessKind.LockedOut);
        int likeness = 0;
        for (int i = 0; i < word.Length; i++)
            likeness += word[i] == Password[i] ? 1 : 0;
        return new HackingGuess(HackingGuessKind.Wrong, likeness);
    }

    private void Place(int start, string word)
    {
        _words.Add((start, word));
        for (int i = 0; i < word.Length; i++)
            _cells[start + i] = word[i];
    }
}
