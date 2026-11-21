namespace OpenCommonwealth.Services.Ui.Terminals;

public enum HackingHitKind
{
    Word,
    Bracket,
}

public readonly record struct HackingHit(int Start, int Length, HackingHitKind Kind)
{
    public bool Contains(int index) => index >= Start && index < Start + Length;
}

public enum HackingGuessKind
{
    Ignored,
    Wrong,
    Correct,
    DudRemoved,
    AttemptsRestored,
    LockedOut,
}

public readonly record struct HackingGuess(HackingGuessKind Kind, int Likeness = 0);

public enum HackingOutcome
{
    Accepted,
    LockedOut,
    Cancelled,
}

public readonly record struct HackingGeometry(int Columns, int RowsPerColumn, int CellsPerRow)
{
    public static HackingGeometry Standard { get; } = new(2, 16, 12);

    public int CellsPerColumn => RowsPerColumn * CellsPerRow;

    public int Cells => Columns * CellsPerColumn;
}

public interface IHackingSession
{
    HackingGeometry Geometry { get; }

    int MaxAttempts { get; }

    int AttemptsRemaining { get; }

    bool StillValid { get; }

    char CellAt(int index);

    HackingHit? HitAt(int index);

    HackingGuess Select(HackingHit hit);
}

public sealed record HackingStrings
{
    public string Login { get; init; } = "PASSWORD REQUIRED";
    public string Attempts { get; init; } = "ATTEMPTS LEFT";
    public string Denied { get; init; } = "Entry denied.";
    public string Likeness { get; init; } = "Likeness={0}";
    public string Granted { get; init; } = "Access granted.";
    public string LockedOut { get; init; } = "Terminal locked.";
    public string DudRemoved { get; init; } = "Dud removed.";
    public string Restored { get; init; } = "Attempts restored.";
}

public sealed record HackingOptions
{
    public double CharsPerSecond { get; init; } = 60;
    public double AcceptedHold { get; init; } = 5.0;
    public double LockedOutHold { get; init; } = 3.0;
    public int LogLines { get; init; } = 15;
    public int LogColumns { get; init; } = 34;
    public HackingStrings Strings { get; init; } = new();
}
