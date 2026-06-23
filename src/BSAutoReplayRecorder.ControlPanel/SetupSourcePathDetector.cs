using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BSAutoReplayRecorder.ControlPanel;

public static class SetupSourcePathDetector
{
    private const string BeatSaberExecutableName = "Beat Saber.exe";

    public static SetupSourcePathReport Detect(string? configuredSourceBeatSaberPath)
    {
        return Detect(configuredSourceBeatSaberPath, steamLibraryCandidates: null);
    }

    public static SetupSourcePathReport Detect(
        string? configuredSourceBeatSaberPath,
        IEnumerable<string>? steamLibraryCandidates)
    {
        var configured = NormalizePath(configuredSourceBeatSaberPath);
        var configuredReady = IsBeatSaberDirectory(configured);
        var detected = FindSteamBeatSaberPath(steamLibraryCandidates);
        var detectedReady = IsBeatSaberDirectory(detected);
        var effective = configuredReady ? configured : (detectedReady ? detected : "");

        var report = new SetupSourcePathReport
        {
            ConfiguredSourceBeatSaberPath = configured,
            ConfiguredSourceReady = configuredReady,
            DetectedSourceBeatSaberPath = detected,
            DetectedSourceReady = detectedReady,
            EffectiveSourceBeatSaberPath = effective
        };

        if (configuredReady)
        {
            report.Status = "Ready";
            report.Summary = "Configured Beat Saber source is ready.";
        }
        else if (detectedReady)
        {
            report.Status = "Detected";
            report.Summary = "Detected a Steam Beat Saber install.";
        }
        else if (!string.IsNullOrWhiteSpace(configured))
        {
            report.Status = "Missing";
            report.Summary = "Configured source does not contain " + BeatSaberExecutableName + ".";
        }

        return report;
    }

    private static string FindSteamBeatSaberPath(IEnumerable<string>? steamLibraryCandidates)
    {
        var libraries = steamLibraryCandidates ??
            (OperatingSystem.IsWindows() ? GetSteamLibraryCandidates() : Array.Empty<string>());

        foreach (var libraryPath in libraries)
        {
            var normalizedLibraryPath = NormalizePath(libraryPath);
            if (string.IsNullOrWhiteSpace(normalizedLibraryPath))
            {
                continue;
            }

            var candidate = Path.Combine(normalizedLibraryPath, "steamapps", "common", "Beat Saber");
            if (IsBeatSaberDirectory(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return "";
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> GetSteamLibraryCandidates()
    {
        var paths = new List<string>();
        foreach (var steamRoot in GetSteamRootCandidates())
        {
            if (!Directory.Exists(steamRoot))
            {
                continue;
            }

            AddUniquePath(paths, steamRoot);
            var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
            {
                continue;
            }

            foreach (var libraryPath in ReadSteamLibraryFile(libraryFile))
            {
                AddUniquePath(paths, libraryPath);
            }
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
        AddUniquePath(paths, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Steam");
        AddUniquePath(paths, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SteamLibrary");
        AddUniquePath(paths, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "SteamLibrary");
        return paths;
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey? OpenRegistrySubKey(RegistryKey root, string name)
    {
        try
        {
            return root.OpenSubKey(name);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> ReadSteamLibraryFile(string libraryFile)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(libraryFile);
        }
        catch
        {
            yield break;
        }

        foreach (var line in lines)
        {
            var match = Regex.Match(line, "^\\s*\"(?:path|\\d+)\"\\s+\"(?<path>[^\"]+)\"");
            if (!match.Success)
            {
                continue;
            }

            yield return match.Groups["path"].Value.Replace(@"\\", @"\");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistrySteamRoot(List<string> paths, RegistryKey? key)
    {
        using (key)
        {
            if (key == null)
            {
                return;
            }

            AddUniquePath(paths, key.GetValue("SteamPath") as string);
            AddUniquePath(paths, key.GetValue("InstallPath") as string);
        }
    }

    private static void AddUniquePath(List<string> paths, string? path, params string[] children)
    {
        var normalized = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (children.Length > 0)
        {
            normalized = Path.Combine(new[] { normalized }.Concat(children).ToArray());
        }

        normalized = NormalizePath(normalized);
        if (!string.IsNullOrWhiteSpace(normalized) &&
            !paths.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            paths.Add(normalized);
        }
    }

    private static bool IsBeatSaberDirectory(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               File.Exists(Path.Combine(path, BeatSaberExecutableName));
    }

    private static string NormalizePath(string? path)
    {
        var trimmed = path?.Trim().Trim('"') ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "";
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return "";
        }
    }
}
