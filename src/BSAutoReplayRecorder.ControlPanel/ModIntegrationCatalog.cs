namespace BSAutoReplayRecorder.ControlPanel;

internal static class ModIntegrationCatalog
{
    public static IReadOnlyList<SharedFolderDefinition> CreateSharedFolderDefinitions(ControlPanelSettings settings)
    {
        var definitions = new List<SharedFolderDefinition>
        {
            new SharedFolderDefinition(
                "CustomLevels",
                Path.Combine("Beat Saber_Data", "CustomLevels"),
                settings.SharedCustomLevelsDirectory),
            new SharedFolderDefinition(
                "CustomWIPLevels",
                Path.Combine("Beat Saber_Data", "CustomWIPLevels"),
                settings.SharedCustomWipLevelsDirectory)
        };

        AddOptionalSharedFolder(definitions, settings.ShareCustomSabers, "CustomSabers", "CustomSabers", settings.SharedCustomSabersDirectory);
        AddOptionalSharedFolder(definitions, settings.ShareCustomNotes, "CustomNotes", "CustomNotes", settings.SharedCustomNotesDirectory);
        AddOptionalSharedFolder(definitions, settings.ShareCustomPlatforms, "CustomPlatforms", "CustomPlatforms", settings.SharedCustomPlatformsDirectory);
        AddOptionalSharedFolder(definitions, settings.ShareCustomAvatars, "CustomAvatars", "CustomAvatars", settings.SharedCustomAvatarsDirectory);
        AddOptionalSharedFolder(definitions, settings.ShareCustomWalls, "CustomWalls", "CustomWalls", settings.SharedCustomWallsDirectory);
        AddOptionalSharedFolder(definitions, settings.ShareCustomBombs, "CustomBombs", "CustomBombs", settings.SharedCustomBombsDirectory);
        return definitions;
    }

    public static IReadOnlyList<ModSettingsAdapterDefinition> SettingsAdapters { get; } =
        new List<ModSettingsAdapterDefinition>
        {
            new ModSettingsAdapterDefinition(
                "Chroma",
                "Reserved adapter slot for worker-local Chroma settings that must be normalized per managed instance."),
            new ModSettingsAdapterDefinition(
                "Custom Sabers Picker",
                "Reserved adapter slot for selected-saber settings once the picker file format is confirmed.")
        };

    private static void AddOptionalSharedFolder(
        List<SharedFolderDefinition> definitions,
        bool enabled,
        string displayName,
        string instanceRelativePath,
        string sharedFolderPath)
    {
        if (!enabled)
        {
            return;
        }

        definitions.Add(new SharedFolderDefinition(displayName, instanceRelativePath, sharedFolderPath));
    }
}

internal sealed class ModSettingsAdapterDefinition
{
    public ModSettingsAdapterDefinition(string displayName, string description)
    {
        DisplayName = displayName;
        Description = description;
    }

    public string DisplayName { get; }

    public string Description { get; }
}
