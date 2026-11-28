namespace OpenCommonwealth.Services.Audio;

public static class MusicTracks
{
    public static int PlayFirstAvailable(int count, int start, Func<int, bool> play)
    {
        if (count <= 0) return -1;
        start = ((start % count) + count) % count;
        for (int offset = 0; offset < count; offset++)
        {
            int index = (start + offset) % count;
            if (play(index)) return index;
        }
        return -1;
    }

    public static int Advance(int trackCount, int current)
    {
        if (trackCount <= 0) return 0;
        if (trackCount == 1) return 0;
        int next = current;
        while (next == current)
            next = Random.Shared.Next(trackCount);
        return next;
    }
}
