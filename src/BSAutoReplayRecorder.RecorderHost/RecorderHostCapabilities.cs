namespace BSAutoReplayRecorder.RecorderHost;

internal static class RecorderHostCapabilities
{
    public static RecorderHostCapabilitiesResponse Create(RecorderHostSettings settings)
    {
        var ffmpegResolver = new FfmpegExecutableResolver();
        var ffmpegPath = ffmpegResolver.Resolve(settings.FfmpegPath) ?? "";
        var processLoopbackPath = ResolveProcessLoopbackCapturePath(settings) ?? "";
        var wgcPath = ResolveWindowsGraphicsCapturePath(settings) ?? "";
        var wgcProbe = ProbeWindowsGraphicsCapture(wgcPath);
        return new RecorderHostCapabilitiesResponse
        {
            Status = "ok",
            FfmpegPath = ffmpegPath,
            ProcessLoopbackCapturePath = processLoopbackPath,
            WindowsGraphicsCapturePath = wgcPath,
            CaptureEngines =
            {
                new CaptureEngineCapability
                {
                    Name = "FFmpegDdagrab",
                    Supported = !string.IsNullOrWhiteSpace(ffmpegPath),
                    Status = string.IsNullOrWhiteSpace(ffmpegPath)
                        ? "FFmpeg was not found"
                        : "FFmpeg desktop capture ready"
                },
                new CaptureEngineCapability
                {
                    Name = "WindowsGraphicsCapture",
                    Supported = !string.IsNullOrWhiteSpace(ffmpegPath) &&
                                !string.IsNullOrWhiteSpace(wgcPath) &&
                                wgcProbe.Supported,
                    Status = string.IsNullOrWhiteSpace(wgcPath)
                        ? "Experimental, buggy: WindowsGraphicsCapture.exe was not found"
                        : string.IsNullOrWhiteSpace(ffmpegPath)
                            ? "Experimental, buggy: FFmpeg was not found"
                            : "Experimental, buggy: " + wgcProbe.Status
                }
            },
            AudioModes =
            {
                new AudioModeCapability
                {
                    Name = "None",
                    Supported = true,
                    Status = "Audio disabled"
                },
                new AudioModeCapability
                {
                    Name = "ProcessLoopback",
                    Supported = !string.IsNullOrWhiteSpace(processLoopbackPath),
                    Status = string.IsNullOrWhiteSpace(processLoopbackPath)
                        ? "ProcessLoopbackCapture.exe was not found"
                        : "ProcessLoopback audio helper ready"
                }
            }
        };
    }

    private static (bool Supported, string Status) ProbeWindowsGraphicsCapture(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return (false, "WindowsGraphicsCapture.exe was not found");
        }

        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "probe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            if (!process.Start())
            {
                return (false, "WindowsGraphicsCapture probe did not start");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return (false, "WindowsGraphicsCapture probe timed out");
            }

            var detail = string.IsNullOrWhiteSpace(output)
                ? error.Trim()
                : output.Trim();
            return process.ExitCode == 0
                ? (true, string.IsNullOrWhiteSpace(detail) ? "Windows Graphics Capture ready" : detail)
                : (false, string.IsNullOrWhiteSpace(detail) ? "Windows Graphics Capture unsupported" : detail);
        }
        catch (Exception ex)
        {
            return (false, "WindowsGraphicsCapture probe failed: " + ex.Message);
        }
    }

    private static string? ResolveWindowsGraphicsCapturePath(RecorderHostSettings settings)
    {
        foreach (var candidate in EnumerateWindowsGraphicsCapturePathCandidates(settings))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateWindowsGraphicsCapturePathCandidates(RecorderHostSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WindowsGraphicsCapturePath))
        {
            yield return settings.WindowsGraphicsCapturePath;
        }

        yield return Path.Combine(Environment.CurrentDirectory, "tools", "WindowsGraphicsCapture.Managed", "bin", "Release", "net10.0-windows10.0.20348.0", "win-x64", "WindowsGraphicsCapture.exe");
        yield return Path.Combine(Environment.CurrentDirectory, "tools", "WindowsGraphicsCapture.Managed", "bin", "Debug", "net10.0-windows10.0.20348.0", "win-x64", "WindowsGraphicsCapture.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "WindowsGraphicsCapture.exe");
        yield return "WindowsGraphicsCapture.exe";
    }

    private static string? ResolveProcessLoopbackCapturePath(RecorderHostSettings settings)
    {
        foreach (var candidate in EnumerateProcessLoopbackCapturePathCandidates(settings))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateProcessLoopbackCapturePathCandidates(RecorderHostSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ProcessLoopbackCapturePath))
        {
            yield return settings.ProcessLoopbackCapturePath;
        }

        yield return Path.Combine(Environment.CurrentDirectory, "tools", "ProcessLoopbackCapture", "x64", "Release", "ProcessLoopbackCapture.exe");
        yield return Path.Combine(Environment.CurrentDirectory, "tools", "ProcessLoopbackCapture", "x64", "Debug", "ProcessLoopbackCapture.exe");
        yield return Path.Combine(Environment.CurrentDirectory, "tools", "ProcessLoopbackCapture.Managed", "bin", "Release", "net10.0-windows10.0.20348.0", "win-x64", "ProcessLoopbackCapture.exe");
        yield return Path.Combine(Environment.CurrentDirectory, "tools", "ProcessLoopbackCapture.Managed", "bin", "Debug", "net10.0-windows10.0.20348.0", "win-x64", "ProcessLoopbackCapture.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "ProcessLoopbackCapture.exe");
        yield return "ProcessLoopbackCapture.exe";
    }
}
