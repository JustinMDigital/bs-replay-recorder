namespace BSAutoReplayRecorder.Core;

public sealed class GamePresentationSettings
{
    public const string NoteJumpDurationTypeDynamic = "Dynamic";
    public const string NoteJumpDurationTypeStatic = "Static";
    public const string ArcVisibilityNone = "None";
    public const string ArcVisibilityLow = "Low";
    public const string ArcVisibilityStandard = "Standard";
    public const string ArcVisibilityHigh = "High";
    public const string EnvironmentEffectsAllEffects = "AllEffects";
    public const string EnvironmentEffectsStrobeFilter = "StrobeFilter";
    public const string EnvironmentEffectsNoEffects = "NoEffects";

    public bool NoHud { get; set; } = true;

    public bool LoadPlayerEnvironment { get; set; }

    public bool LoadPlayerJumpDistance { get; set; }

    public bool IgnoreModifiers { get; set; }

    public bool ShowHead { get; set; }

    public bool ShowLeftSaber { get; set; } = true;

    public bool ShowRightSaber { get; set; } = true;

    public bool ShowWatermark { get; set; } = true;

    public bool ShowTimelineMisses { get; set; } = true;

    public bool ShowTimelineBombs { get; set; } = true;

    public bool ShowTimelinePauses { get; set; } = true;

    public float SfxVolume { get; set; } = 0.3f;

    public bool NoTextsAndHuds { get; set; } = true;

    public bool AdvancedHud { get; set; }

    public bool ReduceDebris { get; set; } = true;

    public bool NoFailEffects { get; set; }

    public float SaberTrailIntensity { get; set; }

    public string LeftSaberColor { get; set; } = "#a82020";

    public string RightSaberColor { get; set; } = "#2064a8";

    public string LightColorA { get; set; } = "#ff3030";

    public string LightColorB { get; set; } = "#c03030";

    public string BoostLightColorA { get; set; } = "#ff3030";

    public string BoostLightColorB { get; set; } = "#c03030";

    public string WallColor { get; set; } = "#3098ff";

    public string NoteJumpDurationType { get; set; } = NoteJumpDurationTypeDynamic;

    public float NoteJumpFixedDuration { get; set; } = 0.2f;

    public float NoteJumpStartBeatOffset { get; set; }

    public bool HideNoteSpawnEffect { get; set; }

    public bool AdaptiveSfx { get; set; } = true;

    public bool ArcsHapticFeedback { get; set; } = true;

    public string ArcVisibility { get; set; } = ArcVisibilityLow;

    public string EnvironmentEffectsFilterDefaultPreset { get; set; } = EnvironmentEffectsAllEffects;

    public string EnvironmentEffectsFilterExpertPlusPreset { get; set; } = EnvironmentEffectsAllEffects;

    public float HeadsetHapticIntensity { get; set; } = 0.7f;

    public void Normalize()
    {
        SfxVolume = ClampFinite(SfxVolume, 0f, 1f, 0.3f);
        SaberTrailIntensity = ClampFinite(SaberTrailIntensity, 0f, 1f, 0f);
        LeftSaberColor = NormalizeHexColor(LeftSaberColor, "#a82020");
        RightSaberColor = NormalizeHexColor(RightSaberColor, "#2064a8");
        LightColorA = NormalizeHexColor(LightColorA, "#ff3030");
        LightColorB = NormalizeHexColor(LightColorB, "#c03030");
        BoostLightColorA = NormalizeHexColor(BoostLightColorA, LightColorA);
        BoostLightColorB = NormalizeHexColor(BoostLightColorB, LightColorB);
        WallColor = NormalizeHexColor(WallColor, "#3098ff");
        NoteJumpDurationType = NormalizeChoice(
            NoteJumpDurationType,
            NoteJumpDurationTypeDynamic,
            NoteJumpDurationTypeDynamic,
            NoteJumpDurationTypeStatic);
        NoteJumpFixedDuration = ClampFinite(NoteJumpFixedDuration, 0.1f, 1f, 0.2f);
        NoteJumpStartBeatOffset = ClampFinite(NoteJumpStartBeatOffset, -1f, 1f, 0f);
        ArcVisibility = NormalizeChoice(
            ArcVisibility,
            ArcVisibilityLow,
            ArcVisibilityNone,
            ArcVisibilityLow,
            ArcVisibilityStandard,
            ArcVisibilityHigh);
        EnvironmentEffectsFilterDefaultPreset = NormalizeChoice(
            EnvironmentEffectsFilterDefaultPreset,
            EnvironmentEffectsAllEffects,
            EnvironmentEffectsAllEffects,
            EnvironmentEffectsStrobeFilter,
            EnvironmentEffectsNoEffects);
        EnvironmentEffectsFilterExpertPlusPreset = NormalizeChoice(
            EnvironmentEffectsFilterExpertPlusPreset,
            EnvironmentEffectsAllEffects,
            EnvironmentEffectsAllEffects,
            EnvironmentEffectsStrobeFilter,
            EnvironmentEffectsNoEffects);
        HeadsetHapticIntensity = ClampFinite(HeadsetHapticIntensity, 0f, 1f, 0.7f);
    }

    private static float ClampFinite(float value, float minimum, float maximum, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            value = fallback;
        }

        if (value < minimum)
        {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private static string NormalizeHexColor(string? value, string fallback)
    {
        var trimmed = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return fallback;
        }

        if (!trimmed.StartsWith("#", System.StringComparison.Ordinal))
        {
            trimmed = "#" + trimmed;
        }

        if (trimmed.Length != 7)
        {
            return fallback;
        }

        for (var index = 1; index < trimmed.Length; index++)
        {
            var character = trimmed[index];
            var isHex =
                character >= '0' && character <= '9' ||
                character >= 'a' && character <= 'f' ||
                character >= 'A' && character <= 'F';
            if (!isHex)
            {
                return fallback;
            }
        }

        return trimmed.ToLowerInvariant();
    }

    private static string NormalizeChoice(string? value, string fallback, params string[] allowedValues)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return fallback;
        }

        foreach (var allowedValue in allowedValues)
        {
            if (string.Equals(trimmed, allowedValue, System.StringComparison.OrdinalIgnoreCase))
            {
                return allowedValue;
            }
        }

        return fallback;
    }
}
