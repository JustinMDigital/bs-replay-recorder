using System.Text.Json;
using BSAutoReplayRecorder.Core;

namespace BSAutoReplayRecorder.ControlPanel;

public sealed class GameColorPreset
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Source { get; set; } = "Saved";

    public string LeftSaberColor { get; set; } = "#a82020";

    public string RightSaberColor { get; set; } = "#2064a8";

    public string LightColorA { get; set; } = "#ff3030";

    public string LightColorB { get; set; } = "#c03030";

    public string BoostLightColorA { get; set; } = "#ff3030";

    public string BoostLightColorB { get; set; } = "#c03030";

    public string WallColor { get; set; } = "#3098ff";

    public bool CanDelete { get; set; }

    public void Normalize()
    {
        Id = Id?.Trim() ?? "";
        Name = string.IsNullOrWhiteSpace(Name) ? "Color preset" : Name.Trim();
        Source = string.IsNullOrWhiteSpace(Source) ? "Saved" : Source.Trim();
        var settings = ToGamePresentationSettings();
        settings.Normalize();
        Apply(settings);
    }

    public GamePresentationSettings ToGamePresentationSettings()
    {
        return new GamePresentationSettings
        {
            LeftSaberColor = LeftSaberColor,
            RightSaberColor = RightSaberColor,
            LightColorA = LightColorA,
            LightColorB = LightColorB,
            BoostLightColorA = BoostLightColorA,
            BoostLightColorB = BoostLightColorB,
            WallColor = WallColor
        };
    }

    public void Apply(GamePresentationSettings settings)
    {
        LeftSaberColor = settings.LeftSaberColor;
        RightSaberColor = settings.RightSaberColor;
        LightColorA = settings.LightColorA;
        LightColorB = settings.LightColorB;
        BoostLightColorA = settings.BoostLightColorA;
        BoostLightColorB = settings.BoostLightColorB;
        WallColor = settings.WallColor;
    }
}

public sealed class GameColorPresetCatalog
{
    public List<GameColorPreset> BuiltIn { get; set; } = new List<GameColorPreset>();

    public List<GameColorPreset> BeatSaber { get; set; } = new List<GameColorPreset>();

    public List<GameColorPreset> Saved { get; set; } = new List<GameColorPreset>();

    public string BeatSaberSourcePath { get; set; } = "";
}

public sealed class SaveGameColorPresetRequest
{
    public string Name { get; set; } = "";

    public GamePresentationSettings Colors { get; set; } = new GamePresentationSettings();
}

internal static class GameColorPresetCatalogReader
{
    private static readonly string PlayerDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData",
        "LocalLow",
        "Hyperbolic Magnetism",
        "Beat Saber",
        "PlayerData.dat");

    public static GameColorPresetCatalog Create(ControlPanelSettings settings)
    {
        return new GameColorPresetCatalog
        {
            BuiltIn = CreateBuiltInPresets(),
            BeatSaber = ReadBeatSaberPresets(),
            Saved = settings.GameColorPresets
                .Select(CloneSavedPreset)
                .ToList(),
            BeatSaberSourcePath = PlayerDataPath
        };
    }

    private static List<GameColorPreset> CreateBuiltInPresets()
    {
        return new List<GameColorPreset>
        {
            CreatePreset("builtin-classic", "Classic red / blue", "Built-in", "#a82020", "#2064a8", "#ff3030", "#c03030", "#ff3030", "#c03030", "#3098ff"),
            CreatePreset("builtin-high-contrast", "High contrast", "Built-in", "#ff3b30", "#00b7ff", "#ff6b4a", "#2fd7ff", "#ffd166", "#72f7ff", "#f5f7fa"),
            CreatePreset("builtin-neon", "Neon magenta / cyan", "Built-in", "#ff2fb3", "#20e7ff", "#ff4fd8", "#38d9ff", "#ffe66d", "#7bffcb", "#8a7dff"),
            CreatePreset("builtin-mono", "Monochrome focus", "Built-in", "#d9dde5", "#3f4652", "#f0f4ff", "#7f8898", "#ffffff", "#aeb6c3", "#5f7cff")
        };
    }

    private static List<GameColorPreset> ReadBeatSaberPresets()
    {
        var presets = new List<GameColorPreset>();
        if (!File.Exists(PlayerDataPath))
        {
            return presets;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(PlayerDataPath));
            if (!document.RootElement.TryGetProperty("localPlayers", out var localPlayers) ||
                localPlayers.ValueKind != JsonValueKind.Array ||
                localPlayers.GetArrayLength() == 0)
            {
                return presets;
            }

            var player = localPlayers[0];
            var selectedId = "";
            if (player.TryGetProperty("colorSchemesSettings", out var settings))
            {
                selectedId = ReadString(settings, "selectedColorSchemeId");
                if (settings.TryGetProperty("colorSchemes", out var schemes) &&
                    schemes.ValueKind == JsonValueKind.Array)
                {
                    var slot = 1;
                    foreach (var scheme in schemes.EnumerateArray())
                    {
                        var id = ReadString(scheme, "colorSchemeId");
                        var name = "Beat Saber User " + slot;
                        if (!string.IsNullOrWhiteSpace(id) &&
                            string.Equals(id, selectedId, StringComparison.OrdinalIgnoreCase))
                        {
                            name += " (selected)";
                        }

                        presets.Add(CreatePreset(
                            "beatsaber-" + (string.IsNullOrWhiteSpace(id) ? slot.ToString() : id),
                            name,
                            "Beat Saber",
                            ReadColor(scheme, "saberAColor", "#a82020"),
                            ReadColor(scheme, "saberBColor", "#2064a8"),
                            ReadColor(scheme, "environmentColor0", "#ff3030"),
                            ReadColor(scheme, "environmentColor1", "#c03030"),
                            ReadColor(scheme, "environmentColor0Boost", "#ff3030"),
                            ReadColor(scheme, "environmentColor1Boost", "#c03030"),
                            ReadColor(scheme, "obstaclesColor", "#3098ff")));
                        slot++;
                    }
                }
            }
        }
        catch
        {
            return presets;
        }

        return presets;
    }

    private static GameColorPreset CloneSavedPreset(GameColorPreset preset)
    {
        var clone = CreatePreset(
            preset.Id,
            preset.Name,
            "Saved",
            preset.LeftSaberColor,
            preset.RightSaberColor,
            preset.LightColorA,
            preset.LightColorB,
            preset.BoostLightColorA,
            preset.BoostLightColorB,
            preset.WallColor);
        clone.CanDelete = true;
        return clone;
    }

    internal static GameColorPreset CreateSavedPreset(string name, GamePresentationSettings colors)
    {
        colors.Normalize();
        var preset = CreatePreset(
            "saved-" + Guid.NewGuid().ToString("N"),
            name,
            "Saved",
            colors.LeftSaberColor,
            colors.RightSaberColor,
            colors.LightColorA,
            colors.LightColorB,
            colors.BoostLightColorA,
            colors.BoostLightColorB,
            colors.WallColor);
        preset.CanDelete = true;
        preset.Normalize();
        return preset;
    }

    private static GameColorPreset CreatePreset(
        string id,
        string name,
        string source,
        string leftSaberColor,
        string rightSaberColor,
        string lightColorA,
        string lightColorB,
        string boostLightColorA,
        string boostLightColorB,
        string wallColor)
    {
        var preset = new GameColorPreset
        {
            Id = id,
            Name = name,
            Source = source,
            LeftSaberColor = leftSaberColor,
            RightSaberColor = rightSaberColor,
            LightColorA = lightColorA,
            LightColorB = lightColorB,
            BoostLightColorA = boostLightColorA,
            BoostLightColorB = boostLightColorB,
            WallColor = wallColor
        };
        preset.Normalize();
        return preset;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
    }

    private static string ReadColor(JsonElement scheme, string propertyName, string fallback)
    {
        if (!scheme.TryGetProperty(propertyName, out var color) ||
            color.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        var red = ReadColorComponent(color, "r");
        var green = ReadColorComponent(color, "g");
        var blue = ReadColorComponent(color, "b");
        return "#" + ToHex(red) + ToHex(green) + ToHex(blue);
    }

    private static double ReadColorComponent(JsonElement color, string propertyName)
    {
        return color.TryGetProperty(propertyName, out var component) &&
               component.TryGetDouble(out var value)
            ? Math.Clamp(value, 0, 1)
            : 0;
    }

    private static string ToHex(double value)
    {
        return ((int)Math.Round(value * 255)).ToString("x2");
    }
}
