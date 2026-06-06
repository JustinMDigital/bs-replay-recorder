using System.Text.Json;

namespace BSAutoReplayRecorder.ControlPanel;

internal static class LocalSettingsFile
{
    private const string SettingsFileName = "settings.json";
    private const string SettingsPathEnvironmentVariable = "BSARR_SETTINGS_PATH";

    public static ControlPanelSettings LoadOrDefault()
    {
        var path = ResolveSettingsPath();
        if (path == null)
        {
            return new ControlPanelSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<ControlPanelSettings>(json, JsonOptions.Default)
                           ?? new ControlPanelSettings();
            ApplyAliases(settings, json);
            ResolveSettingsRelativePaths(settings, Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory());
            return settings;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not read settings.json, so using control-panel defaults: " + ex.Message);
            return new ControlPanelSettings();
        }
    }

    private static string? ResolveSettingsPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(SettingsPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = Path.GetFullPath(configuredPath.Trim().Trim('"'));
            return File.Exists(fullPath) ? fullPath : null;
        }

        foreach (var root in EnumerateSearchRoots())
        {
            var path = FindSettingsInParents(root);
            if (path != null)
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            if (!string.IsNullOrWhiteSpace(root) && seen.Add(Path.GetFullPath(root)))
            {
                yield return root;
            }
        }
    }

    private static string? FindSettingsInParents(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, SettingsFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static void ApplyAliases(ControlPanelSettings settings, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (TryGetString(root, "controlPanelUrl", out var controlPanelUrl))
        {
            settings.BindUrl = controlPanelUrl;
        }

        if (TryGetString(root, "workspace", out var workspace))
        {
            settings.WorkspaceDirectory = workspace;
        }
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString() ?? "";
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        value = "";
        return false;
    }

    private static void ResolveSettingsRelativePaths(ControlPanelSettings settings, string settingsDirectory)
    {
        settings.WorkspaceDirectory = ResolveSettingsRelativePath(settings.WorkspaceDirectory, settingsDirectory);
        settings.BeatSaberInstancesRoot = ResolveSettingsRelativePath(settings.BeatSaberInstancesRoot, settingsDirectory);
        settings.SharedCustomLevelsDirectory = ResolveSettingsRelativePath(settings.SharedCustomLevelsDirectory, settingsDirectory);
        settings.SharedCustomWipLevelsDirectory = ResolveSettingsRelativePath(settings.SharedCustomWipLevelsDirectory, settingsDirectory);
        settings.SharedCustomSabersDirectory = ResolveSettingsRelativePath(settings.SharedCustomSabersDirectory, settingsDirectory);
        settings.SharedCustomNotesDirectory = ResolveSettingsRelativePath(settings.SharedCustomNotesDirectory, settingsDirectory);
        settings.SharedCustomPlatformsDirectory = ResolveSettingsRelativePath(settings.SharedCustomPlatformsDirectory, settingsDirectory);
        settings.SharedCustomAvatarsDirectory = ResolveSettingsRelativePath(settings.SharedCustomAvatarsDirectory, settingsDirectory);
        settings.SharedCustomWallsDirectory = ResolveSettingsRelativePath(settings.SharedCustomWallsDirectory, settingsDirectory);
        settings.SharedCustomBombsDirectory = ResolveSettingsRelativePath(settings.SharedCustomBombsDirectory, settingsDirectory);
    }

    private static string ResolveSettingsRelativePath(string value, string settingsDirectory)
    {
        var trimmed = value?.Trim().Trim('"') ?? "";
        if (string.IsNullOrWhiteSpace(trimmed) || Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        return Path.GetFullPath(Path.Combine(settingsDirectory, trimmed));
    }
}
