using System;

namespace OpenCommonwealth.Services.Ui.Prompts;

public readonly record struct ContextPromptDetails(string Target, string Action, string Binding = "");

public interface IContextPromptSource
{
    ContextPromptDetails? Current { get; }
}

public sealed class InMemoryContextPromptSource : IContextPromptSource
{
    public ContextPromptDetails? Current { get; private set; }

    public void Set(string target, string action, string binding = "") =>
        Current = new ContextPromptDetails(target, action, binding);

    public void Clear() => Current = null;
}

public sealed class ContextPromptModel
{
    public bool Visible { get; private set; }

    public string TargetName { get; private set; } = "";

    public string Action { get; private set; } = "";

    public string Binding { get; private set; } = "";

    public int Version { get; private set; }

    public string ActionLine => string.IsNullOrWhiteSpace(Binding) ? Action : Binding + ": " + Action;

    public void ShowDetails(string? targetName, string? action, string? binding)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            ClearDetails();
            return;
        }
        string target = targetName ?? "";
        string bind = binding ?? "";
        if (Visible && target == TargetName && action == Action && bind == Binding) return;
        Visible = true;
        TargetName = target;
        Action = action;
        Binding = bind;
        Version++;
    }

    public void ClearDetails()
    {
        if (!Visible && TargetName.Length == 0 && Action.Length == 0 && Binding.Length == 0) return;
        Visible = false;
        TargetName = "";
        Action = "";
        Binding = "";
        Version++;
    }

    public void Sync(IContextPromptSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Current is { } details) ShowDetails(details.Target, details.Action, details.Binding);
        else ClearDetails();
    }
}
