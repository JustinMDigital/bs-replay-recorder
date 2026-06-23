using System.Diagnostics;
using System.Windows.Forms;

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
            MessageBox.Show(
                "The packaged Replay Recorder app was not found:\n\n" + targetPath +
                "\n\nRun npm run electron:pack, or keep this launcher beside the dist folder.",
                "Replay Recorder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
}
