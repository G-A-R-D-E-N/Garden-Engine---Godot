using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCommonwealth.Services.Papyrus.Vm;

/// <summary>The scripts one source payload lowered to, as stored in a <c>.gir</c> file.</summary>
public sealed record PapyrusIrFile(int FormatVersion, IReadOnlyList<PapyrusScript> Scripts);

/// <summary>The versioned JSON form of the Garden IR.</summary>
public static class PapyrusIr
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(), new ValueConverter() },
    };

    public static string ToJson(IReadOnlyList<PapyrusScript> scripts) =>
        JsonSerializer.Serialize(new PapyrusIrFile(FormatVersion, scripts), Options);

    public static IReadOnlyList<PapyrusScript> FromJson(string json)
    {
        var file = JsonSerializer.Deserialize<PapyrusIrFile>(json, Options)
                   ?? throw new InvalidOperationException("the IR file was empty");
        if (file.FormatVersion != FormatVersion)
        {
            throw new InvalidOperationException(
                $"the IR file is format {file.FormatVersion}, but this build reads format {FormatVersion}");
        }
        return file.Scripts;
    }

    private sealed class ValueConverter : JsonConverter<PapyrusValue>
    {
        public override PapyrusValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var kind = Enum.Parse<PapyrusValueKind>(root.GetProperty("kind").GetString()!);
            var value = root.TryGetProperty("value", out var element) ? element : default;
            return kind switch
            {
                PapyrusValueKind.None => PapyrusValue.None,
                PapyrusValueKind.Int => PapyrusValue.FromInt(value.GetInt64()),
                PapyrusValueKind.Float => PapyrusValue.FromFloat(value.GetDouble()),
                PapyrusValueKind.Bool => PapyrusValue.FromBool(value.GetBoolean()),
                PapyrusValueKind.String => PapyrusValue.FromString(value.GetString()!),
                _ => throw new JsonException("an IR constant cannot be an object reference"),
            };
        }

        public override void Write(Utf8JsonWriter writer, PapyrusValue value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", value.Kind.ToString());
            switch (value.Kind)
            {
                case PapyrusValueKind.None:
                    break;
                case PapyrusValueKind.Int:
                    writer.WriteNumber("value", value.Integer);
                    break;
                case PapyrusValueKind.Float:
                    writer.WriteNumber("value", value.Real);
                    break;
                case PapyrusValueKind.Bool:
                    writer.WriteBoolean("value", value.Flag);
                    break;
                case PapyrusValueKind.String:
                    writer.WriteString("value", value.Text);
                    break;
                default:
                    throw new JsonException("an IR constant cannot be an object reference");
            }
            writer.WriteEndObject();
        }
    }
}
