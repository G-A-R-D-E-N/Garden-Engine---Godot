using System.Collections.Generic;

namespace OpenCommonwealth.Services.Papyrus.Host;

internal sealed class LatentTokenSet
{
    private readonly List<(long Start, long End)> _runs = new();

    public int RunCount => _runs.Count;

    public bool Add(long token)
    {
        var low = 0;
        var high = _runs.Count;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (_runs[mid].Start <= token) low = mid + 1;
            else high = mid;
        }
        var previous = low - 1;
        if (previous >= 0 && _runs[previous].End >= token) return false;
        var joinsPrevious = previous >= 0 && _runs[previous].End < long.MaxValue && _runs[previous].End + 1 == token;
        var joinsNext = low < _runs.Count && token < long.MaxValue && _runs[low].Start == token + 1;
        if (joinsPrevious && joinsNext)
        {
            _runs[previous] = (_runs[previous].Start, _runs[low].End);
            _runs.RemoveAt(low);
        }
        else if (joinsPrevious) _runs[previous] = (_runs[previous].Start, token);
        else if (joinsNext) _runs[low] = (token, _runs[low].End);
        else _runs.Insert(low, (token, token));
        return true;
    }
}
