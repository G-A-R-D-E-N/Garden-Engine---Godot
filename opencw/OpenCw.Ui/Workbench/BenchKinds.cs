namespace OpenCommonwealth.Services.Ui.Workbench;

// Constraint: values are the FURN WBDT BenchType byte; Weapons, Armor and PowerArmor name an item signature, the rest use the keyword path; read the byte only through BenchRules.EffectiveType.
public static class BenchKinds
{
    public const int Weapons = (int)Radicle.Interop.FurnitureBenchType.Weapons;
    public const int Armor = (int)Radicle.Interop.FurnitureBenchType.Armor;
    public const int PowerArmor = (int)Radicle.Interop.FurnitureBenchType.PowerArmor;
    public const int RobotMod = (int)Radicle.Interop.FurnitureBenchType.RobotMod;
    public const string RobotNotice = "The robot builder is not available yet.";

    public static string? ItemSignature(int? benchType) => benchType switch
    {
        Weapons => "WEAP",
        Armor or PowerArmor => "ARMO",
        _ => null,
    };
}
