using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Service;

public sealed class InMemorySetupSteps : ISetupSteps
{
    public const string ArchiveStep = "archive";
    public const string OutputStep = "output";
    public const string RunStep = "run";

    private static readonly string[] Phases = ["Extracting movies", "Converting art"];

    private readonly SetupStepInfo[] _steps =
    [
        new(ArchiveStep, "Game interface archive", "The archive that holds the original menu files. Edit the path if it was not found.", Editable: true),
        new(OutputStep, "Output folder", "Where converted menu data is written under your user data.", Editable: true),
        new(RunStep, "Extract and convert", "Reads the archive and writes converted menu data. Can be cancelled and resumed.", Runs: true),
    ];

    public InMemorySetupSteps(IEnumerable<string> items)
    {
        Items = items.ToList();
    }

    public List<string> Items { get; }

    public HashSet<string> FailItems { get; } = new();

    public string ArchiveDetected { get; set; } = "Interface.ba2";

    public string OutputDetected { get; set; } = "Converted";

    public ServiceResult CompleteResult { get; set; } = ServiceResult.Success();

    public int CompleteCalls { get; private set; }

    public int StartCalls { get; private set; }

    public IReadOnlyList<SetupStepInfo> Steps => _steps;

    public bool IsComplete { get; private set; }

    public string Detect(string stepId) => stepId switch
    {
        ArchiveStep => ArchiveDetected,
        OutputStep => OutputDetected,
        _ => string.Empty,
    };

    public SetupValidation Validate(string stepId, string value) => stepId switch
    {
        ArchiveStep => value.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase)
            ? new SetupValidation(true, "Archive found.")
            : new SetupValidation(false, "The archive path must end in .ba2."),
        OutputStep => value.Trim().Length > 0
            ? new SetupValidation(true, "Output folder is usable.")
            : new SetupValidation(false, "Enter an output folder."),
        _ => new SetupValidation(true, string.Empty),
    };

    public ISetupRun? Start(string stepId, string value)
    {
        if (stepId != RunStep) return null;
        StartCalls++;
        return new Run(Items.ToList(), FailItems);
    }

    public ServiceResult MarkComplete()
    {
        CompleteCalls++;
        if (CompleteResult.Ok) IsComplete = true;
        return CompleteResult;
    }

    private sealed class Run : ISetupRun
    {
        private readonly List<string> _items;
        private readonly HashSet<string> _fail;
        private readonly List<SetupItemResult> _results = new();
        private int _position;

        public Run(List<string> items, HashSet<string> fail)
        {
            _items = items;
            _fail = fail;
            Progress = new SetupProgress(Phases[0], 0, Phases.Length, 0, items.Count, string.Empty);
            Finished = items.Count == 0;
        }

        public SetupProgress Progress { get; private set; }

        public bool Finished { get; private set; }

        public bool Cancelled { get; private set; }

        public IReadOnlyList<SetupItemResult> Results => _results;

        public bool Advance()
        {
            if (Finished) return false;
            if (Cancelled)
            {
                Finished = true;
                return false;
            }
            var item = _items[_position % _items.Count];
            _results.Add(_fail.Contains(item)
                ? new SetupItemResult(item, SetupItemState.Error, "Could not read this item.")
                : new SetupItemResult(item, SetupItemState.Written));
            _position++;
            var nextPhase = Math.Min(_position / _items.Count, Phases.Length - 1);
            var done = _position >= _items.Count * Phases.Length ? _items.Count : _position % _items.Count;
            Progress = new SetupProgress(Phases[nextPhase], nextPhase, Phases.Length, done, _items.Count, item);
            if (_position >= _items.Count * Phases.Length) Finished = true;
            return !Finished;
        }

        public void Cancel()
        {
            if (!Finished) Cancelled = true;
        }
    }
}
