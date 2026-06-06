using System.Runtime.InteropServices;

return await ProcessLoopbackCaptureCli.RunAsync(args).ConfigureAwait(false);

internal static class ProcessLoopbackCaptureCli
{
    private const int Success = 0;
    private const int IncludeMode = 0;
    private const int ExcludeMode = 1;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 3 || args.Length > 5)
        {
            Usage();
            return 2;
        }

        if (!uint.TryParse(args[0], out var processId) || processId == 0)
        {
            Usage();
            return 2;
        }

        var mode = args[1].Equals("includetree", StringComparison.OrdinalIgnoreCase)
            ? IncludeMode
            : args[1].Equals("excludetree", StringComparison.OrdinalIgnoreCase)
                ? ExcludeMode
                : -1;
        if (mode < 0)
        {
            Usage();
            return 2;
        }

        if (!ProcessAudioCaptureNative.PacIsSupported())
        {
            Console.Error.WriteLine("Windows process-loopback capture is not supported on this OS.");
            return 1;
        }

        var outputPath = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var error = ProcessAudioCaptureNative.PacStartCapture(
            processId,
            mode,
            outputPath,
            IntPtr.Zero,
            IntPtr.Zero,
            out var handle);
        if (error != Success)
        {
            Console.Error.WriteLine(GetLastErrorMessage(error));
            return 1;
        }

        var stopError = Success;
        try
        {
            Console.WriteLine("CaptureStartedUtc=" + DateTimeOffset.UtcNow.ToString("O"));
            Console.WriteLine("Capturing process-loopback audio for PID " + processId + ". Send q, quit, or stop on stdin to finish.");
            await WaitForStopAsync().ConfigureAwait(false);
        }
        finally
        {
            stopError = ProcessAudioCaptureNative.PacStopCapture(handle);
        }

        if (stopError != Success)
        {
            Console.Error.WriteLine(GetLastErrorMessage(stopError));
            return 1;
        }

        Console.WriteLine("Finished.");
        return 0;
    }

    private static async Task WaitForStopAsync()
    {
        while (true)
        {
            var line = await Console.In.ReadLineAsync().ConfigureAwait(false);
            if (line == null ||
                line.Equals("q", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
    }

    private static string GetLastErrorMessage(int code)
    {
        var buffer = new char[512];
        ProcessAudioCaptureNative.PacGetLastErrorMessage(buffer, buffer.Length);
        var message = new string(buffer).TrimEnd('\0').Trim();
        return string.IsNullOrWhiteSpace(message)
            ? "ProcessAudioCapture failed with error code " + code + "."
            : message;
    }

    private static void Usage()
    {
        Console.Error.WriteLine("Usage: ProcessLoopbackCapture <pid> <includetree|excludetree> <output.wav> [sampleRate] [channels]");
    }
}

internal static partial class ProcessAudioCaptureNative
{
    [LibraryImport("ProcessAudioCapture.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PacIsSupported();

    [LibraryImport("ProcessAudioCapture.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int PacStartCapture(
        uint processId,
        int mode,
        string? outputPath,
        IntPtr levelCallback,
        IntPtr userData,
        out IntPtr captureHandle);

    [LibraryImport("ProcessAudioCapture.dll")]
    public static partial int PacStopCapture(IntPtr captureHandle);

    [LibraryImport("ProcessAudioCapture.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial void PacGetLastErrorMessage(
        [Out] char[] buffer,
        int bufferLength);
}
