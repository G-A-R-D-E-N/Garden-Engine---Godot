namespace OpenCommonwealth.Services.Papyrus.Vm;

/// <summary>What a native call targets.</summary>
public enum NativeTargetKind
{
    /// <summary>No self. The function must be global.</summary>
    Static,

    /// <summary>The attached ScriptObject of the required script.</summary>
    ScriptInstance,

    /// <summary>An engine identity or reference, never an arbitrary attached custom script.</summary>
    EngineObject,
}
