using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BSAutoReplayRecorder.ControlPanel;

internal sealed class InstanceBaselineScanner
{
    private const string MissingFingerprint = "<missing>";
    private const string RecorderSettingsPath = "UserData/BSAutoReplayRecorder/settings.json";

    private static readonly string[] RequiredRelativePaths =
    {
        "Beat Saber.exe",
        "Beat Saber_Data/Managed/IPA.Loader.dll",
        "Plugins/BeatLeader.dll",
        "Plugins/BSAutoReplayRecorder.Plugin.dll",
        "Libs/BSAutoReplayRecorder.Core.dll",
        RecorderSettingsPath
    };

    private static readonly string[] OptionalExactRelativePaths =
    {
        "UserData/PlayerData.dat",
        "UserData/settings.cfg",
        "UserData/Beat Saber IPA.json"
    };

    private static readonly string[] DllInventoryDirectories =
    {
        "Plugins",
        "Libs"
    };

    private static readonly string[] JsonConfigDirectories =
    {
        "UserData/BeatLeader",
        "UserData/Camera2",
        "UserData/BSIPA"
    };

    private static readonly HashSet<string> IgnoredRecorderSettings =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ControlPanelWorker.WorkerId",
            "ControlPanelWorker.WorkerName",
            "ControlPanelWorker.PreferredInstanceIndex",
            "RecorderHost.BaseUrl",
            "RecorderHost.OutputDirectory",
            "RecorderHost.AudioDeviceName",
            "RecorderHost.TargetProcessId",
            "WindowPlacement.InstanceIndex"
        };

    public InstanceBaselineReport Scan(IReadOnlyList<WorkerInstanceRecord> instances)
    {
        var scans = instances
            .OrderBy(instance => instance.Index)
            .Select(ScanInstance)
            .ToList();
        var baseline = scans.FirstOrDefault();
        var report = new InstanceBaselineReport
        {
            CheckedAtUtc = DateTimeOffset.UtcNow,
            BaselineInstanceIndex = baseline?.Index ?? 0,
            BaselineInstanceName = baseline?.Name ?? ""
        };

        if (baseline == null)
        {
            report.Status = "Missing";
            report.Summary = "No Beat Saber instances are configured.";
            return report;
        }

        foreach (var scan in scans)
        {
            CompareToBaseline(baseline, scan);
            report.Instances.Add(scan.ToRecord());
        }

        if (report.Instances.Any(instance => string.Equals(instance.Status, "Missing", StringComparison.OrdinalIgnoreCase)))
        {
            report.Status = "Missing";
        }
        else if (report.Instances.Any(instance => string.Equals(instance.Status, "Mismatch", StringComparison.OrdinalIgnoreCase)))
        {
            report.Status = "Mismatch";
        }
        else
        {
            report.Status = "Matched";
        }

        report.Summary = CreateSummary(report);
        return report;
    }

    private static InstanceScan ScanInstance(WorkerInstanceRecord instance)
    {
        var directory = ResolveInstanceDirectory(instance);
        var scan = new InstanceScan
        {
            Index = instance.Index,
            Name = string.IsNullOrWhiteSpace(instance.Name)
                ? "Instance " + (instance.Index + 1)
                : instance.Name,
            Directory = directory
        };

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            scan.Issues.Add("Instance folder was not found.");
            scan.DirectoryMissing = true;
            return scan;
        }

        var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in RequiredRelativePaths.Concat(OptionalExactRelativePaths))
        {
            relativePaths.Add(NormalizeRelativePath(relativePath));
        }

        foreach (var inventoryDirectory in DllInventoryDirectories)
        {
            AddFiles(relativePaths, directory, inventoryDirectory, "*.dll");
        }

        foreach (var configDirectory in JsonConfigDirectories)
        {
            AddFiles(relativePaths, directory, configDirectory, "*.json");
        }

        foreach (var relativePath in relativePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var required = RequiredRelativePaths.Any(requiredPath =>
                string.Equals(NormalizeRelativePath(requiredPath), relativePath, StringComparison.OrdinalIgnoreCase));

            if (!File.Exists(fullPath))
            {
                if (required)
                {
                    scan.Fingerprints[relativePath] = MissingFingerprint;
                    scan.Issues.Add("Missing " + relativePath + ".");
                }

                continue;
            }

            scan.Fingerprints[relativePath] = HashBaselineFile(fullPath, relativePath);
        }

        return scan;
    }

    private static void AddFiles(HashSet<string> relativePaths, string rootDirectory, string relativeDirectory, string searchPattern)
    {
        var fullDirectory = Path.Combine(rootDirectory, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(fullDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(fullDirectory, searchPattern, SearchOption.TopDirectoryOnly))
        {
            relativePaths.Add(NormalizeRelativePath(Path.GetRelativePath(rootDirectory, file)));
        }
    }

    private static void CompareToBaseline(InstanceScan baseline, InstanceScan scan)
    {
        if (scan.DirectoryMissing)
        {
            return;
        }

        if (baseline.DirectoryMissing)
        {
            if (scan.Index != baseline.Index)
            {
                scan.Issues.Add("Baseline instance folder is missing.");
            }

            return;
        }

        var comparedPaths = new HashSet<string>(baseline.Fingerprints.Keys, StringComparer.OrdinalIgnoreCase);
        comparedPaths.UnionWith(scan.Fingerprints.Keys);

        foreach (var relativePath in comparedPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            baseline.Fingerprints.TryGetValue(relativePath, out var baselineFingerprint);
            scan.Fingerprints.TryGetValue(relativePath, out var scanFingerprint);

            if (string.Equals(baselineFingerprint, scanFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (baselineFingerprint == null)
            {
                scan.Issues.Add("Extra " + relativePath + ".");
            }
            else if (scanFingerprint == null || string.Equals(scanFingerprint, MissingFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                scan.Issues.Add("Missing " + relativePath + ".");
            }
            else if (string.Equals(baselineFingerprint, MissingFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                scan.Issues.Add("Baseline is missing " + relativePath + ".");
            }
            else
            {
                scan.Issues.Add("Changed " + relativePath + ".");
            }
        }
    }

    private static string HashBaselineFile(string path, string relativePath)
    {
        if (string.Equals(relativePath, RecorderSettingsPath, StringComparison.OrdinalIgnoreCase))
        {
            var normalized = NormalizeRecorderSettings(File.ReadAllText(path));
            return HashBytes(Encoding.UTF8.GetBytes(normalized));
        }

        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant() + ":" + stream.Length;
    }

    private static string NormalizeRecorderSettings(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteCanonicalJson(writer, document.RootElement, "");
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return json.Trim();
        }
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var propertyPath = string.IsNullOrEmpty(path) ? property.Name : path + "." + property.Name;
                    if (IgnoredRecorderSettings.Contains(propertyPath))
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value, propertyPath);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item, path);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string HashBytes(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ":" + bytes.Length;
    }

    private static string ResolveInstanceDirectory(WorkerInstanceRecord instance)
    {
        return NormalizePath(instance.GameDirectory) ?? NormalizePath(instance.LaunchDirectory) ?? "";
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch
        {
            return path.Trim().Trim('"');
        }
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    private static string CreateSummary(InstanceBaselineReport report)
    {
        var issueCount = report.Instances.Sum(instance => instance.Issues.Count);
        return report.Status switch
        {
            "Matched" => "All configured instances match the baseline.",
            "Missing" => issueCount + " missing baseline item" + (issueCount == 1 ? "" : "s") + ".",
            "Mismatch" => issueCount + " baseline difference" + (issueCount == 1 ? "" : "s") + ".",
            _ => "Baseline has not been checked."
        };
    }

    private sealed class InstanceScan
    {
        public int Index { get; set; }

        public string Name { get; set; } = "";

        public string Directory { get; set; } = "";

        public bool DirectoryMissing { get; set; }

        public Dictionary<string, string> Fingerprints { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<string> Issues { get; } = new List<string>();

        public InstanceBaselineRecord ToRecord()
        {
            return new InstanceBaselineRecord
            {
                Index = Index,
                Name = Name,
                Directory = Directory,
                Status = DirectoryMissing ? "Missing" : Issues.Count == 0 ? "Ok" : "Mismatch",
                CheckedFileCount = Fingerprints.Count(item => !string.Equals(item.Value, MissingFingerprint, StringComparison.OrdinalIgnoreCase)),
                Issues = Issues.ToList()
            };
        }
    }
}
