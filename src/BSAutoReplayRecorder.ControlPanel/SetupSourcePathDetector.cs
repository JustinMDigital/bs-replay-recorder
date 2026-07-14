using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BSAutoReplayRecorder.ControlPanel;

public static class SetupSourcePathDetector
{
    private const string BeatSaberExecutableName = "Beat Saber.exe";
    private const string MetaCanonicalName = "hyperbolic-magnetism-beat-saber";
    private static readonly string[] SupportedWorkerPluginVersions =
    {
        "1.39.1",
        "1.40.6",
        "1.40.8",
        "1.44.1"
    };

    public static SetupSourcePathReport Detect(string? configuredSourceBeatSaberPath, string? configuredStore = null)
        => DetectCore(configuredSourceBeatSaberPath, steamLibraryCandidates: null, metaLibraryCandidates: null, bsManagerRootCandidates: null, configuredStore);

    // Retained for the existing deterministic Steam detector tests.
    public static SetupSourcePathReport Detect(string? configuredSourceBeatSaberPath, IEnumerable<string>? steamLibraryCandidates)
        => DetectCore(configuredSourceBeatSaberPath, steamLibraryCandidates, metaLibraryCandidates: Array.Empty<string>(), bsManagerRootCandidates: Array.Empty<string>());

    public static SetupSourcePathReport Detect(
        string? configuredSourceBeatSaberPath,
        IEnumerable<string>? steamLibraryCandidates,
        IEnumerable<string>? metaLibraryCandidates,
        string? configuredStore = null)
        => DetectCore(configuredSourceBeatSaberPath, steamLibraryCandidates, metaLibraryCandidates, Array.Empty<string>(), configuredStore);

    public static SetupSourcePathReport DetectWithBsManagerRoots(
        string? configuredSourceBeatSaberPath,
        IEnumerable<string>? steamLibraryCandidates,
        IEnumerable<string>? metaLibraryCandidates,
        IEnumerable<string>? bsManagerRootCandidates,
        string? configuredStore = null)
        => DetectCore(configuredSourceBeatSaberPath, steamLibraryCandidates, metaLibraryCandidates, bsManagerRootCandidates, configuredStore);

    private static SetupSourcePathReport DetectCore(
        string? configuredSourceBeatSaberPath,
        IEnumerable<string>? steamLibraryCandidates,
        IEnumerable<string>? metaLibraryCandidates,
        IEnumerable<string>? bsManagerRootCandidates,
        string? configuredStore = null)
    {
        var configured = NormalizePath(configuredSourceBeatSaberPath);
        var candidates = FindCandidates(steamLibraryCandidates, metaLibraryCandidates, bsManagerRootCandidates);
        var configuredReady = IsBeatSaberDirectory(configured);
        var detectedConfiguredCandidate = candidates.FirstOrDefault(candidate => PathsEqual(candidate.Path, configured));
        var configuredStoreKind = detectedConfiguredCandidate?.Store ??
                                  InferStoreFromDirectory(configured, configuredStore);
        var configuredCandidate = detectedConfiguredCandidate ??
                                  (configuredReady ? CreateCandidate(configuredStoreKind, configured) : null);
        var configuredMissingPrerequisites = configuredCandidate?.MissingPrerequisites ?? new List<string>();
        var configuredRecorderReady = configuredReady && configuredCandidate?.RecorderReady == true;
        var detected = candidates.FirstOrDefault();
        var report = new SetupSourcePathReport
        {
            ConfiguredSourceBeatSaberPath = configured,
            ConfiguredSourceStore = configuredStoreKind,
            ConfiguredSourceReady = configuredReady,
            ConfiguredSourceRecorderReady = configuredRecorderReady,
            ConfiguredSourceMissingPrerequisites = configuredMissingPrerequisites,
            DetectedSourceBeatSaberPath = detected?.Path ?? "",
            DetectedSourceReady = detected?.Ready ?? false,
            DetectedSourceRecorderReady = detected?.RecorderReady ?? false,
            DetectedSourceMissingPrerequisites = detected?.MissingPrerequisites ?? new List<string>(),
            DetectedSources = candidates
        };

        if (configuredRecorderReady)
        {
            report.Status = "Ready";
            report.Summary = "Configured " + BeatSaberStore.DisplayName(configuredStoreKind) + " Beat Saber source is ready.";
            report.EffectiveSourceBeatSaberPath = configured;
            report.EffectiveSourceStore = configuredStoreKind;
        }
        else if (configuredReady)
        {
            report.Status = "PrerequisitesMissing";
            report.Summary = "Configured " + BeatSaberStore.DisplayName(configuredStoreKind) +
                             " Beat Saber source needs " + string.Join(" + ", configuredMissingPrerequisites) + ".";
            report.EffectiveSourceBeatSaberPath = configured;
            report.EffectiveSourceStore = configuredStoreKind;
        }
        else if (candidates.Count == 1)
        {
            report.Status = "Detected";
            report.Summary = candidates[0].RecorderReady
                ? "Detected a " + candidates[0].DisplayName + " Beat Saber install."
                : "Detected a " + candidates[0].DisplayName + " Beat Saber install that needs " +
                  string.Join(" + ", candidates[0].MissingPrerequisites) + ".";
            report.EffectiveSourceBeatSaberPath = candidates[0].Path;
            report.EffectiveSourceStore = candidates[0].Store;
        }
        else if (candidates.Count > 1)
        {
            report.Status = "SelectionRequired";
            report.Summary = "Choose which detected Beat Saber install to use.";
        }
        else if (!string.IsNullOrWhiteSpace(configured))
        {
            report.Status = "Missing";
            report.Summary = "Configured source does not contain " + BeatSaberExecutableName + ".";
        }

        return report;
    }

    public static string InferStoreFromDirectory(string? directory, string? requestedStore = null)
    {
        var normalized = NormalizePath(directory);
        if (string.IsNullOrWhiteSpace(normalized)) return BeatSaberStore.Normalize(requestedStore);
        if (File.Exists(Path.Combine(normalized, "Beat Saber_Data", "Plugins", "x86_64", "steam_api64.dll"))) return BeatSaberStore.Steam;
        if (normalized.Contains(MetaCanonicalName, StringComparison.OrdinalIgnoreCase)) return BeatSaberStore.MetaPc;
        return BeatSaberStore.Normalize(requestedStore);
    }

    public static string ResolveWorkerPluginBuild(string? directory, string? requestedStore = null)
    {
        var path = NormalizePath(directory);
        var store = InferStoreFromDirectory(path, requestedStore);
        var version = ReadSourceVersion(path, store);
        var supportedVersion = ResolveSupportedWorkerPluginVersion(version);
        if (!string.IsNullOrWhiteSpace(supportedVersion))
        {
            return "bs-" + supportedVersion;
        }

        if (ReadUnityPlayerVersion(path).StartsWith("6000.0.40", StringComparison.Ordinal))
        {
            return "bs-1.44.1";
        }

        return "bs-1.40.6";
    }

    private static List<SetupSourceCandidate> FindCandidates(
        IEnumerable<string>? steamLibraryCandidates,
        IEnumerable<string>? metaLibraryCandidates,
        IEnumerable<string>? bsManagerRootCandidates)
    {
        var candidates = new List<SetupSourceCandidate>();
        foreach (var library in steamLibraryCandidates ?? (OperatingSystem.IsWindows() ? GetSteamLibraryCandidates() : Array.Empty<string>()))
        {
            AddCandidate(candidates, BeatSaberStore.Steam, Path.Combine(NormalizePath(library), "steamapps", "common", "Beat Saber"));
        }

        foreach (var library in metaLibraryCandidates ?? (OperatingSystem.IsWindows() ? GetMetaLibraryCandidates() : Array.Empty<string>()))
        {
            AddCandidate(candidates, BeatSaberStore.MetaPc, Path.Combine(NormalizePath(library), "Software", MetaCanonicalName));
        }

        foreach (var root in bsManagerRootCandidates ?? (OperatingSystem.IsWindows() ? GetBsManagerRootCandidates() : Array.Empty<string>()))
        {
            AddBsManagerCandidates(candidates, root);
        }

        return candidates;
    }

    private static void AddCandidate(
        List<SetupSourceCandidate> candidates,
        string store,
        string? path,
        string? displayName = null,
        string sourceType = "Store",
        string? versionOverride = null)
    {
        var normalized = NormalizePath(path);
        if (!IsBeatSaberDirectory(normalized) || candidates.Any(candidate => PathsEqual(candidate.Path, normalized))) return;
        candidates.Add(CreateCandidate(store, normalized, displayName, sourceType, versionOverride));
    }

    private static SetupSourceCandidate CreateCandidate(
        string store,
        string path,
        string? displayName = null,
        string sourceType = "Store",
        string? versionOverride = null)
    {
        var missing = GetMissingPrerequisites(path);
        var version = string.IsNullOrWhiteSpace(versionOverride) ? ReadSourceVersion(path, store) : versionOverride;
        var compatibility = CheckVersionCompatibility(path, store, version);
        if (sourceType == "BSManager" && string.IsNullOrWhiteSpace(ResolveSupportedWorkerPluginVersion(version)))
        {
            compatibility = (false, "Supported Beat Saber versions are " +
                                     FormatSupportedWorkerPluginVersions() + " (found BSManager " +
                                     (string.IsNullOrWhiteSpace(version) ? "version" : version) + ")");
        }
        if (store == BeatSaberStore.MetaPc && !MetaSideloadedApps.IsEnabled())
        {
            missing.Add("Meta sideloaded apps");
        }
        if (!compatibility.Supported)
        {
            missing.Add(compatibility.Detail);
        }

        return new SetupSourceCandidate
        {
            SourceType = sourceType,
            Store = store,
            DisplayName = displayName ?? BeatSaberStore.DisplayName(store),
            Path = path,
            Version = version,
            Ready = true,
            RecorderReady = missing.Count == 0 && compatibility.Supported,
            VersionSupported = compatibility.Supported,
            VersionCompatibilityDetail = compatibility.Detail,
            MissingPrerequisites = missing
        };
    }

    private static void AddBsManagerCandidates(List<SetupSourceCandidate> candidates, string? root)
    {
        var normalizedRoot = NormalizePath(root);
        if (string.IsNullOrWhiteSpace(normalizedRoot)) return;

        if (IsBeatSaberDirectory(normalizedRoot))
        {
            AddBsManagerCandidate(candidates, normalizedRoot);
            return;
        }

        var instancesRoot = string.Equals(Path.GetFileName(normalizedRoot), "BSInstances", StringComparison.OrdinalIgnoreCase)
            ? normalizedRoot
            : Path.Combine(normalizedRoot, "BSInstances");
        if (!Directory.Exists(instancesRoot)) return;

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(instancesRoot))
            {
                AddBsManagerCandidate(candidates, directory);
            }
        }
        catch { }
    }

    private static void AddBsManagerCandidate(List<SetupSourceCandidate> candidates, string path)
    {
        var version = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var store = InferStoreFromDirectory(path);
        AddCandidate(candidates, store, path, "BSManager", "BSManager", version);
    }

    private static List<string> GetMissingPrerequisites(string path)
    {
        var missing = new List<string>();
        if (!File.Exists(Path.Combine(path, "IPA.exe")) || !File.Exists(Path.Combine(path, "winhttp.dll"))) missing.Add("BSIPA");
        if (!File.Exists(Path.Combine(path, "Plugins", "BeatLeader.dll"))) missing.Add("BeatLeader");
        return missing;
    }

    private static string ReadMetaVersion(string path, string store)
    {
        if (store != BeatSaberStore.MetaPc) return "";
        var libraryRoot = Directory.GetParent(Directory.GetParent(path)?.FullName ?? "")?.FullName;
        var manifest = libraryRoot == null ? null : Path.Combine(libraryRoot, "Manifests", MetaCanonicalName + ".json");
        try
        {
            if (manifest != null && File.Exists(manifest))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                return document.RootElement.TryGetProperty("version", out var version) ? version.GetString() ?? "" : "";
            }
        }
        catch { }
        return "";
    }

    private static string ReadSourceVersion(string path, string store)
    {
        var metaVersion = ReadMetaVersion(path, store);
        if (!string.IsNullOrWhiteSpace(metaVersion)) return metaVersion;

        var parent = Directory.GetParent(path);
        if (string.Equals(parent?.Name, "BSInstances", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return "";
    }

    private static string ResolveSupportedWorkerPluginVersion(string? version)
        => SupportedWorkerPluginVersions.FirstOrDefault(supported =>
               !string.IsNullOrWhiteSpace(version) &&
               version.StartsWith(supported, StringComparison.OrdinalIgnoreCase)) ?? "";

    private static string FormatSupportedWorkerPluginVersions()
        => string.Join(", ", SupportedWorkerPluginVersions.Take(SupportedWorkerPluginVersions.Length - 1)) +
           ", or " + SupportedWorkerPluginVersions[^1];

    private static (bool Supported, string Detail) CheckVersionCompatibility(string path, string store, string version)
    {
        if (store == BeatSaberStore.MetaPc &&
            !version.StartsWith("1.40.6", StringComparison.OrdinalIgnoreCase) &&
            !version.StartsWith("1.44.1", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Beat Saber 1.40.6 or 1.44.1 (found Meta " + (string.IsNullOrWhiteSpace(version) ? "version" : version) + ")");
        }

        var unityVersion = ReadUnityPlayerVersion(path);
        if (unityVersion.StartsWith("6000.", StringComparison.Ordinal) &&
            !string.Equals(ResolveWorkerPluginBuild(path, store), "bs-1.44.1", StringComparison.Ordinal))
        {
            return (false, "Beat Saber 1.40.6 or 1.44.1 (found an unsupported Unity 6 install)");
        }

        return (true, "");
    }

    private static string ReadUnityPlayerVersion(string path)
    {
        try { return FileVersionInfo.GetVersionInfo(Path.Combine(path, BeatSaberExecutableName)).ProductVersion ?? ""; }
        catch { return ""; }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> GetMetaLibraryCandidates()
    {
        var paths = new List<string>();
        AddUniquePath(paths, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Oculus", "Software");
        AddUniquePath(paths, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Oculus", "Software");
        using var config = OpenRegistrySubKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Oculus VR, LLC\Oculus\Config");
        AddUniquePath(paths, config?.GetValue("InitialAppLibrary") as string);
        using var libraries = OpenRegistrySubKey(Registry.CurrentUser, @"Software\Oculus VR, LLC\Oculus\Libraries");
        if (libraries != null)
        {
            foreach (var keyName in libraries.GetSubKeyNames())
            {
                using var key = libraries.OpenSubKey(keyName);
                AddUniquePath(paths, key?.GetValue("Path") as string);
            }
        }
        return paths;
    }

    private static IEnumerable<string> GetBsManagerRootCandidates()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return Path.Combine(userProfile, "BSManager");
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> GetSteamLibraryCandidates()
    {
        var paths = new List<string>();
        foreach (var steamRoot in GetSteamRootCandidates())
        {
            if (!Directory.Exists(steamRoot)) continue;
            AddUniquePath(paths, steamRoot);
            var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile)) continue;
            foreach (var libraryPath in ReadSteamLibraryFile(libraryFile)) AddUniquePath(paths, libraryPath);
        }
        return paths;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> GetSteamRootCandidates()
    {
        var paths = new List<string>();
        AddRegistrySteamRoot(paths, OpenRegistrySubKey(Registry.CurrentUser, @"Software\Valve\Steam"));
        AddRegistrySteamRoot(paths, OpenRegistrySubKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam"));
        AddRegistrySteamRoot(paths, OpenRegistrySubKey(Registry.LocalMachine, @"SOFTWARE\Valve\Steam"));
        AddUniquePath(paths, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        AddUniquePath(paths, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam");
        AddUniquePath(paths, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam");
        return paths;
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey? OpenRegistrySubKey(RegistryKey root, string name)
    {
        try { return root.OpenSubKey(name); } catch { return null; }
    }

    private static IEnumerable<string> ReadSteamLibraryFile(string libraryFile)
    {
        string[] lines;
        try { lines = File.ReadAllLines(libraryFile); } catch { yield break; }
        foreach (var line in lines)
        {
            var match = Regex.Match(line, "^\\s*\"(?:path|\\d+)\"\\s+\"(?<path>[^\"]+)\"");
            if (match.Success) yield return match.Groups["path"].Value.Replace(@"\\", @"\");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistrySteamRoot(List<string> paths, RegistryKey? key)
    {
        using (key)
        {
            if (key == null) return;
            AddUniquePath(paths, key.GetValue("SteamPath") as string);
            AddUniquePath(paths, key.GetValue("InstallPath") as string);
        }
    }

    private static void AddUniquePath(List<string> paths, string? path, params string[] children)
    {
        var normalized = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalized)) return;
        if (children.Length > 0) normalized = Path.Combine(new[] { normalized }.Concat(children).ToArray());
        normalized = NormalizePath(normalized);
        if (!string.IsNullOrWhiteSpace(normalized) && !paths.Any(existing => PathsEqual(existing, normalized))) paths.Add(normalized);
    }

    private static bool IsBeatSaberDirectory(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, BeatSaberExecutableName));
    private static bool PathsEqual(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && string.Equals(left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    private static string NormalizePath(string? path)
    {
        var trimmed = path?.Trim().Trim('"') ?? "";
        if (string.IsNullOrWhiteSpace(trimmed)) return "";
        try { return Path.GetFullPath(trimmed); } catch { return ""; }
    }
}
