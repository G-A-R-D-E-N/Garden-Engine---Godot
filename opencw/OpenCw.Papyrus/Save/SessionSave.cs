using OpenCommonwealth.Services.Papyrus.Vm;
using OpenCommonwealth.Services.Papyrus.Quest;

namespace OpenCommonwealth.Services.Papyrus.Save;

public sealed class SessionSaveException : FormatException
{
    public SessionSaveException(string section, Exception inner) : base($"{section} could not be captured: {inner.Message}", inner) => Section = section;

    public string Section { get; }
}

public static class SessionSave
{
    // Invariant: a capture failure throws SessionSaveException (a FormatException) instead of dropping the section, so a caller never writes a save that lacks Papyrus state it should have.
    public static IReadOnlyDictionary<string, byte[]> Sections(GameSession? session)
    {
        if (session?.Quests.CleanupFailure is { } failure)
            throw new SessionSaveException("quest cleanup", new QuestCleanupException(failure));
        var sections = new Dictionary<string, byte[]>();
        Capture(sections, QuestStateSave.SectionName, () => QuestStateSave.Sections(session?.QuestStages));
        Capture(sections, QuestScriptSave.SectionName, () => QuestScriptSave.Sections(session?.Quests, session?.Host));
        Capture(sections, PapyrusStateSave.SectionName, () => PapyrusStateSave.Sections(session?.Host));
        return sections;
    }

    private static void Capture(Dictionary<string, byte[]> sections, string name, Func<IReadOnlyDictionary<string, byte[]>> capture)
    {
        try
        {
            foreach (var (key, payload) in capture()) sections[key] = payload;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new SessionSaveException(name, error);
        }
    }
}
