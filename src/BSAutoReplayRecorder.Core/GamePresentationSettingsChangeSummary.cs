using System;
using System.Collections.Generic;
using System.Globalization;

namespace BSAutoReplayRecorder.Core;

public sealed class GamePresentationSettingsChangeSummary
{
    private static readonly FieldDescriptor[] FieldDescriptors =
    {
        new FieldDescriptor("HUDs", settings => FormatEnabled(!settings.NoTextsAndHuds)),
        new FieldDescriptor("Saber trail", settings => FormatPercent(settings.SaberTrailIntensity)),
        new FieldDescriptor("SFX volume", settings => FormatPercent(settings.SfxVolume)),
        new FieldDescriptor("Auto-hide UI", settings => FormatEnabled(settings.NoHud)),
        new FieldDescriptor("Override player settings", settings => FormatEnabled(settings.OverrideReplayPlayerSettings)),
        new FieldDescriptor("Restore on close", settings => FormatEnabled(settings.RestorePlayerSettingsOnExit)),
        new FieldDescriptor("Player environment", settings => FormatEnabled(settings.LoadPlayerEnvironment)),
        new FieldDescriptor("Replay jump distance", settings => FormatEnabled(settings.LoadPlayerJumpDistance)),
        new FieldDescriptor("Ignore modifiers", settings => FormatEnabled(settings.IgnoreModifiers)),
        new FieldDescriptor("Head", settings => FormatEnabled(settings.ShowHead)),
        new FieldDescriptor("Left saber", settings => FormatEnabled(settings.ShowLeftSaber)),
        new FieldDescriptor("Right saber", settings => FormatEnabled(settings.ShowRightSaber)),
        new FieldDescriptor("Watermark", settings => FormatEnabled(settings.ShowWatermark)),
        new FieldDescriptor("Timeline misses", settings => FormatEnabled(settings.ShowTimelineMisses)),
        new FieldDescriptor("Timeline bombs", settings => FormatEnabled(settings.ShowTimelineBombs)),
        new FieldDescriptor("Timeline pauses", settings => FormatEnabled(settings.ShowTimelinePauses)),
        new FieldDescriptor("Advanced HUD", settings => FormatEnabled(settings.AdvancedHud)),
        new FieldDescriptor("Reduce debris", settings => FormatEnabled(settings.ReduceDebris)),
        new FieldDescriptor("Fail effects", settings => FormatEnabled(!settings.NoFailEffects)),
        new FieldDescriptor("Left saber color", settings => FormatColor(settings.LeftSaberColor)),
        new FieldDescriptor("Right saber color", settings => FormatColor(settings.RightSaberColor)),
        new FieldDescriptor("Light A", settings => FormatColor(settings.LightColorA)),
        new FieldDescriptor("Light B", settings => FormatColor(settings.LightColorB)),
        new FieldDescriptor("Boost light A", settings => FormatColor(settings.BoostLightColorA)),
        new FieldDescriptor("Boost light B", settings => FormatColor(settings.BoostLightColorB)),
        new FieldDescriptor("Wall color", settings => FormatColor(settings.WallColor)),
        new FieldDescriptor("Note jump mode", settings => FormatNoteJumpDurationType(settings.NoteJumpDurationType)),
        new FieldDescriptor("Static note jump", settings => FormatSeconds(settings.NoteJumpFixedDuration)),
        new FieldDescriptor("Note jump offset", settings => FormatNumber(settings.NoteJumpStartBeatOffset)),
        new FieldDescriptor("JDFixer", settings => FormatEnabled(settings.ApplyJdFixerSettings)),
        new FieldDescriptor("JDFixer mode", settings => FormatJdFixerMode(settings.JdFixerMode)),
        new FieldDescriptor("JDFixer jump distance", settings => FormatNumber(settings.JdFixerJumpDistance)),
        new FieldDescriptor("JDFixer reaction time", settings => FormatMilliseconds(settings.JdFixerReactionTime)),
        new FieldDescriptor("Spawn effect", settings => settings.HideNoteSpawnEffect ? "Hidden" : "Shown"),
        new FieldDescriptor("Adaptive SFX", settings => FormatEnabled(settings.AdaptiveSfx)),
        new FieldDescriptor("Arc haptics", settings => FormatEnabled(settings.ArcsHapticFeedback)),
        new FieldDescriptor("Arcs", settings => FormatArcVisibility(settings.ArcVisibility)),
        new FieldDescriptor("Lights", settings => FormatEnvironmentEffects(settings.EnvironmentEffectsFilterDefaultPreset)),
        new FieldDescriptor("Expert+ lights", settings => FormatEnvironmentEffects(settings.EnvironmentEffectsFilterExpertPlusPreset)),
        new FieldDescriptor("Headset haptics", settings => FormatPercent(settings.HeadsetHapticIntensity))
    };

    private GamePresentationSettingsChangeSummary(IReadOnlyList<string> lines, int totalChanges)
    {
        Lines = lines;
        TotalChanges = totalChanges;
    }

    public IReadOnlyList<string> Lines { get; }

    public int TotalChanges { get; }

    public int AdditionalChanges => Math.Max(0, TotalChanges - Lines.Count);

    public bool HasChanges => TotalChanges > 0;

    public static GamePresentationSettingsChangeSummary Create(
        GamePresentationSettings? previous,
        GamePresentationSettings? current,
        int maximumLines = 2)
    {
        if (previous == null || current == null)
        {
            return new GamePresentationSettingsChangeSummary(new List<string>().AsReadOnly(), 0);
        }

        var normalizedPrevious = previous.Clone();
        var normalizedCurrent = current.Clone();
        var lineLimit = Math.Max(0, maximumLines);
        var lines = new List<string>();
        var totalChanges = 0;

        foreach (var descriptor in FieldDescriptors)
        {
            var before = descriptor.FormatValue(normalizedPrevious);
            var after = descriptor.FormatValue(normalizedCurrent);
            if (string.Equals(before, after, StringComparison.Ordinal))
            {
                continue;
            }

            totalChanges++;
            if (lines.Count < lineLimit)
            {
                lines.Add(descriptor.Label + ": " + before + " -> " + after);
            }
        }

        return new GamePresentationSettingsChangeSummary(lines.AsReadOnly(), totalChanges);
    }

    private static string FormatEnabled(bool value)
    {
        return value ? "Enabled" : "Disabled";
    }

    private static string FormatPercent(float value)
    {
        return Math.Round(value * 100f, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatSeconds(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture) + "s";
    }

    private static string FormatMilliseconds(float value)
    {
        return Math.Round(value, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture) + "ms";
    }

    private static string FormatNumber(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatColor(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();
    }

    private static string FormatNoteJumpDurationType(string value)
    {
        return string.Equals(value, GamePresentationSettings.NoteJumpDurationTypeStatic, StringComparison.OrdinalIgnoreCase)
            ? "Static"
            : "Dynamic";
    }

    private static string FormatJdFixerMode(string value)
    {
        return string.Equals(value, GamePresentationSettings.JdFixerModeJumpDistance, StringComparison.OrdinalIgnoreCase)
            ? "Jump distance"
            : "Reaction time";
    }

    private static string FormatArcVisibility(string value)
    {
        if (string.Equals(value, GamePresentationSettings.ArcVisibilityNone, StringComparison.OrdinalIgnoreCase))
        {
            return "None";
        }

        if (string.Equals(value, GamePresentationSettings.ArcVisibilityStandard, StringComparison.OrdinalIgnoreCase))
        {
            return "Standard";
        }

        return string.Equals(value, GamePresentationSettings.ArcVisibilityHigh, StringComparison.OrdinalIgnoreCase)
            ? "High"
            : "Low";
    }

    private static string FormatEnvironmentEffects(string value)
    {
        if (string.Equals(value, GamePresentationSettings.EnvironmentEffectsStrobeFilter, StringComparison.OrdinalIgnoreCase))
        {
            return "Strobe filter";
        }

        return string.Equals(value, GamePresentationSettings.EnvironmentEffectsNoEffects, StringComparison.OrdinalIgnoreCase)
            ? "No effects"
            : "All effects";
    }

    private sealed class FieldDescriptor
    {
        public FieldDescriptor(string label, Func<GamePresentationSettings, string> formatValue)
        {
            Label = label;
            FormatValue = formatValue;
        }

        public string Label { get; }

        public Func<GamePresentationSettings, string> FormatValue { get; }
    }
}
