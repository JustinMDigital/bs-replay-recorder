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
    public static void Apply(GamePresentationSettings settings, IPA.Logging.Logger logger)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        settings.Normalize();

        var changed = ApplyBeatLeaderSettings(settings);
        changed |= ApplyPlayerSpecificSettings(settings);
        ApplyRuntimeAudioSettings(settings);

        if (changed)
        {
            logger.Info("Applied game settings from the control panel.");
        }
    }

    private static bool ApplyBeatLeaderSettings(GamePresentationSettings settings)
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

        return changed;
    }

    private static bool ApplyPlayerSpecificSettings(GamePresentationSettings settings)
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

        if (current.AreValuesEqual(next))
        {
            return false;
        }

        playerData.SetPlayerSpecificSettings(next);
        SavePlayerData(playerDataModel);
        return true;
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
