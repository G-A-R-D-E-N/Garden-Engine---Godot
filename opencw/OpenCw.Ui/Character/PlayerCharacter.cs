using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Ui.MainMenu;

namespace OpenCommonwealth.Services.Ui.Character;

// Requires: main thread only; a null name means the player never chose one, and readers fall back to PlayClock.DefaultCharacterName.
public sealed class PlayerCharacter
{
    public const string SectionName = "player.character.json";
    private const string NameKey = "name";

    public static PlayerCharacter Current { get; } = new();

    public event Action? Changed;

    public string? Name { get; private set; }

    public string DisplayName => Name ?? PlayClock.DefaultCharacterName;

    public bool IsEmpty => Name is null;

    // Invariant: only a name the new-game rules accept is stored; a refused name leaves the old one.
    public bool SetName(string? raw)
    {
        var check = CharacterNameRules.NewGame.Check(raw);
        if (!check.IsValid) return false;
        Name = check.Name;
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (Name is null) return;
        Name = null;
        Changed?.Invoke();
    }

    public IReadOnlyDictionary<string, byte[]> Sections() =>
        Name is null
            ? new Dictionary<string, byte[]>()
            : new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(new JsonObject { [NameKey] = Name }.ToJsonString()) };

    // Invariant: an absent section is a save from before character creation and loads with no name; a bad one throws FormatException before anything changes.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        string? name = null;
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                var row = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("player character is not an object");
                var raw = row[NameKey]?.GetValue<string>() ?? throw new FormatException("player character has no name");
                var check = CharacterNameRules.NewGame.Check(raw);
                if (!check.IsValid) throw new FormatException("player character name is not valid");
                name = check.Name;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("player character is malformed", ex); }
        }
        Name = name;
        Changed?.Invoke();
    }
}
