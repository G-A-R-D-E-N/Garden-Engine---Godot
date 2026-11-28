using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.DevConsole;

public enum ConsoleSeverity
{
    Info,
    Warning,
    Error,
    Echo,
}

public sealed record ConsoleLine(ConsoleSeverity Severity, string Text);

public sealed record ConsoleTarget(string Id, string Name);

public sealed record ConsoleResult(IReadOnlyList<ConsoleLine> Lines, ConsoleTarget? Target = null)
{
    public static ConsoleResult Info(string text, ConsoleTarget? target = null) =>
        new(new[] { new ConsoleLine(ConsoleSeverity.Info, text) }, target);

    public static ConsoleResult Error(string text) =>
        new(new[] { new ConsoleLine(ConsoleSeverity.Error, text) });
}

public interface IConsoleHost
{
    ConsoleResult Execute(string line, ConsoleTarget? target);

    IReadOnlyList<string> Complete(string prefix);

    ConsoleTarget? Pick(double x, double y);
}
