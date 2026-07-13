using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var rootDirectory = AppContext.BaseDirectory;
        var targetPath = Path.Combine(
            rootDirectory,
            "dist",
            "electron",
            "win-unpacked",
            "Replay Recorder.exe");

        if (!File.Exists(targetPath))
        {
            MessageBoxW(
                IntPtr.Zero,
                "The packaged Replay Recorder app was not found:\n\n" + targetPath +
                "\n\nRun npm run electron:pack, or keep this launcher beside the dist folder.",
                "Replay Recorder",
                0x00000010);
            return 1;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = targetPath,
            WorkingDirectory = Path.GetDirectoryName(targetPath) ?? rootDirectory,
            UseShellExecute = true
        };

        startInfo.ArgumentList.Add("--repo-root");
        startInfo.ArgumentList.Add(rootDirectory);

        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
        return 0;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr windowHandle, string text, string caption, uint type);
}
