namespace OpenCommonwealth.Services.Ui.Workbench;

// Constraint: values are the FURN WBDT BenchType byte; Weapons, Armor and PowerArmor name an item signature, the rest use the keyword path; read the byte only through BenchRules.EffectiveType.
public static class BenchKinds
{
    public const int Weapons = 2;
    public const int Armor = 7;
    public const int PowerArmor = 8;
    public const int RobotMod = 9;
    public const string RobotNotice = "The robot builder is not available yet.";

    public static string? ItemSignature(int? benchType) => benchType switch
    {
        Weapons => "WEAP",
        Armor or PowerArmor => "ARMO",
        _ => null,
    };
}
