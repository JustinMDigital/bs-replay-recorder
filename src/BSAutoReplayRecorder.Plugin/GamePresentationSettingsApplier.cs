using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BeatLeader.Models;
using BSAutoReplayRecorder.Core;
using IPA.Logging;
using UnityEngine;

namespace BSAutoReplayRecorder.Plugin;

internal static class GamePresentationSettingsApplier
{
    private static readonly IGamePresentationSettingsSectionApplier[] SectionAppliers =
    {
        new BeatLeaderReplayerSettingsApplier(),
        new BeatSaberPlayerSpecificSettingsApplier()
    };

    public static void Apply(GamePresentationSettings settings, IPA.Logging.Logger logger)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        settings.Normalize();

        var changedSections = new List<string>();
        foreach (var applier in SectionAppliers)
        {
            var result = applier.Apply(settings);
            if (result.Changed)
            {
                changedSections.Add(applier.Name);
            }
        }

        ApplyRuntimeAudioSettings(settings);

        if (changedSections.Count > 0)
        {
            logger.Info("Applied game settings from the control panel: " + string.Join(", ", changedSections) + ".");
        }
    }

    private interface IGamePresentationSettingsSectionApplier
    {
        string Name { get; }

        GamePresentationSettingsApplyResult Apply(GamePresentationSettings settings);
    }

    private readonly struct GamePresentationSettingsApplyResult
    {
        public GamePresentationSettingsApplyResult(bool changed)
        {
            Changed = changed;
        }

        public bool Changed { get; }
    }

    private sealed class BeatLeaderReplayerSettingsApplier : IGamePresentationSettingsSectionApplier
    {
        public string Name => "BeatLeader replay settings";

        public GamePresentationSettingsApplyResult Apply(GamePresentationSettings settings)
        {
            var replayerSettings = ReplayerSettings.UserSettings
                                   ?? GetPluginConfigReplayerSettings()
                                   ?? ReplayerSettings.DefaultSettings
                                   ?? new ReplayerSettings();
            var changed = false;

            if (replayerSettings.AutoHideUI != settings.NoHud)
            {
                replayerSettings.AutoHideUI = settings.NoHud;
                changed = true;
            }

            changed |= SetBoolean(replayerSettings.LoadPlayerEnvironment, settings.LoadPlayerEnvironment, value => replayerSettings.LoadPlayerEnvironment = value);
            changed |= SetBoolean(replayerSettings.LoadPlayerJumpDistance, settings.LoadPlayerJumpDistance, value => replayerSettings.LoadPlayerJumpDistance = value);
            changed |= SetBoolean(replayerSettings.IgnoreModifiers, settings.IgnoreModifiers, value => replayerSettings.IgnoreModifiers = value);
            changed |= SetBoolean(replayerSettings.ShowHead, settings.ShowHead, value => replayerSettings.ShowHead = value);
            changed |= SetBoolean(replayerSettings.ShowLeftSaber, settings.ShowLeftSaber, value => replayerSettings.ShowLeftSaber = value);
            changed |= SetBoolean(replayerSettings.ShowRightSaber, settings.ShowRightSaber, value => replayerSettings.ShowRightSaber = value);
            changed |= SetBoolean(replayerSettings.ShowWatermark, settings.ShowWatermark, value => replayerSettings.ShowWatermark = value);
            changed |= SetBoolean(replayerSettings.ShowTimelineMisses, settings.ShowTimelineMisses, value => replayerSettings.ShowTimelineMisses = value);
            changed |= SetBoolean(replayerSettings.ShowTimelineBombs, settings.ShowTimelineBombs, value => replayerSettings.ShowTimelineBombs = value);
            changed |= SetBoolean(replayerSettings.ShowTimelinePauses, settings.ShowTimelinePauses, value => replayerSettings.ShowTimelinePauses = value);

            if (!ReferenceEquals(GetPluginConfigReplayerSettings(), replayerSettings))
            {
                SetPluginConfigReplayerSettings(replayerSettings);
                changed = true;
            }

            if (changed)
            {
                NotifyPluginConfigReplayerSettingsChanged();
            }

            return new GamePresentationSettingsApplyResult(changed);
        }
    }

    private sealed class BeatSaberPlayerSpecificSettingsApplier : IGamePresentationSettingsSectionApplier
    {
        public string Name => "Beat Saber player settings";

        public GamePresentationSettingsApplyResult Apply(GamePresentationSettings settings)
        {
            var playerDataModel = ResolvePlayerDataModel()
                                  ?? throw new InvalidOperationException("Beat Saber player data model is not available yet.");
            var playerData = playerDataModel.playerData
                             ?? throw new InvalidOperationException("Beat Saber player data is not loaded yet.");
            var current = playerData.playerSpecificSettings
                          ?? throw new InvalidOperationException("Beat Saber player-specific settings are not loaded yet.");

            var next = current.CopyWith(
                null,
                null,
                null,
                settings.SfxVolume,
                settings.ReduceDebris,
                settings.NoTextsAndHuds,
                settings.NoFailEffects,
                settings.AdvancedHud,
                null,
                settings.SaberTrailIntensity,
                ParseEnum(settings.NoteJumpDurationType, NoteJumpDurationTypeSettings.Dynamic),
                settings.NoteJumpFixedDuration,
                settings.NoteJumpStartBeatOffset,
                settings.HideNoteSpawnEffect,
                settings.AdaptiveSfx,
                settings.ArcsHapticFeedback,
                ParseEnum(settings.ArcVisibility, ArcVisibilityType.Low),
                ParseEnum(settings.EnvironmentEffectsFilterDefaultPreset, EnvironmentEffectsFilterPreset.AllEffects),
                ParseEnum(settings.EnvironmentEffectsFilterExpertPlusPreset, EnvironmentEffectsFilterPreset.AllEffects),
                settings.HeadsetHapticIntensity);

            var changed = false;
            if (!current.AreValuesEqual(next))
            {
                playerData.SetPlayerSpecificSettings(next);
                changed = true;
            }

            changed |= ApplyColorScheme(playerData, settings);
            if (changed)
            {
                SavePlayerData(playerDataModel);
            }

            return new GamePresentationSettingsApplyResult(changed);
        }

        private static bool ApplyColorScheme(PlayerData playerData, GamePresentationSettings settings)
        {
            var colorSchemesSettings = playerData.colorSchemesSettings
                                       ?? throw new InvalidOperationException("Beat Saber color settings are not loaded yet.");
            var selectedColorScheme = colorSchemesSettings.GetSelectedColorScheme()
                                      ?? colorSchemesSettings.GetColorSchemeForId("User0")
                                      ?? throw new InvalidOperationException("Beat Saber user color scheme is not available yet.");
            var colorSchemeId = GetStringProperty(selectedColorScheme, "colorSchemeId");
            colorSchemeId = string.IsNullOrWhiteSpace(colorSchemeId)
                ? "User0"
                : colorSchemeId;

            var nextColorScheme = CreateColorScheme(
                selectedColorScheme.GetType(),
                colorSchemeId,
                settings);

            var changed =
                !colorSchemesSettings.overrideDefaultColors ||
                !string.Equals(GetEnumPropertyName(colorSchemesSettings, "colorOverrideType"), "All", StringComparison.Ordinal) ||
                !string.Equals(colorSchemesSettings.selectedColorSchemeId, colorSchemeId, StringComparison.Ordinal) ||
                !ColorSchemesEqual(selectedColorScheme, nextColorScheme);

            if (!changed)
            {
                return false;
            }

            colorSchemesSettings
                .GetType()
                .GetMethod("SetColorSchemeForId", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(colorSchemesSettings, new[] { nextColorScheme });
            colorSchemesSettings.selectedColorSchemeId = colorSchemeId;
            colorSchemesSettings.overrideDefaultColors = true;
            SetEnumPropertyByName(colorSchemesSettings, "colorOverrideType", "All");
            return true;
        }
    }

    private static void ApplyRuntimeAudioSettings(GamePresentationSettings settings)
    {
        var audioManagerType = FindType("AudioManagerSO");
        if (audioManagerType == null)
        {
            return;
        }

        foreach (var audioManager in Resources.FindObjectsOfTypeAll(audioManagerType))
        {
            audioManagerType
                .GetProperty("sfxVolume", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(audioManager, settings.SfxVolume, null);
            audioManagerType
                .GetProperty("sfxEnabled", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(audioManager, true, null);
        }
    }

    private static IPlayerDataModel? ResolvePlayerDataModel()
    {
        foreach (var container in ResolveZenjectContainers())
        {
            var resolved = TryResolve(container, typeof(IPlayerDataModel)) ??
                           TryResolve(container, typeof(PlayerDataModel));
            if (resolved is IPlayerDataModel playerDataModel)
            {
                return playerDataModel;
            }
        }

        return null;
    }

    private static IEnumerable<object> ResolveZenjectContainers()
    {
        var projectContextType = FindType("Zenject.ProjectContext");
        if (projectContextType != null &&
            projectContextType.GetProperty("HasInstance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is bool hasProjectContext &&
            hasProjectContext &&
            projectContextType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is object projectContext)
        {
            var container = projectContextType
                .GetProperty("Container", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(projectContext, null);
            if (container != null)
            {
                yield return container;
            }
        }

        var zenUtilType = FindType("Zenject.Internal.ZenUtilInternal");
        var sceneContexts = zenUtilType
            ?.GetMethod("GetAllSceneContexts", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, null) as IEnumerable;
        if (sceneContexts == null)
        {
            yield break;
        }

        foreach (var sceneContext in sceneContexts)
        {
            if (sceneContext == null)
            {
                continue;
            }

            var container = sceneContext.GetType()
                .GetProperty("Container", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(sceneContext, null);
            if (container != null)
            {
                yield return container;
            }
        }
    }

    private static object? TryResolve(object container, Type contractType)
    {
        try
        {
            return container.GetType()
                .GetMethod("TryResolve", new[] { typeof(Type) })
                ?.Invoke(container, new object[] { contractType });
        }
        catch
        {
            return null;
        }
    }

    private static void SavePlayerData(IPlayerDataModel playerDataModel)
    {
        if (playerDataModel is PlayerDataModel concretePlayerDataModel)
        {
            concretePlayerDataModel.Save();
            return;
        }

        playerDataModel.GetType()
            .GetMethod("Save", BindingFlags.Public | BindingFlags.Instance)
            ?.Invoke(playerDataModel, null);
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct
    {
        if (Enum.TryParse(value, true, out TEnum result))
        {
            return result;
        }

        return fallback;
    }

    private static Color ParseColor(string? value)
    {
        var trimmed = value?.Trim().TrimStart('#') ?? "";
        if (trimmed.Length != 6 ||
            !byte.TryParse(trimmed.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var red) ||
            !byte.TryParse(trimmed.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var green) ||
            !byte.TryParse(trimmed.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var blue))
        {
            return Color.white;
        }

        return new Color(red / 255f, green / 255f, blue / 255f, 1f);
    }

    private static object CreateColorScheme(Type colorSchemeType, string colorSchemeId, GamePresentationSettings settings)
    {
        return colorSchemeType
            .GetConstructor(new[]
            {
                typeof(string),
                typeof(bool),
                typeof(Color),
                typeof(Color),
                typeof(Color),
                typeof(bool),
                typeof(Color),
                typeof(Color),
                typeof(Color),
                typeof(Color)
            })
            ?.Invoke(new object[]
            {
                colorSchemeId,
                true,
                ParseColor(settings.LeftSaberColor),
                ParseColor(settings.RightSaberColor),
                ParseColor(settings.WallColor),
                true,
                ParseColor(settings.LightColorA),
                ParseColor(settings.LightColorB),
                ParseColor(settings.BoostLightColorA),
                ParseColor(settings.BoostLightColorB)
            })
            ?? throw new InvalidOperationException("Beat Saber color scheme constructor is not available.");
    }

    private static bool ColorSchemesEqual(object left, object right)
    {
        return ColorsEqual(GetColorProperty(left, "saberAColor"), GetColorProperty(right, "saberAColor")) &&
               ColorsEqual(GetColorProperty(left, "saberBColor"), GetColorProperty(right, "saberBColor")) &&
               ColorsEqual(GetColorProperty(left, "obstaclesColor"), GetColorProperty(right, "obstaclesColor")) &&
               ColorsEqual(GetColorProperty(left, "environmentColor0"), GetColorProperty(right, "environmentColor0")) &&
               ColorsEqual(GetColorProperty(left, "environmentColor1"), GetColorProperty(right, "environmentColor1")) &&
               ColorsEqual(GetColorProperty(left, "environmentColor0Boost"), GetColorProperty(right, "environmentColor0Boost")) &&
               ColorsEqual(GetColorProperty(left, "environmentColor1Boost"), GetColorProperty(right, "environmentColor1Boost"));
    }

    private static bool ColorsEqual(Color left, Color right)
    {
        const float tolerance = 0.0001f;
        return Math.Abs(left.r - right.r) < tolerance &&
               Math.Abs(left.g - right.g) < tolerance &&
               Math.Abs(left.b - right.b) < tolerance &&
               Math.Abs(left.a - right.a) < tolerance;
    }

    private static string GetStringProperty(object target, string propertyName)
    {
        return target.GetType()
                   .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                   ?.GetValue(target, null) as string ??
               "";
    }

    private static Color GetColorProperty(object target, string propertyName)
    {
        var value = target.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(target, null);
        return value is Color color ? color : Color.clear;
    }

    private static string GetEnumPropertyName(object target, string propertyName)
    {
        return target.GetType()
                   .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                   ?.GetValue(target, null)
                   ?.ToString() ??
               "";
    }

    private static void SetEnumPropertyByName(object target, string propertyName, string valueName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                       ?? throw new InvalidOperationException("Beat Saber color override property is not available.");
        var value = Enum.Parse(property.PropertyType, valueName, ignoreCase: true);
        property.SetValue(target, value, null);
    }

    private static Type? FindType(string fullName)
    {
        var type = Type.GetType(fullName, throwOnError: false);
        if (type != null)
        {
            return type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(fullName, throwOnError: false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private static bool SetBoolean(bool currentValue, bool nextValue, Action<bool> setValue)
    {
        if (currentValue == nextValue)
        {
            return false;
        }

        setValue(nextValue);
        return true;
    }

    private static ReplayerSettings? GetPluginConfigReplayerSettings()
    {
        return GetPluginConfigReplayerSettingsProperty()?.GetValue(null) as ReplayerSettings;
    }

    private static void SetPluginConfigReplayerSettings(ReplayerSettings settings)
    {
        GetPluginConfigReplayerSettingsProperty()?.SetValue(null, settings);
    }

    private static void NotifyPluginConfigReplayerSettingsChanged()
    {
        GetPluginConfigType()
            ?.GetMethod(
                "NotifyReplayerSettingsChanged",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }

    private static PropertyInfo? GetPluginConfigReplayerSettingsProperty()
    {
        return GetPluginConfigType()?.GetProperty(
            "ReplayerSettings",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    }

    private static Type? GetPluginConfigType()
    {
        return typeof(ReplayerSettings).Assembly.GetType("BeatLeader.PluginConfig", throwOnError: false);
    }
}
