using System.Diagnostics;

namespace BSAutoReplayRecorder.ControlPanel;

internal interface IStackShutdownLauncher
{
    void StartStopScript(bool stopGames);
}

internal sealed class StopScriptShutdownLauncher : IStackShutdownLauncher
{
    public void StartStopScript(bool stopGames)
    {
        var scriptPath = ResolveStopScriptPath()
                         ?? throw new InvalidOperationException("Stop script was not found. Expected scripts\\launcher\\Stop-ReplayRecorder.ps1 under the repo root.");
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        if (stopGames)
        {
            startInfo.ArgumentList.Add("-StopGames");
        }

        Process.Start(startInfo);
    }

    private static string? ResolveStopScriptPath()
    {
        foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(root);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "scripts", "launcher", "Stop-ReplayRecorder.ps1");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }
}
