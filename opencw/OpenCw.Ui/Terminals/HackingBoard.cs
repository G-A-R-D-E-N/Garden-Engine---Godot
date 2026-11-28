namespace OpenCommonwealth.Services.Ui.Terminals;

public sealed class HackingBoard : IHackingSession
{
    public const string Junk = "!@#$%^*()_+=-`[]{}|;':,./<>?\\\"";
    public const char Removed = '.';

    private const string Openers = "([{<";
    private const string Closers = ")]}>";

    private sealed class Word(int start, string text)
    {
        public int Start { get; } = start;
        public string Text { get; } = text;
        public bool Dud { get; set; }
    }

    private readonly char[] _cells;
    private readonly List<Word> _words = new();
    private readonly HashSet<int> _usedBrackets = new();
    private readonly HashSet<int> _guessed = new();
    private readonly Random _random;
    private readonly Word _password;
    private bool _resetUsed;
    private bool _over;

    public HackingBoard(IReadOnlyList<string> pool, int wordCount, int guesses, int seed)
    {
        ArgumentNullException.ThrowIfNull(pool);
        _random = new Random(seed);
        string[] distinct = pool.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0 || distinct.Any(w => w.Length != distinct[0].Length || w.Length > HackingRules.MaxWordLength))
            throw new ArgumentException("The word pool must hold same-length words of at most 12 letters.", nameof(pool));
        int count = Math.Min(wordCount, distinct.Length);
        if (count < 2)
            throw new ArgumentException("A board needs at least two words.", nameof(pool));
        MaxAttempts = guesses;
        AttemptsRemaining = guesses;
        Geometry = HackingGeometry.Standard;
        _cells = new char[Geometry.Cells];
        Array.Fill(_cells, '\0');
        Place(Shuffle(distinct), count, distinct[0].Length);
        for (int i = 0; i < _cells.Length; i++)
            if (_cells[i] == '\0')
                _cells[i] = Junk[_random.Next(Junk.Length)];
        _password = _words[_random.Next(_words.Count)];
    }

    public HackingGeometry Geometry { get; }

    public int MaxAttempts { get; }

    public int AttemptsRemaining { get; private set; }

    public bool StillValid => true;

    internal string Password => _password.Text;

    public char CellAt(int index) => _cells[index];

    public HackingHit? HitAt(int index)
    {
        if (index < 0 || index >= _cells.Length)
            return null;
        foreach (Word word in _words)
            if (!word.Dud && index >= word.Start && index < word.Start + word.Text.Length)
                return new HackingHit(word.Start, word.Text.Length, HackingHitKind.Word);
        return BracketAt(index);
    }

    public HackingGuess Select(HackingHit hit)
    {
        if (_over)
            return new HackingGuess(HackingGuessKind.Ignored);
        if (hit.Kind == HackingHitKind.Bracket)
            return HitAt(hit.Start) is { Kind: HackingHitKind.Bracket } live && live == hit ? UseBracket(hit.Start) : new HackingGuess(HackingGuessKind.Ignored);
        Word? word = _words.Find(w => !w.Dud && w.Start == hit.Start && w.Text.Length == hit.Length);
        if (word is null)
            return new HackingGuess(HackingGuessKind.Ignored);
        if (word == _password)
        {
            _over = true;
            return new HackingGuess(HackingGuessKind.Correct);
        }
        if (!_guessed.Add(word.Start))
            return new HackingGuess(HackingGuessKind.Ignored);
        if (--AttemptsRemaining <= 0)
        {
            AttemptsRemaining = 0;
            _over = true;
            return new HackingGuess(HackingGuessKind.LockedOut);
        }
        return new HackingGuess(HackingGuessKind.Wrong, Likeness(word.Text, _password.Text));
    }

    private static int Likeness(string guess, string password)
    {
        int likeness = 0;
        for (int i = 0; i < Math.Min(guess.Length, password.Length); i++)
            if (guess[i] == password[i])
                likeness++;
        return likeness;
    }

    private HackingGuess UseBracket(int start)
    {
        _usedBrackets.Add(start);
        if (!_resetUsed && _random.Next(4) == 0)
        {
            _resetUsed = true;
            AttemptsRemaining = MaxAttempts;
            return new HackingGuess(HackingGuessKind.AttemptsRestored);
        }
        List<Word> duds = _words.FindAll(w => w != _password && !w.Dud);
        Word dud = duds[_random.Next(duds.Count)];
        dud.Dud = true;
        for (int i = 0; i < dud.Text.Length; i++)
            _cells[dud.Start + i] = Removed;
        return new HackingGuess(HackingGuessKind.DudRemoved);
    }

    private HackingHit? BracketAt(int index)
    {
        int kind = Openers.IndexOf(_cells[index]);
        if (kind < 0 || _usedBrackets.Contains(index) || !_words.Exists(w => w != _password && !w.Dud))
            return null;
        int rowEnd = (index / Geometry.CellsPerRow + 1) * Geometry.CellsPerRow;
        for (int i = index + 1; i < rowEnd; i++)
        {
            if (_cells[i] == Closers[kind])
                return new HackingHit(index, i - index + 1, HackingHitKind.Bracket);
            if (!Junk.Contains(_cells[i]))
                break;
        }
        return null;
    }

    private string[] Shuffle(string[] source)
    {
        string[] words = (string[])source.Clone();
        for (int i = words.Length - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (words[i], words[j]) = (words[j], words[i]);
        }
        return words;
    }

    private void Place(string[] shuffled, int count, int length)
    {
        int first = (count + 1) / 2;
        int next = 0;
        for (int column = 0; column < Geometry.Columns; column++)
        {
            int inColumn = column == 0 ? first : count - first;
            for (int slot = 0; slot < inColumn; slot++)
            {
                int low = column * Geometry.CellsPerColumn + slot * Geometry.CellsPerColumn / inColumn;
                int high = column * Geometry.CellsPerColumn + (slot + 1) * Geometry.CellsPerColumn / inColumn;
                int start = low + _random.Next(high - low - length + 1);
                string text = shuffled[next++];
                text.CopyTo(0, _cells, start, length);
                _words.Add(new Word(start, text));
            }
        }
    }
}
