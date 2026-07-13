using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BSAutoReplayRecorder.ControlPanel;

public static class SetupSourcePathDetector
{
    private const string BeatSaberExecutableName = "Beat Saber.exe";
    private const string MetaCanonicalName = "hyperbolic-magnetism-beat-saber";

    public static SetupSourcePathReport Detect(string? configuredSourceBeatSaberPath, string? configuredStore = null)
        => Detect(configuredSourceBeatSaberPath, steamLibraryCandidates: null, metaLibraryCandidates: null, configuredStore);

    // Retained for the existing deterministic Steam detector tests.
    public static SetupSourcePathReport Detect(string? configuredSourceBeatSaberPath, IEnumerable<string>? steamLibraryCandidates)
        => Detect(configuredSourceBeatSaberPath, steamLibraryCandidates, metaLibraryCandidates: Array.Empty<string>());

    public static SetupSourcePathReport Detect(
        string? configuredSourceBeatSaberPath,
        IEnumerable<string>? steamLibraryCandidates,
        IEnumerable<string>? metaLibraryCandidates,
        string? configuredStore = null)
    {
        var configured = NormalizePath(configuredSourceBeatSaberPath);
        var candidates = FindCandidates(steamLibraryCandidates, metaLibraryCandidates);
        var configuredCandidate = candidates.FirstOrDefault(candidate => PathsEqual(candidate.Path, configured));
        var configuredReady = IsBeatSaberDirectory(configured);
        var configuredStoreKind = configuredCandidate?.Store ??
                                  InferStoreFromDirectory(configured, configuredStore);
        var detected = candidates.FirstOrDefault();
        var report = new SetupSourcePathReport
        {
            ConfiguredSourceBeatSaberPath = configured,
            ConfiguredSourceStore = configuredStoreKind,
            ConfiguredSourceReady = configuredReady,
            DetectedSourceBeatSaberPath = detected?.Path ?? "",
            DetectedSourceReady = detected?.Ready ?? false,
            DetectedSources = candidates
        };

        if (configuredReady)
        {
            report.Status = "Ready";
            report.Summary = "Configured " + BeatSaberStore.DisplayName(configuredStoreKind) + " Beat Saber source is ready.";
            report.EffectiveSourceBeatSaberPath = configured;
            report.EffectiveSourceStore = configuredStoreKind;
        }
        else if (candidates.Count == 1)
        {
            report.Status = "Detected";
            report.Summary = "Detected a " + candidates[0].DisplayName + " Beat Saber install.";
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
        var requested = BeatSaberStore.Normalize(requestedStore);
        if (requested != BeatSaberStore.Unknown) return requested;
        if (File.Exists(Path.Combine(normalized, "Beat Saber_Data", "Plugins", "x86_64", "steam_api64.dll"))) return BeatSaberStore.Steam;
        if (normalized.Contains(MetaCanonicalName, StringComparison.OrdinalIgnoreCase)) return BeatSaberStore.MetaPc;
        return BeatSaberStore.Unknown;
    }

    private static List<SetupSourceCandidate> FindCandidates(
        IEnumerable<string>? steamLibraryCandidates,
        IEnumerable<string>? metaLibraryCandidates)
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

        return candidates;
    }

    private static void AddCandidate(List<SetupSourceCandidate> candidates, string store, string? path)
    {
        var normalized = NormalizePath(path);
        if (!IsBeatSaberDirectory(normalized) || candidates.Any(candidate => PathsEqual(candidate.Path, normalized))) return;
        var missing = GetMissingPrerequisites(normalized);
        candidates.Add(new SetupSourceCandidate
        {
            Store = store,
            DisplayName = BeatSaberStore.DisplayName(store),
            Path = normalized,
            Version = ReadMetaVersion(normalized, store),
            Ready = true,
            RecorderReady = missing.Count == 0,
            MissingPrerequisites = missing
        });
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
