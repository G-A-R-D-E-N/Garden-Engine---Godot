using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Combat;

public sealed record VitalsSave(VitalsSnapshot? Vitals, IReadOnlyDictionary<LimbKind, LimbCondition> Limbs);

public static class PlayerVitalsSave
{
    public const string SectionName = "player.vitals.json";
    public const int CurrentVersion = 1;

    public static IReadOnlyDictionary<string, byte[]> Sections(PlayerVitals? vitals, PlayerLimbs limbs)
    {
        if (vitals is null && limbs.All.Count == 0) return new Dictionary<string, byte[]>();
        var root = new JsonObject { ["version"] = CurrentVersion };
        if (vitals?.Capture() is { } snapshot)
        {
            root["health"] = snapshot.Health;
            root["actionPoints"] = snapshot.ActionPoints;
            root["rads"] = snapshot.Rads;
            root["heals"] = new JsonArray(snapshot.Heals.Select(heal => (JsonNode)new JsonObject { ["rate"] = heal.Rate, ["left"] = heal.Left, ["target"] = heal.Target.ToString() }).ToArray());
            root["buffs"] = new JsonArray(snapshot.Buffs.Select(buff => (JsonNode)new JsonObject { ["value"] = buff.ActorValue, ["amount"] = buff.Amount, ["left"] = buff.Left }).ToArray());
        }
        root["limbs"] = new JsonArray(limbs.All.OrderBy(pair => pair.Key)
            .Select(pair => (JsonNode)new JsonObject { ["limb"] = pair.Key.ToString(), ["fraction"] = pair.Value.Fraction, ["crippled"] = pair.Value.Crippled }).ToArray());
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(root.ToJsonString()) };
    }

    // Requires: callers treat a FormatException as a refused section; null means the save has no vitals section.
    public static VitalsSave? Read(IReadOnlyDictionary<string, byte[]> sections)
    {
        if (!sections.TryGetValue(SectionName, out var payload)) return null;
        try
        {
            var root = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("player vitals are not an object");
            if (root["version"]?.GetValue<int>() != CurrentVersion) throw new FormatException("player vitals have an unknown version");
            VitalsSnapshot? vitals = null;
            if (root.ContainsKey("health"))
            {
                var heals = (root["heals"] as JsonArray ?? throw new FormatException("player vitals need a heals list")).Select(node =>
                    new PendingHeal(Number(node, "rate"), Number(node, "left"), Enum.TryParse<Vital>(node?["target"]?.GetValue<string>(), out var target) && Enum.IsDefined(target) ? target : throw new FormatException("a heal has an unknown target"))).ToList();
                var buffs = (root["buffs"] as JsonArray ?? throw new FormatException("player vitals need a buffs list")).Select(node =>
                    new ActiveBuff(node?["value"]?.GetValue<string>() ?? throw new FormatException("a buff has no actor value"), Number(node, "amount"), Number(node, "left"))).ToList();
                vitals = new VitalsSnapshot(Number(root, "health"), Number(root, "actionPoints"), Number(root, "rads"), heals, buffs);
            }
            var limbs = new Dictionary<LimbKind, LimbCondition>();
            foreach (var node in root["limbs"] as JsonArray ?? throw new FormatException("player vitals need a limbs list"))
            {
                if (!Enum.TryParse<LimbKind>(node?["limb"]?.GetValue<string>(), out var limb) || !Enum.IsDefined(limb)) throw new FormatException("a limb is unknown");
                limbs[limb] = new LimbCondition(Number(node, "fraction"), node?["crippled"]?.GetValue<bool>() ?? false);
            }
            return new VitalsSave(vitals, limbs);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NullReferenceException)
        {
            throw new FormatException("player vitals are malformed", ex);
        }
    }

    // Invariant: a save with no vitals section puts the store back to full and clears the limbs, because the live values belong to the game being left.
    public static void Apply(VitalsSave? save, PlayerVitals? vitals, PlayerLimbs limbs)
    {
        if (save is null)
        {
            vitals?.ResetToFull();
            limbs.Clear();
            return;
        }
        if (save.Vitals is { } snapshot) vitals?.Restore(snapshot);
        else vitals?.ResetToFull();
        limbs.ReplaceAll(save.Limbs);
    }

    private static double Number(JsonNode? node, string name) =>
        node?[name]?.GetValue<double>() is { } value && double.IsFinite(value) ? value : throw new FormatException($"player vitals need a finite number '{name}'");
}
