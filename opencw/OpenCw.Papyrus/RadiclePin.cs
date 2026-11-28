namespace OpenCommonwealth.Services.Papyrus;

/// <summary>
/// The exact Radicle revision the Papyrus runtime is built against.
/// </summary>
// Invariant: the pin names the revision and C ABI that produced the script cache metadata; Radicle.Interop is referenced through RadicleSourceDir.
public static class RadiclePin
{
    public const string Repository = "G-A-R-D-E-N/GardenTools";
    public const string Branch = "main";
    public const string Commit = "089c6f6c69192baba887df8708ebd4a39bd35b53";
    public const int AbiVersion = 700633;

    public static string Describe() => $"{Repository}@{Commit[..12]} ABI {AbiVersion}";
}
