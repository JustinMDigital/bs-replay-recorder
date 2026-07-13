using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BeatLeader.Models;
using BSAutoReplayRecorder.Core;
using IPA.Logging;
using Newtonsoft.Json;
using UnityEngine;

namespace BSAutoReplayRecorder.Plugin;

internal static class GamePresentationSettingsApplier
{
    private static readonly IGamePresentationSettingsSectionApplier[] SectionAppliers =
    {
        new BeatLeaderReplayerSettingsApplier(),
        new JdFixerSettingsApplier(),
        new ScoreSaberReplaySettingsApplier(),
        new BeatSaberPlayerSpecificSettingsApplier()
    };

    public static void Apply(GamePresentationSettings settings, IPA.Logging.Logger logger)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        settings.Normalize();
        if (settings.RestorePlayerSettingsOnExit)
        {
            PlayerProfileRestore.CaptureIfNeeded(logger);
        }
        else
        {
            PlayerProfileRestore.ClearIfExists(logger);
        }

        BeatLeaderReplayUiSuppressor.Install(logger);

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

    public static void RestorePlayerSettingsIfPending(IPA.Logging.Logger logger)
    {
        PlayerProfileRestore.RestoreIfPending(logger);
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

            changed |= SetBooleanMemberIfAvailable(replayerSettings, "AutoHideUI", settings.NoHud);
            changed |= ApplyBeatLeaderReplayUiSuppression(replayerSettings);
            changed |= SetBooleanMemberIfAvailable(replayerSettings, "LoadPlayerEnvironment", settings.LoadPlayerEnvironment);
            changed |= SetBooleanMemberIfAvailable(replayerSettings, "LoadPlayerJumpDistance", settings.LoadPlayerJumpDistance);
            changed |= SetBooleanMemberIfAvailable(replayerSettings, "IgnoreModifiers", settings.IgnoreModifiers);
            changed |= SetBooleanMemberIfAvailable(replayerSettings, "ShowWatermark", settings.ShowWatermark);
            changed |= ApplyBeatLeaderBodySettings(replayerSettings, settings);
            changed |= ApplyBeatLeaderTimelineMarkers(replayerSettings, settings);

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

        private static bool ApplyBeatLeaderReplayUiSuppression(ReplayerSettings replayerSettings)
        {
            var uiSettings = GetOrCreateMemberObject(
                replayerSettings,
                "UISettings",
                "BeatLeader.Models.ReplayerUISettings",
                out var created);
            var changed = created;
            if (uiSettings == null)
            {
                return changed;
            }

            changed |= SetBooleanMemberIfAvailable(uiSettings, "QuickSettingsEnabled", false);
            changed |= SetBooleanMemberIfAvailable(uiSettings, "ShowUIOnPause", false);
            return changed;
        }

        private static bool ApplyBeatLeaderBodySettings(ReplayerSettings replayerSettings, GamePresentationSettings settings)
        {
            var bodySettings = GetOrCreateMemberObject(
                replayerSettings,
                "BodySettings",
                "BeatLeader.Models.BodySettings",
                out var changed);
            if (bodySettings == null)
            {
                changed |= SetBooleanMemberIfAvailable(replayerSettings, "ShowHead", settings.ShowHead);
                changed |= SetBooleanMemberIfAvailable(replayerSettings, "ShowLeftSaber", settings.ShowLeftSaber);
                changed |= SetBooleanMemberIfAvailable(replayerSettings, "ShowRightSaber", settings.ShowRightSaber);
                return changed;
            }

            var basicSettings = GetOrCreateBodySettingsConfig(
                bodySettings,
                "BeatLeader.Models.BasicBodySettings",
                out var configChanged);
            changed |= configChanged;
            if (basicSettings == null)
            {
                return changed;
            }

            var bodyChanged = false;
            bodyChanged |= SetBooleanMemberIfAvailable(basicSettings, "HeadEnabled", settings.ShowHead);
            bodyChanged |= SetBooleanMemberIfAvailable(basicSettings, "LeftHandEnabled", false);
            bodyChanged |= SetBooleanMemberIfAvailable(basicSettings, "RightHandEnabled", false);
            bodyChanged |= SetBooleanMemberIfAvailable(basicSettings, "LeftSaberEnabled", settings.ShowLeftSaber);
            bodyChanged |= SetBooleanMemberIfAvailable(basicSettings, "RightSaberEnabled", settings.ShowRightSaber);
            if (bodyChanged)
            {
                NotifyBodySettingsConfigUpdated(bodySettings, basicSettings);
            }

            return changed || bodyChanged;
        }

        private static bool ApplyBeatLeaderTimelineMarkers(ReplayerSettings replayerSettings, GamePresentationSettings settings)
        {
            var directChanged = false;
            directChanged |= SetBooleanMemberIfAvailable(replayerSettings, "ShowTimelineMisses", settings.ShowTimelineMisses);
            directChanged |= SetBooleanMemberIfAvailable(replayerSettings, "ShowTimelineBombs", settings.ShowTimelineBombs);
            directChanged |= SetBooleanMemberIfAvailable(replayerSettings, "ShowTimelinePauses", settings.ShowTimelinePauses);

            var uiSettings = GetOrCreateMemberObject(
                replayerSettings,
                "UISettings",
                "BeatLeader.Models.ReplayerUISettings",
                out var created);
            if (uiSettings == null)
            {
                return directChanged || created;
            }

            return directChanged ||
                   created ||
                   SetEnumFlagsMemberIfAvailable(
                       uiSettings,
                       "MarkersMask",
                       new Dictionary<string, bool>
                       {
                           { "Miss", settings.ShowTimelineMisses },
                           { "Bomb", settings.ShowTimelineBombs },
                           { "Pause", settings.ShowTimelinePauses }
                       });
        }
    }

    private sealed class JdFixerSettingsApplier : IGamePresentationSettingsSectionApplier
    {
        public string Name => "JDFixer settings";

        public GamePresentationSettingsApplyResult Apply(GamePresentationSettings settings)
        {
            if (!settings.ApplyJdFixerSettings)
            {
                return new GamePresentationSettingsApplyResult(false);
            }

            var config = ResolveJdFixerPluginConfig();
            if (config == null)
            {
                return new GamePresentationSettingsApplyResult(false);
            }

            var useReactionTime = string.Equals(
                settings.JdFixerMode,
                GamePresentationSettings.JdFixerModeReactionTime,
                StringComparison.OrdinalIgnoreCase);
            var changed = false;

            changed |= SetBooleanMemberIfAvailable(config, "enabled", true);
            changed |= SetInt32MemberIfAvailable(config, "slider_setting", useReactionTime ? 1 : 0);
            changed |= SetInt32MemberIfAvailable(config, "pref_selected", 0);
            changed |= SetBooleanMemberIfAvailable(config, "use_jd_pref", false);
            changed |= SetBooleanMemberIfAvailable(config, "use_rt_pref", false);
            changed |= useReactionTime
                ? SetSingleMemberIfAvailable(config, "reactionTime", settings.JdFixerReactionTime)
                : SetSingleMemberIfAvailable(config, "jumpDistance", settings.JdFixerJumpDistance);

            if (changed)
            {
                NotifyJdFixerConfigChanged(config);
                RefreshJdFixerUi();
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
                                  ?? throw new GamePresentationSettingsNotReadyException("Beat Saber player data model is not available yet.");
            var playerData = playerDataModel.playerData
                             ?? throw new GamePresentationSettingsNotReadyException("Beat Saber player data is not loaded yet.");
            var current = playerData.playerSpecificSettings
                          ?? throw new GamePresentationSettingsNotReadyException("Beat Saber player-specific settings are not loaded yet.");

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
                                       ?? throw new GamePresentationSettingsNotReadyException("Beat Saber color settings are not loaded yet.");
            var selectedColorScheme = colorSchemesSettings.GetSelectedColorScheme()
                                      ?? colorSchemesSettings.GetColorSchemeForId("User0")
                                      ?? throw new InvalidOperationException("Beat Saber user color scheme is not available yet.");
            var colorSchemeId = GetStringProperty(selectedColorScheme, "colorSchemeId");
            colorSchemeId = string.IsNullOrWhiteSpace(colorSchemeId)
                ? "User0"
                : colorSchemeId;

            var nextColorScheme = CreateColorScheme(
                selectedColorScheme,
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

    private sealed class ScoreSaberReplaySettingsApplier : IGamePresentationSettingsSectionApplier
    {
        public string Name => "ScoreSaber replay settings";

        public GamePresentationSettingsApplyResult Apply(GamePresentationSettings settings)
        {
            var settingsService = ResolveScoreSaberSettingsService();
            if (settingsService == null)
            {
                return new GamePresentationSettingsApplyResult(false);
            }

            var current = GetInstancePropertyValue(settingsService, "Current");
            if (current == null)
            {
                return new GamePresentationSettingsApplyResult(false);
            }

            var useRecordedPlayerSettings = !settings.OverrideReplayPlayerSettings;
            if (!TrySetBooleanMember(
                    current,
                    "useRecordedPlayerSettings",
                    useRecordedPlayerSettings,
                    out var changed) ||
                !changed)
            {
                return new GamePresentationSettingsApplyResult(false);
            }

            SaveScoreSaberSettings(settingsService);
            return new GamePresentationSettingsApplyResult(true);
        }
    }

    private static void ApplyRuntimeAudioSettings(GamePresentationSettings settings)
    {
        ApplyRuntimeAudioSettings(settings.SfxVolume);
    }

    private static void ApplyRuntimeAudioSettings(float sfxVolume)
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
                ?.SetValue(audioManager, sfxVolume, null);
            audioManagerType
                .GetProperty("sfxEnabled", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(audioManager, true, null);
        }
    }

    private static class PlayerProfileRestore
    {
        private const string SnapshotDirectoryName = "BSAutoReplayRecorder";
        private const string SnapshotFileName = "player-profile-restore.json";

        public static void CaptureIfNeeded(IPA.Logging.Logger logger)
        {
            var snapshotPath = GetSnapshotPath();
            if (File.Exists(snapshotPath))
            {
                return;
            }

            var playerDataModel = ResolvePlayerDataModel()
                                  ?? throw new GamePresentationSettingsNotReadyException("Beat Saber player data model is not available yet.");
            var playerData = playerDataModel.playerData
                             ?? throw new GamePresentationSettingsNotReadyException("Beat Saber player data is not loaded yet.");
            var current = playerData.playerSpecificSettings
                          ?? throw new GamePresentationSettingsNotReadyException("Beat Saber player-specific settings are not loaded yet.");
            var snapshot = PlayerProfileRestoreSnapshot.Create(playerData, current);
            var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);

            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath) ?? GamePaths.GetRecorderUserDataDirectory());
            try
            {
                using (var stream = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(json);
                }

                logger.Info("Captured Beat Saber player profile restore snapshot at " + snapshotPath + ".");
            }
            catch (IOException) when (File.Exists(snapshotPath))
            {
                // Another managed instance captured the shared profile first.
            }
        }

        public static void ClearIfExists(IPA.Logging.Logger logger)
        {
            var snapshotPath = GetSnapshotPath();
            try
            {
                if (!File.Exists(snapshotPath))
                {
                    return;
                }

                File.Delete(snapshotPath);
                logger.Info("Cleared pending Beat Saber player profile restore snapshot.");
            }
            catch (Exception ex)
            {
                logger.Warn("Could not clear pending Beat Saber player profile restore snapshot: " + ex.Message);
            }
        }

        public static void RestoreIfPending(IPA.Logging.Logger logger)
        {
            var snapshotPath = GetSnapshotPath();
            if (!File.Exists(snapshotPath))
            {
                return;
            }

            try
            {
                var snapshot = JsonConvert.DeserializeObject<PlayerProfileRestoreSnapshot>(File.ReadAllText(snapshotPath));
                if (snapshot?.PlayerSpecificSettings == null)
                {
                    logger.Warn("Beat Saber player profile restore snapshot was empty; leaving it in place.");
                    return;
                }

                var playerDataModel = ResolvePlayerDataModel();
                var playerData = playerDataModel?.playerData;
                if (playerData == null || playerData.playerSpecificSettings == null)
                {
                    logger.Warn("Beat Saber player data was not available for profile restore; leaving snapshot in place.");
                    return;
                }

                var changed = RestorePlayerSpecificSettings(playerData, snapshot.PlayerSpecificSettings);
                changed |= RestoreColorSchemes(playerData, snapshot.ColorSchemes);
                if (changed)
                {
                    SavePlayerData(playerDataModel!);
                }

                ApplyRuntimeAudioSettings(snapshot.PlayerSpecificSettings.SfxVolume);
                File.Delete(snapshotPath);
                logger.Info("Restored Beat Saber player profile settings from " + snapshotPath + ".");
            }
            catch (Exception ex)
            {
                logger.Warn("Could not restore Beat Saber player profile settings: " + ex.Message);
            }
        }

        private static bool RestorePlayerSpecificSettings(
            PlayerData playerData,
            PlayerSpecificSettingsSnapshot snapshot)
        {
            var current = playerData.playerSpecificSettings
                          ?? throw new GamePresentationSettingsNotReadyException("Beat Saber player-specific settings are not loaded yet.");
            var next = current.CopyWith(
                snapshot.LeftHanded,
                snapshot.PlayerHeight,
                snapshot.AutomaticPlayerHeight,
                snapshot.SfxVolume,
                snapshot.ReduceDebris,
                snapshot.NoTextsAndHuds,
                snapshot.NoFailEffects,
                snapshot.AdvancedHud,
                snapshot.AutoRestart,
                snapshot.SaberTrailIntensity,
                ParseEnum(snapshot.NoteJumpDurationType, current.noteJumpDurationTypeSettings),
                snapshot.NoteJumpFixedDuration,
                snapshot.NoteJumpStartBeatOffset,
                snapshot.HideNoteSpawnEffect,
                snapshot.AdaptiveSfx,
                snapshot.ArcsHapticFeedback,
                ParseEnum(snapshot.ArcVisibility, current.arcVisibility),
                ParseEnum(snapshot.EnvironmentEffectsFilterDefaultPreset, current.environmentEffectsFilterDefaultPreset),
                ParseEnum(snapshot.EnvironmentEffectsFilterExpertPlusPreset, current.environmentEffectsFilterExpertPlusPreset),
                snapshot.HeadsetHapticIntensity);
            if (current.AreValuesEqual(next))
            {
                return false;
            }

            playerData.SetPlayerSpecificSettings(next);
            return true;
        }

        private static bool RestoreColorSchemes(
            PlayerData playerData,
            ColorSchemesSettingsSnapshot? snapshot)
        {
            if (snapshot?.SelectedColorScheme == null)
            {
                return false;
            }

            var colorSchemesSettings = playerData.colorSchemesSettings
                                       ?? throw new GamePresentationSettingsNotReadyException("Beat Saber color settings are not loaded yet.");
            var selectedColorSchemeId = string.IsNullOrWhiteSpace(snapshot.SelectedColorSchemeId)
                ? snapshot.SelectedColorScheme.ColorSchemeId
                : snapshot.SelectedColorSchemeId;
            var currentColorScheme =
                colorSchemesSettings.GetColorSchemeForId(snapshot.SelectedColorScheme.ColorSchemeId) ??
                colorSchemesSettings.GetColorSchemeForId(selectedColorSchemeId) ??
                colorSchemesSettings.GetSelectedColorScheme();
            if (currentColorScheme == null)
            {
                return false;
            }

            var restoredColorScheme = CreateColorScheme(snapshot.SelectedColorScheme, currentColorScheme);
            var changed =
                !string.Equals(colorSchemesSettings.selectedColorSchemeId, selectedColorSchemeId, StringComparison.Ordinal) ||
                colorSchemesSettings.overrideDefaultColors != snapshot.OverrideDefaultColors ||
                !string.Equals(GetEnumPropertyName(colorSchemesSettings, "colorOverrideType"), snapshot.ColorOverrideType, StringComparison.Ordinal) ||
                !ColorSchemesEqual(currentColorScheme, restoredColorScheme);

            colorSchemesSettings
                .GetType()
                .GetMethod("SetColorSchemeForId", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(colorSchemesSettings, new[] { restoredColorScheme });
            colorSchemesSettings.selectedColorSchemeId = selectedColorSchemeId;
            colorSchemesSettings.overrideDefaultColors = snapshot.OverrideDefaultColors;
            if (!string.IsNullOrWhiteSpace(snapshot.ColorOverrideType))
            {
                SetEnumPropertyByName(colorSchemesSettings, "colorOverrideType", snapshot.ColorOverrideType);
            }

            return changed;
        }

        private static string GetSnapshotPath()
        {
            var persistentDataPath = Application.persistentDataPath;
            if (string.IsNullOrWhiteSpace(persistentDataPath))
            {
                persistentDataPath = GamePaths.GetRecorderUserDataDirectory();
            }

            return Path.Combine(persistentDataPath, SnapshotDirectoryName, SnapshotFileName);
        }
    }

    private sealed class PlayerProfileRestoreSnapshot
    {
        public DateTimeOffset CreatedAtUtc { get; set; }

        public PlayerSpecificSettingsSnapshot PlayerSpecificSettings { get; set; } = new PlayerSpecificSettingsSnapshot();

        public ColorSchemesSettingsSnapshot? ColorSchemes { get; set; }

        public static PlayerProfileRestoreSnapshot Create(
            PlayerData playerData,
            PlayerSpecificSettings playerSpecificSettings)
        {
            return new PlayerProfileRestoreSnapshot
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                PlayerSpecificSettings = PlayerSpecificSettingsSnapshot.Create(playerSpecificSettings),
                ColorSchemes = ColorSchemesSettingsSnapshot.Create(playerData.colorSchemesSettings)
            };
        }
    }

    private sealed class PlayerSpecificSettingsSnapshot
    {
        public bool LeftHanded { get; set; }

        public float PlayerHeight { get; set; }

        public bool AutomaticPlayerHeight { get; set; }

        public float SfxVolume { get; set; }

        public bool ReduceDebris { get; set; }

        public bool NoTextsAndHuds { get; set; }

        public bool NoFailEffects { get; set; }

        public bool AdvancedHud { get; set; }

        public bool AutoRestart { get; set; }

        public float SaberTrailIntensity { get; set; }

        public string NoteJumpDurationType { get; set; } = "";

        public float NoteJumpFixedDuration { get; set; }

        public float NoteJumpStartBeatOffset { get; set; }

        public bool HideNoteSpawnEffect { get; set; }

        public bool AdaptiveSfx { get; set; }

        public bool ArcsHapticFeedback { get; set; }

        public string ArcVisibility { get; set; } = "";

        public string EnvironmentEffectsFilterDefaultPreset { get; set; } = "";

        public string EnvironmentEffectsFilterExpertPlusPreset { get; set; } = "";

        public float HeadsetHapticIntensity { get; set; }

        public static PlayerSpecificSettingsSnapshot Create(PlayerSpecificSettings settings)
        {
            return new PlayerSpecificSettingsSnapshot
            {
                LeftHanded = settings.leftHanded,
                PlayerHeight = settings.playerHeight,
                AutomaticPlayerHeight = settings.automaticPlayerHeight,
                SfxVolume = settings.sfxVolume,
                ReduceDebris = settings.reduceDebris,
                NoTextsAndHuds = settings.noTextsAndHuds,
                NoFailEffects = settings.noFailEffects,
                AdvancedHud = settings.advancedHud,
                AutoRestart = settings.autoRestart,
                SaberTrailIntensity = settings.saberTrailIntensity,
                NoteJumpDurationType = settings.noteJumpDurationTypeSettings.ToString(),
                NoteJumpFixedDuration = settings.noteJumpFixedDuration,
                NoteJumpStartBeatOffset = settings.noteJumpStartBeatOffset,
                HideNoteSpawnEffect = settings.hideNoteSpawnEffect,
                AdaptiveSfx = settings.adaptiveSfx,
                ArcsHapticFeedback = settings.arcsHapticFeedback,
                ArcVisibility = settings.arcVisibility.ToString(),
                EnvironmentEffectsFilterDefaultPreset = settings.environmentEffectsFilterDefaultPreset.ToString(),
                EnvironmentEffectsFilterExpertPlusPreset = settings.environmentEffectsFilterExpertPlusPreset.ToString(),
                HeadsetHapticIntensity = settings.headsetHapticIntensity
            };
        }
    }

    private sealed class ColorSchemesSettingsSnapshot
    {
        public string SelectedColorSchemeId { get; set; } = "";

        public bool OverrideDefaultColors { get; set; }

        public string ColorOverrideType { get; set; } = "";

        public ColorSchemeSnapshot? SelectedColorScheme { get; set; }

        public static ColorSchemesSettingsSnapshot? Create(ColorSchemesSettings? settings)
        {
            if (settings == null)
            {
                return null;
            }

            var selectedColorSchemeId = settings.selectedColorSchemeId ?? "";
            var selectedColorScheme = settings.GetSelectedColorScheme();
            if (selectedColorScheme == null && !string.IsNullOrWhiteSpace(selectedColorSchemeId))
            {
                selectedColorScheme = settings.GetColorSchemeForId(selectedColorSchemeId);
            }

            return new ColorSchemesSettingsSnapshot
            {
                SelectedColorSchemeId = selectedColorSchemeId,
                OverrideDefaultColors = settings.overrideDefaultColors,
                ColorOverrideType = GetEnumPropertyName(settings, "colorOverrideType"),
                SelectedColorScheme = selectedColorScheme == null
                    ? null
                    : ColorSchemeSnapshot.Create(selectedColorScheme)
            };
        }
    }

    private sealed class ColorSchemeSnapshot
    {
        public string ColorSchemeId { get; set; } = "";

        public string ColorSchemeNameLocalizationKey { get; set; } = "";

        public bool UseNonLocalizedName { get; set; }

        public string NonLocalizedName { get; set; } = "";

        public bool IsEditable { get; set; }

        public bool OverrideNotes { get; set; }

        public ColorValueSnapshot SaberAColor { get; set; } = new ColorValueSnapshot();

        public ColorValueSnapshot SaberBColor { get; set; } = new ColorValueSnapshot();

        public bool OverrideLights { get; set; }

        public ColorValueSnapshot EnvironmentColor0 { get; set; } = new ColorValueSnapshot();

        public ColorValueSnapshot EnvironmentColor1 { get; set; } = new ColorValueSnapshot();

        public ColorValueSnapshot EnvironmentColorW { get; set; } = new ColorValueSnapshot();

        public bool SupportsEnvironmentColorBoost { get; set; }

        public ColorValueSnapshot EnvironmentColor0Boost { get; set; } = new ColorValueSnapshot();

        public ColorValueSnapshot EnvironmentColor1Boost { get; set; } = new ColorValueSnapshot();

        public ColorValueSnapshot EnvironmentColorWBoost { get; set; } = new ColorValueSnapshot();

        public ColorValueSnapshot ObstaclesColor { get; set; } = new ColorValueSnapshot();

        public static ColorSchemeSnapshot Create(object colorScheme)
        {
            return new ColorSchemeSnapshot
            {
                ColorSchemeId = GetStringProperty(colorScheme, "colorSchemeId"),
                ColorSchemeNameLocalizationKey = GetStringProperty(colorScheme, "colorSchemeNameLocalizationKey"),
                UseNonLocalizedName = GetBooleanProperty(colorScheme, "useNonLocalizedName", false),
                NonLocalizedName = GetStringProperty(colorScheme, "nonLocalizedName"),
                IsEditable = GetBooleanProperty(colorScheme, "isEditable", true),
                OverrideNotes = GetBooleanProperty(colorScheme, "overrideNotes", true),
                SaberAColor = ColorValueSnapshot.Create(GetColorPropertyOrDefault(colorScheme, "saberAColor", Color.white)),
                SaberBColor = ColorValueSnapshot.Create(GetColorPropertyOrDefault(colorScheme, "saberBColor", Color.white)),
                OverrideLights = GetBooleanProperty(colorScheme, "overrideLights", true),
                EnvironmentColor0 = ColorValueSnapshot.Create(GetColorPropertyOrDefault(colorScheme, "environmentColor0", Color.white)),
                EnvironmentColor1 = ColorValueSnapshot.Create(GetColorPropertyOrDefault(colorScheme, "environmentColor1", Color.white)),
                EnvironmentColorW = ColorValueSnapshot.Create(GetColorPropertyOrDefault(colorScheme, "environmentColorW", Color.white)),
                SupportsEnvironmentColorBoost = GetBooleanProperty(colorScheme, "supportsEnvironmentColorBoost", true),
                EnvironmentColor0Boost = ColorValueSnapshot.Create(GetColorPropertyOrDefault(colorScheme, "environmentColor0Boost", Color.white)),
                EnvironmentColor1Boost = ColorValueSnapshot.Create(GetColorPropertyOrDefault(colorScheme, "environmentColor1Boost", Color.white)),
                EnvironmentColorWBoost = ColorValueSnapshot.Create(GetColorPropertyOrDefault(colorScheme, "environmentColorWBoost", Color.white)),
                ObstaclesColor = ColorValueSnapshot.Create(GetColorPropertyOrDefault(colorScheme, "obstaclesColor", Color.white))
            };
        }
    }

    private sealed class ColorValueSnapshot
    {
        public float R { get; set; }

        public float G { get; set; }

        public float B { get; set; }

        public float A { get; set; } = 1f;

        public static ColorValueSnapshot Create(Color color)
        {
            return new ColorValueSnapshot
            {
                R = color.r,
                G = color.g,
                B = color.b,
                A = color.a
            };
        }

        public Color ToColor()
        {
            return new Color(
                NormalizeColorComponent(R, 1f),
                NormalizeColorComponent(G, 1f),
                NormalizeColorComponent(B, 1f),
                NormalizeColorComponent(A, 1f));
        }

        private static float NormalizeColorComponent(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return fallback;
            }

            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }

    private static object? ResolveJdFixerPluginConfig()
    {
        var pluginConfigType = FindType("JDFixer.PluginConfig");
        return pluginConfigType == null
            ? null
            : GetStaticMemberValue(pluginConfigType, "Instance");
    }

    private static object? ResolveScoreSaberSettingsService()
    {
        var settingsServiceType = FindType("ScoreSaber.Core.Configuration.SettingsService");
        if (settingsServiceType == null)
        {
            return null;
        }

        foreach (var container in ResolveZenjectContainers())
        {
            var resolved = TryResolve(container, settingsServiceType);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return null;
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

    private static object? GetStaticMemberValue(Type targetType, string memberName)
    {
        var property = targetType.GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (property != null)
        {
            return property.GetValue(null, null);
        }

        return targetType
            .GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null);
    }

    private static object? GetInstancePropertyValue(object target, string propertyName)
    {
        return target.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(target, null);
    }

    private static object? GetInstanceMemberValue(object target, string memberName)
    {
        var targetType = target.GetType();
        var property = targetType.GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null && property.CanRead)
        {
            return property.GetValue(target, null);
        }

        return targetType
            .GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(target);
    }

    private static bool TrySetInstanceMemberValue(object target, string memberName, object value)
    {
        var targetType = target.GetType();
        var property = targetType.GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null &&
            property.CanWrite &&
            property.PropertyType.IsInstanceOfType(value))
        {
            property.SetValue(target, value, null);
            return true;
        }

        var field = targetType.GetField(
            memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null || !field.FieldType.IsInstanceOfType(value))
        {
            return false;
        }

        field.SetValue(target, value);
        return true;
    }

    private static bool SetBooleanMemberIfAvailable(object target, string memberName, bool nextValue)
    {
        return TrySetBooleanMember(target, memberName, nextValue, out var changed) && changed;
    }

    private static bool SetSingleMemberIfAvailable(object target, string memberName, float nextValue)
    {
        return TrySetSingleMember(target, memberName, nextValue, out var changed) && changed;
    }

    private static bool SetInt32MemberIfAvailable(object target, string memberName, int nextValue)
    {
        return TrySetInt32Member(target, memberName, nextValue, out var changed) && changed;
    }

    private static object? GetOrCreateMemberObject(
        object target,
        string memberName,
        string memberTypeName,
        out bool changed)
    {
        changed = false;

        var current = GetInstanceMemberValue(target, memberName);
        if (current != null)
        {
            return current;
        }

        var memberType = FindType(memberTypeName);
        if (memberType == null)
        {
            return null;
        }

        var created = Activator.CreateInstance(memberType);
        if (created == null || !TrySetInstanceMemberValue(target, memberName, created))
        {
            return null;
        }

        changed = true;
        return created;
    }

    private static object? GetOrCreateBodySettingsConfig(
        object bodySettings,
        string configTypeName,
        out bool changed)
    {
        changed = false;

        var configType = FindType(configTypeName);
        if (configType == null)
        {
            return null;
        }

        var getConfigMethod = FindGenericInstanceMethod(bodySettings.GetType(), "GetConfig", parameterCount: 0);
        var config = getConfigMethod
            ?.MakeGenericMethod(configType)
            .Invoke(bodySettings, null);
        if (config != null)
        {
            return config;
        }

        config = Activator.CreateInstance(configType);
        if (config == null)
        {
            return null;
        }

        var setConfigMethod = FindGenericInstanceMethod(bodySettings.GetType(), "SetConfig", parameterCount: 1);
        if (setConfigMethod == null)
        {
            return null;
        }

        setConfigMethod.MakeGenericMethod(configType).Invoke(bodySettings, new[] { config });
        changed = true;
        return config;
    }

    private static void NotifyBodySettingsConfigUpdated(object bodySettings, object config)
    {
        bodySettings
            .GetType()
            .GetMethod("NotifyConfigUpdated", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(bodySettings, new[] { config });
    }

    private static bool SetEnumFlagsMemberIfAvailable(
        object target,
        string memberName,
        IReadOnlyDictionary<string, bool> flagValues)
    {
        var targetType = target.GetType();
        var property = targetType.GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property?.PropertyType.IsEnum == true && property.CanRead && property.CanWrite)
        {
            var current = property.GetValue(target, null);
            var nextValue = CreateEnumFlagsValue(property.PropertyType, current, flagValues);
            if (Equals(current, nextValue))
            {
                return false;
            }

            property.SetValue(target, nextValue, null);
            return true;
        }

        var field = targetType.GetField(
            memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.FieldType.IsEnum != true)
        {
            return false;
        }

        var fieldValue = field.GetValue(target);
        var nextFieldValue = CreateEnumFlagsValue(field.FieldType, fieldValue, flagValues);
        if (Equals(fieldValue, nextFieldValue))
        {
            return false;
        }

        field.SetValue(target, nextFieldValue);
        return true;
    }

    private static object CreateEnumFlagsValue(
        Type enumType,
        object? currentValue,
        IReadOnlyDictionary<string, bool> flagValues)
    {
        var nextBits = currentValue == null ? 0 : Convert.ToInt32(currentValue);
        foreach (var item in flagValues)
        {
            if (!Enum.IsDefined(enumType, item.Key))
            {
                continue;
            }

            var bit = Convert.ToInt32(Enum.Parse(enumType, item.Key));
            nextBits = item.Value
                ? nextBits | bit
                : nextBits & ~bit;
        }

        return Enum.ToObject(enumType, nextBits);
    }

    private static MethodInfo? FindGenericInstanceMethod(Type targetType, string methodName, int parameterCount)
    {
        foreach (var method in targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Length == parameterCount)
            {
                return method;
            }
        }

        return null;
    }

    private static bool TrySetBooleanMember(object target, string memberName, bool nextValue, out bool changed)
    {
        changed = false;
        var targetType = target.GetType();
        var property = targetType.GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property?.PropertyType == typeof(bool) && property.CanRead && property.CanWrite)
        {
            var currentValue = property.GetValue(target, null) is bool value && value;
            if (currentValue == nextValue)
            {
                return true;
            }

            property.SetValue(target, nextValue, null);
            changed = true;
            return true;
        }

        var field = targetType.GetField(
            memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.FieldType != typeof(bool))
        {
            return false;
        }

        var fieldValue = field.GetValue(target) is bool valueFromField && valueFromField;
        if (fieldValue == nextValue)
        {
            return true;
        }

        field.SetValue(target, nextValue);
        changed = true;
        return true;
    }

    private static bool TrySetSingleMember(object target, string memberName, float nextValue, out bool changed)
    {
        changed = false;
        var targetType = target.GetType();
        var property = targetType.GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property?.PropertyType == typeof(float) && property.CanRead && property.CanWrite)
        {
            var currentValue = property.GetValue(target, null) is float value ? value : 0f;
            if (Math.Abs(currentValue - nextValue) < 0.0001f)
            {
                return true;
            }

            property.SetValue(target, nextValue, null);
            changed = true;
            return true;
        }

        var field = targetType.GetField(
            memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.FieldType != typeof(float))
        {
            return false;
        }

        var fieldValue = field.GetValue(target) is float valueFromField ? valueFromField : 0f;
        if (Math.Abs(fieldValue - nextValue) < 0.0001f)
        {
            return true;
        }

        field.SetValue(target, nextValue);
        changed = true;
        return true;
    }

    private static bool TrySetInt32Member(object target, string memberName, int nextValue, out bool changed)
    {
        changed = false;
        var targetType = target.GetType();
        var property = targetType.GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property?.PropertyType == typeof(int) && property.CanRead && property.CanWrite)
        {
            var currentValue = property.GetValue(target, null) is int value ? value : 0;
            if (currentValue == nextValue)
            {
                return true;
            }

            property.SetValue(target, nextValue, null);
            changed = true;
            return true;
        }

        var field = targetType.GetField(
            memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.FieldType != typeof(int))
        {
            return false;
        }

        var fieldValue = field.GetValue(target) is int valueFromField ? valueFromField : 0;
        if (fieldValue == nextValue)
        {
            return true;
        }

        field.SetValue(target, nextValue);
        changed = true;
        return true;
    }

    private static void NotifyJdFixerConfigChanged(object config)
    {
        config.GetType()
            .GetMethod("Changed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(config, null);
    }

    private static void RefreshJdFixerUi()
    {
        foreach (var typeName in new[]
                 {
                     "JDFixer.UI.ModifierUI",
                     "JDFixer.UI.LegacyModifierUI",
                     "JDFixer.UI.CustomOnlineUI"
                 })
        {
            var type = FindType(typeName);
            if (type == null)
            {
                continue;
            }

            var instance = GetStaticMemberValue(type, "Instance");
            if (instance == null)
            {
                continue;
            }

            type.GetMethod("Refresh", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(instance, null);
        }
    }

    private static void SaveScoreSaberSettings(object settingsService)
    {
        settingsService.GetType()
            .GetMethod("Save", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(settingsService, null);
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

    private static object CreateColorScheme(object currentColorScheme, string colorSchemeId, GamePresentationSettings settings)
    {
        var colorSchemeType = currentColorScheme.GetType();
        var leftSaberColor = ParseColor(settings.LeftSaberColor);
        var rightSaberColor = ParseColor(settings.RightSaberColor);
        var wallColor = ParseColor(settings.WallColor);
        var lightColorA = ParseColor(settings.LightColorA);
        var lightColorB = ParseColor(settings.LightColorB);
        var lightColorW = GetColorPropertyOrDefault(currentColorScheme, "environmentColorW", Color.white);
        var boostLightColorA = ParseColor(settings.BoostLightColorA);
        var boostLightColorB = ParseColor(settings.BoostLightColorB);
        var boostLightColorW = GetColorPropertyOrDefault(currentColorScheme, "environmentColorWBoost", Color.white);

        var copyConstructor = colorSchemeType.GetConstructor(new[]
        {
            colorSchemeType,
            typeof(bool),
            typeof(Color),
            typeof(Color),
            typeof(bool),
            typeof(Color),
            typeof(Color),
            typeof(Color),
            typeof(bool),
            typeof(Color),
            typeof(Color),
            typeof(Color),
            typeof(Color)
        });
        if (copyConstructor != null)
        {
            return copyConstructor.Invoke(new[]
            {
                currentColorScheme,
                true,
                leftSaberColor,
                rightSaberColor,
                true,
                lightColorA,
                lightColorB,
                lightColorW,
                true,
                boostLightColorA,
                boostLightColorB,
                boostLightColorW,
                wallColor
            });
        }

        var currentConstructor = colorSchemeType.GetConstructor(new[]
        {
            typeof(string),
            typeof(string),
            typeof(bool),
            typeof(string),
            typeof(bool),
            typeof(bool),
            typeof(Color),
            typeof(Color),
            typeof(bool),
            typeof(Color),
            typeof(Color),
            typeof(Color),
            typeof(bool),
            typeof(Color),
            typeof(Color),
            typeof(Color),
            typeof(Color)
        });
        if (currentConstructor != null)
        {
            var nonLocalizedName = GetStringProperty(currentColorScheme, "nonLocalizedName");
            if (string.IsNullOrWhiteSpace(nonLocalizedName))
            {
                nonLocalizedName = "Replay Recorder";
            }

            return currentConstructor.Invoke(new object[]
            {
                colorSchemeId,
                GetStringProperty(currentColorScheme, "colorSchemeNameLocalizationKey"),
                GetBooleanProperty(currentColorScheme, "useNonLocalizedName", true),
                nonLocalizedName,
                GetBooleanProperty(currentColorScheme, "isEditable", true),
                true,
                leftSaberColor,
                rightSaberColor,
                true,
                lightColorA,
                lightColorB,
                lightColorW,
                true,
                boostLightColorA,
                boostLightColorB,
                boostLightColorW,
                wallColor
            });
        }

        var legacyConstructor = colorSchemeType.GetConstructor(new[]
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
        });
        if (legacyConstructor != null)
        {
            return legacyConstructor.Invoke(new object[]
            {
                colorSchemeId,
                true,
                leftSaberColor,
                rightSaberColor,
                wallColor,
                true,
                lightColorA,
                lightColorB,
                boostLightColorA,
                boostLightColorB
            });
        }

        throw new InvalidOperationException("Beat Saber color scheme constructor is not available.");
    }

    private static object CreateColorScheme(ColorSchemeSnapshot snapshot, object currentColorScheme)
    {
        var colorSchemeType = currentColorScheme.GetType();
        var colorSchemeId = string.IsNullOrWhiteSpace(snapshot.ColorSchemeId)
            ? GetStringProperty(currentColorScheme, "colorSchemeId")
            : snapshot.ColorSchemeId;
        var colorSchemeNameLocalizationKey = string.IsNullOrWhiteSpace(snapshot.ColorSchemeNameLocalizationKey)
            ? GetStringProperty(currentColorScheme, "colorSchemeNameLocalizationKey")
            : snapshot.ColorSchemeNameLocalizationKey;
        var nonLocalizedName = string.IsNullOrWhiteSpace(snapshot.NonLocalizedName)
            ? GetStringProperty(currentColorScheme, "nonLocalizedName")
            : snapshot.NonLocalizedName;

        var currentConstructor = colorSchemeType.GetConstructor(new[]
        {
            typeof(string),
            typeof(string),
            typeof(bool),
            typeof(string),
            typeof(bool),
            typeof(bool),
            typeof(Color),
            typeof(Color),
            typeof(bool),
            typeof(Color),
            typeof(Color),
            typeof(Color),
            typeof(bool),
            typeof(Color),
            typeof(Color),
            typeof(Color),
            typeof(Color)
        });
        if (currentConstructor != null)
        {
            return currentConstructor.Invoke(new object[]
            {
                colorSchemeId,
                colorSchemeNameLocalizationKey,
                snapshot.UseNonLocalizedName,
                nonLocalizedName,
                snapshot.IsEditable,
                snapshot.OverrideNotes,
                snapshot.SaberAColor.ToColor(),
                snapshot.SaberBColor.ToColor(),
                snapshot.OverrideLights,
                snapshot.EnvironmentColor0.ToColor(),
                snapshot.EnvironmentColor1.ToColor(),
                snapshot.EnvironmentColorW.ToColor(),
                snapshot.SupportsEnvironmentColorBoost,
                snapshot.EnvironmentColor0Boost.ToColor(),
                snapshot.EnvironmentColor1Boost.ToColor(),
                snapshot.EnvironmentColorWBoost.ToColor(),
                snapshot.ObstaclesColor.ToColor()
            });
        }

        var copyConstructor = colorSchemeType.GetConstructor(new[]
        {
            colorSchemeType,
            typeof(bool),
            typeof(Color),
            typeof(Color),
            typeof(bool),
            typeof(Color),
            typeof(Color),
            typeof(Color),
            typeof(bool),
            typeof(Color),
            typeof(Color),
            typeof(Color),
            typeof(Color)
        });
        if (copyConstructor != null)
        {
            return copyConstructor.Invoke(new object[]
            {
                currentColorScheme,
                snapshot.OverrideNotes,
                snapshot.SaberAColor.ToColor(),
                snapshot.SaberBColor.ToColor(),
                snapshot.OverrideLights,
                snapshot.EnvironmentColor0.ToColor(),
                snapshot.EnvironmentColor1.ToColor(),
                snapshot.EnvironmentColorW.ToColor(),
                snapshot.SupportsEnvironmentColorBoost,
                snapshot.EnvironmentColor0Boost.ToColor(),
                snapshot.EnvironmentColor1Boost.ToColor(),
                snapshot.EnvironmentColorWBoost.ToColor(),
                snapshot.ObstaclesColor.ToColor()
            });
        }

        var legacyConstructor = colorSchemeType.GetConstructor(new[]
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
        });
        if (legacyConstructor != null)
        {
            return legacyConstructor.Invoke(new object[]
            {
                colorSchemeId,
                snapshot.OverrideNotes,
                snapshot.SaberAColor.ToColor(),
                snapshot.SaberBColor.ToColor(),
                snapshot.ObstaclesColor.ToColor(),
                snapshot.OverrideLights,
                snapshot.EnvironmentColor0.ToColor(),
                snapshot.EnvironmentColor1.ToColor(),
                snapshot.EnvironmentColor0Boost.ToColor(),
                snapshot.EnvironmentColor1Boost.ToColor()
            });
        }

        throw new InvalidOperationException("Beat Saber color scheme constructor is not available.");
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

    private static Color GetColorPropertyOrDefault(object target, string propertyName, Color fallback)
    {
        var value = target.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(target, null);
        return value is Color color ? color : fallback;
    }

    private static bool GetBooleanProperty(object target, string propertyName, bool fallback)
    {
        var value = target.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(target, null);
        return value is bool boolean ? boolean : fallback;
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
