using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Service;

public enum ModKind
{
    Master,
    Plugin,
    Light,
}

public enum ProblemSeverity
{
    Warning,
    Error,
}

public sealed record ModProblem(string FileName, ProblemSeverity Severity, string Message);

public sealed record ModEntry(
    string FileName,
    ModKind Kind,
    bool Enabled,
    string Path,
    IReadOnlyList<string> Masters,
    int RecordCount,
    bool Mandatory = false,
    bool Missing = false,
    string Error = "");

public interface IModLibrary
{
    IReadOnlyList<ModEntry> Load();

    IReadOnlyList<ModProblem> Validate(IReadOnlyList<ModEntry> order);

    ServiceResult Apply(IReadOnlyList<ModEntry> order);
}
