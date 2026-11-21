using OpenCommonwealth.Services.Papyrus.Owners;

namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed class ScriptInstance
{
    private readonly string _hostScriptName;
    private readonly Func<string, bool> _hostDefinesFunction;
    private readonly Func<string, int?> _hostDeclaredParameterCount;

    internal ScriptInstance(string scriptName, Func<string, bool> definesFunction,
        Func<string, int?> declaredParameterCount, ulong hostId, IReadOnlyDictionary<string, PapyrusValue> properties,
        PapyrusOwnerId? owner)
    {
        ValidateOwner(owner);
        _hostScriptName = scriptName;
        _hostDefinesFunction = definesFunction;
        _hostDeclaredParameterCount = declaredParameterCount;
        HostId = hostId;
        Properties = properties;
        Owner = owner;
    }

    public string ScriptName => _hostScriptName;

    public bool DefinesFunction(string name) => _hostDefinesFunction(name);

    internal ulong HostId { get; }

    internal int? DeclaredParameterCount(string name) => _hostDeclaredParameterCount(name);

    public IReadOnlyDictionary<string, PapyrusValue> Properties { get; }

    public PapyrusOwnerId? Owner { get; }

    public ScriptLifecycle Lifecycle { get; internal set; } = ScriptLifecycle.NotInitialized;

    public string? Failure { get; internal set; }

    internal Queue<(string Function, PapyrusValue[] Arguments)> Queued { get; } = new();

    internal bool Detached { get; set; }

    private static void ValidateOwner(PapyrusOwnerId? owner)
    {
        if (owner is { } identity && string.IsNullOrWhiteSpace(identity.BaseFormKey))
            throw new ArgumentException("a script owner needs a base form identity", nameof(owner));
    }
}
