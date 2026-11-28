using OpenCommonwealth.Services;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class RecordVocabularyTableTests
{
    // Constraint: detects a Radicle vocabulary export that drifts from the identifiers the engine wrote before the swap, which would change .tscn metadata text and compile stamps.
    private static readonly Dictionary<string, string> ExpectedClassNames = new()
    {
        ["AACT"] = "ActionRecord", ["ACHR"] = "PlacedNpc", ["ACTI"] = "Activator", ["ADDN"] = "AddonNode",
        ["AECH"] = "AudioEffectChain", ["ALCH"] = "Ingestible", ["AMDL"] = "AimModel", ["AMMO"] = "Ammunition",
        ["ANIO"] = "AnimatedObject", ["AORU"] = "AttractionRule", ["ARMA"] = "ArmorAddon", ["ARMO"] = "Armor",
        ["ARTO"] = "ArtObject", ["ASPC"] = "AcousticSpace", ["ASTP"] = "AssociationType", ["AVIF"] = "ActorValueInformation",
        ["BNDS"] = "BendableSpline", ["BOOK"] = "Book", ["BPTD"] = "BodyPartData", ["CAMS"] = "CameraShot",
        ["CELL"] = "Cell", ["CLAS"] = "Class", ["CLFM"] = "ColorRecord", ["CLMT"] = "Climate", ["CMPO"] = "Component",
        ["COBJ"] = "ConstructibleObject", ["COLL"] = "CollisionLayer", ["CONT"] = "Container", ["CPTH"] = "CameraPath",
        ["CSTY"] = "CombatStyle", ["DEBR"] = "Debris", ["DFOB"] = "DefaultObject", ["DIAL"] = "DialogTopic",
        ["DLBR"] = "DialogBranch", ["DLVW"] = "DialogView", ["DOBJ"] = "DefaultObjectManager", ["DOOR"] = "Door",
        ["DUAL"] = "DualCastData", ["ECZN"] = "EncounterZone", ["EFSH"] = "EffectShader", ["ENCH"] = "ObjectEffect",
        ["EQUP"] = "EquipType", ["EXPL"] = "Explosion", ["FACT"] = "Faction", ["FLOR"] = "Flora", ["FLST"] = "FormList",
        ["FSTP"] = "Footstep", ["FSTS"] = "FootstepSet", ["FURN"] = "Furniture", ["GDRY"] = "GodRays", ["GRAS"] = "Grass",
        ["HAZD"] = "Hazard", ["HDPT"] = "HeadPart", ["IDLE"] = "IdleAnimation", ["IDLM"] = "IdleMarker",
        ["IMAD"] = "ImageSpaceAdapter", ["IMGS"] = "ImageSpace", ["INFO"] = "DialogResponses", ["INGR"] = "Ingredient",
        ["INNR"] = "InstanceNamingRules", ["IPCT"] = "Impact", ["IPDS"] = "ImpactDataSet", ["KEYM"] = "Key",
        ["KSSM"] = "SoundKeywordMapping", ["KYWD"] = "Keyword", ["LAND"] = "Landscape", ["LAYR"] = "Layer",
        ["LCRT"] = "LocationReferenceType", ["LCTN"] = "Location", ["LENS"] = "LensFlare", ["LGTM"] = "LightingTemplate",
        ["LIGH"] = "Light", ["LSCR"] = "LoadScreen", ["LTEX"] = "LandscapeTexture", ["LVLI"] = "LeveledItem",
        ["LVLN"] = "LeveledNpc", ["MATO"] = "MaterialObject", ["MATT"] = "MaterialType", ["MESG"] = "Message",
        ["MGEF"] = "MagicEffect", ["MISC"] = "MiscItem", ["MOVT"] = "MovementType", ["MSTT"] = "MovableStatic",
        ["MSWP"] = "MaterialSwap", ["MUSC"] = "MusicType", ["MUST"] = "MusicTrack", ["NAVI"] = "NavigationMeshInfoMap",
        ["NAVM"] = "NavigationMesh", ["NOCM"] = "NavigationMeshObstacleManager", ["NOTE"] = "Holotape", ["NPC_"] = "Npc",
        ["OTFT"] = "Outfit", ["OVIS"] = "ObjectVisibilityManager", ["PACK"] = "Package", ["PARW"] = "PlacedArrow",
        ["PBAR"] = "PlacedBarrier", ["PBEA"] = "PlacedBeam", ["PCON"] = "PlacedCone", ["PERK"] = "Perk",
        ["PFLA"] = "PlacedFlame", ["PGRE"] = "PlacedTrap", ["PHZD"] = "PlacedHazard", ["PKIN"] = "PackIn",
        ["PMIS"] = "PlacedMissile", ["PROJ"] = "Projectile", ["QUST"] = "Quest", ["RACE"] = "Race",
        ["REFR"] = "PlacedObject", ["REGN"] = "Region", ["RELA"] = "Relationship", ["REVB"] = "ReverbParameters",
        ["RFCT"] = "VisualEffect", ["RFGP"] = "ReferenceGroup", ["SCCO"] = "SceneCollection", ["SCEN"] = "Scene",
        ["SCOL"] = "StaticCollection", ["SCSN"] = "AudioCategorySnapshot", ["SMBN"] = "StoryManagerBranchNode",
        ["SMEN"] = "StoryManagerEventNode", ["SMQN"] = "StoryManagerQuestNode", ["SNCT"] = "SoundCategory",
        ["SNDR"] = "SoundDescriptor", ["SOPM"] = "SoundOutputModel", ["SOUN"] = "SoundMarker", ["SPEL"] = "Spell",
        ["SPGD"] = "ShaderParticleGeometry", ["STAG"] = "AnimationSoundTagSet", ["STAT"] = "Static",
        ["TACT"] = "TalkingActivator", ["TERM"] = "Terminal", ["TREE"] = "Tree", ["TRNS"] = "Transform",
        ["TXST"] = "TextureSet", ["VTYP"] = "VoiceType", ["WATR"] = "Water", ["WEAP"] = "Weapon",
        ["WRLD"] = "Worldspace", ["WTHR"] = "Weather", ["ZOOM"] = "Zoom",
    };

    private static readonly HashSet<string> ExpectedNamed = new()
    {
        "AACT", "ACTI", "AVIF", "AMMO", "OMOD", "ARMO", "BOOK", "CELL", "CLAS", "CLFM", "CMPO", "CONT", "DIAL",
        "DOOR", "EXPL", "FACT", "FLST", "FURN", "HAZD", "HDPT", "NOTE", "ALCH", "INGR", "INNR", "KYWD", "LIGH",
        "LCTN", "MGEF", "MATT", "MESG", "MISC", "MSTT", "MOVT", "NPC_", "ENCH", "PERK", "PROJ", "QUST", "RACE",
        "RFGP", "SNCT", "SPEL", "STAT", "SCOL", "TACT", "TERM", "TREE", "WATR", "WEAP", "WRLD",
    };

    private static readonly string[] ExpectedTrapLike = { "PARW", "PBAR", "PBEA", "PCON", "PFLA", "PHZD", "PMIS", "PGRE" };

    private static readonly string[] ExpectedMarkerTypes =
    {
        "Cave", "City", "DiamondCity", "Encampment", "FactoryOrIndustrialSite", "GovtBuildingOrMonument",
        "MetroStation", "MilitaryBase", "NaturalLandmark", "OfficeOrCivicBuilding", "RuinsTown", "RuinsUrban",
        "Sanctuary", "Settlement", "SewerOrUtilityTunnels", "Vault", "Airfield", "BunkerHill", "Camper", "Car",
        "Church", "CountryClub", "CustomHouse", "DriveIn", "ElevatedHighway", "FaneuilHall", "Farm",
        "FillingStation", "Forested", "Goodneighbor", "Graveyard", "Hospital", "IndustrialDome",
        "IndustrialStacks", "Institute", "IrishPride", "Junkyard", "Observatory", "Pier", "PondOrLake", "Quarry",
        "RadioactiveArea", "RadioTower", "Salem", "School", "Shipwreck", "Submarine", "SwanPond", "SynthHead",
        "Town", "BrotherhoodofSteel", "BrownstoneTownhouse", "Bunker", "Castle", "Skyscraper", "Libertalia",
        "LowRiseBuilding", "Minutemen", "PoliceStation", "Prydwen", "RailroadFaction", "Railroad", "Satellite",
        "Sentinel", "USSConstitution", "MechanistLairRaidersettlementVassalsettlementPotentialVassalsettlement",
    };

    private static IEnumerable<string> EverySignature()
    {
        for (var a = 'A'; a <= 'Z'; a++)
            foreach (var b in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
                foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ_")
                    foreach (var d in "ABCDEFGHIJKLMNOPQRSTUVWXYZ_")
                        yield return $"{a}{b}{c}{d}";
    }

    [Fact]
    public void EveryClassNameRowIsExported()
    {
        Assert.Equal(139, ExpectedClassNames.Count);
        foreach (var (signature, name) in ExpectedClassNames)
            Assert.Equal(name, RecordVocabulary.ClassName(signature));
    }

    [Theory]
    [InlineData("ZZZZ")]
    [InlineData("OMOD")]
    [InlineData("")]
    [InlineData("REF")]
    [InlineData("REFÉ")]
    public void ASignatureWithoutARowIsReturnedUnchanged(string signature)
    {
        Assert.Equal(signature, RecordVocabulary.ClassName(signature));
    }

    // Constraint: detects a signature that gains or loses a class name row; the scan covers every four-letter signature, not only the expected keys.
    [Fact]
    public void NoSignatureOutsideTheExpectedTableHasAClassName()
    {
        foreach (var signature in EverySignature())
            if (!ExpectedClassNames.ContainsKey(signature))
                Assert.Equal(signature, RecordVocabulary.ClassName(signature));
    }

    [Fact]
    public void NamedAndTrapLikeTraitsMatchTheTablesTheEngineHeld()
    {
        Assert.Equal(50, ExpectedNamed.Count);
        Assert.Equal(8, ExpectedTrapLike.Length);
        foreach (var signature in EverySignature())
        {
            Assert.Equal(ExpectedNamed.Contains(signature), RecordVocabulary.IsNamed(signature));
            Assert.Equal(ExpectedTrapLike.Contains(signature), RecordVocabulary.IsTrapLike(signature));
        }
    }

    [Fact]
    public void APlacedSignatureIsAReferenceActorOrTrap()
    {
        foreach (var signature in new[] { "REFR", "ACHR" }.Concat(ExpectedTrapLike))
            Assert.True(RecordVocabulary.IsPlaced(signature), signature);
        Assert.False(RecordVocabulary.IsPlaced("STAT"));
        Assert.False(RecordVocabulary.IsPlaced("ZZZZ"));
        Assert.False(RecordVocabulary.IsPlaced(""));
    }

    [Theory]
    [InlineData(0, "Easy")]
    [InlineData(1, "Medium")]
    [InlineData(2, "Hard")]
    [InlineData(3, "VeryHard")]
    [InlineData(4, "4")]
    [InlineData(-1, "-1")]
    [InlineData(int.MinValue, "-2147483648")]
    [InlineData(int.MaxValue, "2147483647")]
    public void LevelNamesTheFourTiersAndEchoesOtherValues(int value, string expected)
    {
        Assert.Equal(expected, RecordVocabulary.Level(value));
    }

    [Theory]
    [InlineData(0u, "None")]
    [InlineData(1u, "Box")]
    [InlineData(2u, "Sphere")]
    [InlineData(3u, "Plane")]
    [InlineData(4u, "Line")]
    [InlineData(5u, "Ellipsoid")]
    [InlineData(6u, "6")]
    public void PrimitiveTypeNamesTheTableAndEchoesOutOfRange(uint value, string expected)
    {
        Assert.Equal(expected, RecordVocabulary.PrimitiveType(value));
    }

    [Fact]
    public void EveryMarkerTypeRowIsExportedAndTheRestEchoTheNumber()
    {
        Assert.Equal(66, ExpectedMarkerTypes.Length);
        for (var value = 0; value <= byte.MaxValue; value++)
            Assert.Equal(value < ExpectedMarkerTypes.Length ? ExpectedMarkerTypes[value] : value.ToString(), RecordVocabulary.MarkerType((byte)value));
    }

    [Fact]
    public void ScriptPropertyTypesNameTheMappedValuesAndEchoTheRest()
    {
        var expected = new Dictionary<byte, string>
        {
            [1] = "Object", [2] = "String", [3] = "Int", [4] = "Float", [5] = "Bool", [6] = "Variable", [7] = "Struct",
            [11] = "ArrayOfObject", [12] = "ArrayOfString", [13] = "ArrayOfInt", [14] = "ArrayOfFloat",
            [15] = "ArrayOfBool", [16] = "ArrayOfVariable", [17] = "ArrayOfStruct",
        };
        for (var value = 0; value <= byte.MaxValue; value++)
            Assert.Equal(expected.TryGetValue((byte)value, out var name) ? name : value.ToString(), RecordVocabulary.ScriptPropertyType((byte)value));
    }

    [Fact]
    public void ActivateParentFlagsNamesOnlyOne()
    {
        Assert.Equal("ParentActivateOnly", RecordVocabulary.ActivateParentFlags(1));
        for (var value = 0; value <= byte.MaxValue; value++)
            if (value != 1)
                Assert.Equal(value.ToString(), RecordVocabulary.ActivateParentFlags((byte)value));
    }
}
