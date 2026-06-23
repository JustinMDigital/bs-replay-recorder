using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

return await WindowsGraphicsCaptureCli.RunAsync(args).ConfigureAwait(false);

internal static class WindowsGraphicsCaptureCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || HasArg(args, "--help") || HasArg(args, "-h"))
        {
            PrintUsage();
            return 0;
        }

        if (string.Equals(args[0], "probe", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(GraphicsCaptureSession.IsSupported()
                ? "WindowsGraphicsCapture=Supported"
                : "WindowsGraphicsCapture=Unsupported");
            return GraphicsCaptureSession.IsSupported() ? 0 : 2;
        }

        if (!string.Equals(args[0], "capture", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Unknown command: " + args[0]);
            PrintUsage();
            return 2;
        }

        var options = CaptureOptions.Parse(args.Skip(1).ToArray());
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new InvalidOperationException("Windows Graphics Capture is not supported on this Windows session.");
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        _ = Task.Run(() => WatchStandardInputForQuit(cancellation), CancellationToken.None);

        await using var capture = new WindowsGraphicsCaptureRunner(options);
        await capture.RunAsync(cancellation.Token).ConfigureAwait(false);
        return 0;
    }

    private static bool HasArg(string[] args, string name)
    {
        return args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
    }

    private static void WatchStandardInputForQuit(CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var line = Console.ReadLine();
                if (line == null ||
                    string.Equals(line.Trim(), "q", StringComparison.OrdinalIgnoreCase))
                {
                    cancellation.Cancel();
                    return;
                }
            }
        }
        catch
        {
            cancellation.Cancel();
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  WindowsGraphicsCapture probe");
        Console.Error.WriteLine("  WindowsGraphicsCapture capture --window-title <title> [--process-id <pid>] --ffmpeg <path> --output <path> [--fps 60] [--width 1920] [--height 1080] [--encoder h264_nvenc] [--bitrate-kbps 12000] [--format mkv] [--quality Performance|Balanced|Quality]");
    }
}

internal sealed class WindowsGraphicsCaptureRunner : IAsyncDisposable
{
    private readonly CaptureOptions _options;
    private readonly SemaphoreSlim _frameGate = new SemaphoreSlim(1, 1);
    private readonly object _latestFrameSync = new object();
    private readonly TaskCompletionSource _firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private CapturedFrame? _latestFrame;
    private IDirect3DDevice? _device;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private Process? _ffmpegProcess;
    private SizeInt32 _captureSize;
    private long _writtenFrames;

    public WindowsGraphicsCaptureRunner(CaptureOptions options)
    {
        _options = options;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var hwnd = _options.ProcessId.HasValue
            ? WindowFinder.FindWindowByProcessId(_options.ProcessId.Value)
            : WindowFinder.FindWindowByTitle(_options.WindowTitle);
        if (hwnd == IntPtr.Zero)
        {
            var description = _options.ProcessId.HasValue
                ? "process id " + _options.ProcessId.Value.ToString(CultureInfo.InvariantCulture)
                : _options.WindowTitle;
            throw new InvalidOperationException("Window was not found: " + description);
        }

        _device = Direct3DDeviceFactory.Create();
        var item = GraphicsCaptureItemFactory.CreateForWindow(hwnd);
        _captureSize = item.Size;
        if (_captureSize.Width <= 0 || _captureSize.Height <= 0)
        {
            throw new InvalidOperationException("Capture item has an invalid size.");
        }

        _ffmpegProcess = StartFfmpegProcess(_captureSize);
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            _captureSize);
        _session = _framePool.CreateCaptureSession(item);
        _session.IsCursorCaptureEnabled = false;
        _framePool.FrameArrived += HandleFrameArrived;

        Console.WriteLine("CaptureSize=" + _captureSize.Width + "x" + _captureSize.Height);
        _session.StartCapture();

        var writerTask = Task.Run(() => WriteFramesAtTargetRateAsync(cancellationToken), CancellationToken.None);
        try
        {
            try
            {
                await WaitForCancellationOrExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
        finally
        {
            try
            {
                await writerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            await StopFfmpegAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        (_session as IDisposable)?.Dispose();
        (_framePool as IDisposable)?.Dispose();
        _session = null;
        _framePool = null;
        _device = null;
        _frameGate.Dispose();
        lock (_latestFrameSync)
        {
            _latestFrame = null;
        }

        if (_ffmpegProcess != null)
        {
            await StopFfmpegAsync().ConfigureAwait(false);
            _ffmpegProcess.Dispose();
            _ffmpegProcess = null;
        }
    }

    private async void HandleFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (!await _frameGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null)
            {
                return;
            }

            if (frame.ContentSize.Width != _captureSize.Width ||
                frame.ContentSize.Height != _captureSize.Height)
            {
                _captureSize = frame.ContentSize;
                sender.Recreate(_device!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _captureSize);
                return;
            }

            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface)
                .AsTask()
                .ConfigureAwait(false);
            using var converted = SoftwareBitmap.Convert(
                bitmap,
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);
            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            var buffer = new Windows.Storage.Streams.Buffer((uint)(width * height * 4));
            converted.CopyToBuffer(buffer);
            var bytes = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer))
            {
                reader.ReadBytes(bytes);
            }

            lock (_latestFrameSync)
            {
                _latestFrame = new CapturedFrame(bytes, width, height);
            }

            _firstFrame.TrySetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("WGC frame capture failed: " + ex.Message);
        }
        finally
        {
            _frameGate.Release();
        }
    }

    private async Task WriteFramesAtTargetRateAsync(CancellationToken cancellationToken)
    {
        await _firstFrame.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        var reportedStarted = false;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / _options.Fps));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            CapturedFrame? frame;
            lock (_latestFrameSync)
            {
                frame = _latestFrame;
            }

            if (frame == null)
            {
                continue;
            }

            if (_ffmpegProcess == null || _ffmpegProcess.HasExited)
            {
                throw new InvalidOperationException("FFmpeg exited while Windows Graphics Capture was still running.");
            }

            if (!reportedStarted)
            {
                Console.WriteLine("CaptureStartedUtc=" + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                reportedStarted = true;
            }

            await _ffmpegProcess.StandardInput.BaseStream
                .WriteAsync(frame.Bytes, cancellationToken)
                .ConfigureAwait(false);
            _writtenFrames++;
        }
    }

    private async Task WaitForCancellationOrExitAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_ffmpegProcess?.HasExited == true)
            {
                throw new InvalidOperationException("FFmpeg exited during Windows Graphics Capture. ExitCode=" + _ffmpegProcess.ExitCode + ".");
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    private Process StartFfmpegProcess(SizeInt32 captureSize)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_options.OutputPath)) ?? ".");
        var args = BuildFfmpegArguments(captureSize);
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.FfmpegPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, eventArgs) => LogFfmpegLine(eventArgs.Data, isError: false);
        process.ErrorDataReceived += (_, eventArgs) => LogFfmpegLine(eventArgs.Data, isError: true);
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start FFmpeg for Windows Graphics Capture.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private string BuildFfmpegArguments(SizeInt32 captureSize)
    {
        var filter = captureSize.Width == _options.Width && captureSize.Height == _options.Height
            ? ""
            : " -vf scale=" + _options.Width.ToString(CultureInfo.InvariantCulture) +
              ":" + _options.Height.ToString(CultureInfo.InvariantCulture);
        var containerFlags = string.Equals(_options.Format, "mp4", StringComparison.OrdinalIgnoreCase)
            ? " -movflags +faststart"
            : "";
        return "-hide_banner -y -f rawvideo -pixel_format bgra -video_size " +
               captureSize.Width.ToString(CultureInfo.InvariantCulture) + "x" +
               captureSize.Height.ToString(CultureInfo.InvariantCulture) +
               " -framerate " + _options.Fps.ToString(CultureInfo.InvariantCulture) +
               " -i pipe:0" + filter +
               " -map 0:v:0 -c:v " + Quote(_options.Encoder) +
               " -preset " + Quote(_options.EncoderPreset) +
               " -b:v " + _options.BitrateKbps.ToString(CultureInfo.InvariantCulture) +
               "k -pix_fmt yuv420p" + containerFlags +
               " " + Quote(_options.OutputPath);
    }

    private async Task StopFfmpegAsync()
    {
        if (_ffmpegProcess == null)
        {
            return;
        }

        try
        {
            _ffmpegProcess.StandardInput.Close();
        }
        catch
        {
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await _ffmpegProcess.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!_ffmpegProcess.HasExited)
            {
                _ffmpegProcess.Kill(entireProcessTree: true);
                await _ffmpegProcess.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        if (_ffmpegProcess.ExitCode != 0 && _writtenFrames > 0)
        {
            throw new InvalidOperationException("FFmpeg for Windows Graphics Capture failed. ExitCode=" + _ffmpegProcess.ExitCode + ".");
        }
    }

    private static void LogFfmpegLine(string? line, bool isError)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (isError)
        {
            Console.Error.WriteLine("ffmpeg: " + line);
        }
        else
        {
            Console.WriteLine("ffmpeg: " + line);
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}

internal sealed class CapturedFrame
{
    public CapturedFrame(byte[] bytes, int width, int height)
    {
        Bytes = bytes;
        Width = width;
        Height = height;
    }

    public byte[] Bytes { get; }

    public int Width { get; }

    public int Height { get; }

}

internal sealed class CaptureOptions
{
    public string WindowTitle { get; private init; } = "Beat Saber";

    public int? ProcessId { get; private init; }

    public string FfmpegPath { get; private init; } = "ffmpeg";

    public string OutputPath { get; private init; } = "";

    public int Fps { get; private init; } = 60;

    public int Width { get; private init; } = 1920;

    public int Height { get; private init; } = 1080;

    public string Encoder { get; private init; } = "h264_nvenc";

    public int BitrateKbps { get; private init; } = 12000;

    public string Format { get; private init; } = "mkv";

    public string Quality { get; private init; } = "Balanced";

    public string EncoderPreset => ResolveEncoderPreset(Encoder, Quality);

    public static CaptureOptions Parse(string[] args)
    {
        string? windowTitle = null;
        int? processId = null;
        string? ffmpeg = null;
        string? output = null;
        var fps = 60;
        var width = 1920;
        var height = 1080;
        var encoder = "h264_nvenc";
        var bitrate = 12000;
        var format = "mkv";
        var quality = "Balanced";

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            string ReadValue()
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for " + arg + ".");
                }

                index++;
                return args[index];
            }

            switch (arg.ToLowerInvariant())
            {
                case "--window-title":
                    windowTitle = ReadValue();
                    break;
                case "--process-id":
                    processId = int.Parse(ReadValue(), CultureInfo.InvariantCulture);
                    break;
                case "--ffmpeg":
                    ffmpeg = ReadValue();
                    break;
                case "--output":
                    output = ReadValue();
                    break;
                case "--fps":
                    fps = int.Parse(ReadValue(), CultureInfo.InvariantCulture);
                    break;
                case "--width":
                    width = int.Parse(ReadValue(), CultureInfo.InvariantCulture);
                    break;
                case "--height":
                    height = int.Parse(ReadValue(), CultureInfo.InvariantCulture);
                    break;
                case "--encoder":
                    encoder = ReadValue();
                    break;
                case "--bitrate-kbps":
                    bitrate = int.Parse(ReadValue(), CultureInfo.InvariantCulture);
                    break;
                case "--format":
                    format = ReadValue();
                    break;
                case "--quality":
                    quality = ReadValue();
                    break;
                default:
                    throw new ArgumentException("Unknown option: " + arg);
            }
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("--output is required.");
        }

        return new CaptureOptions
        {
            WindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? "Beat Saber" : windowTitle,
            ProcessId = processId.HasValue && processId.Value > 0 ? processId : null,
            FfmpegPath = string.IsNullOrWhiteSpace(ffmpeg) ? "ffmpeg" : ffmpeg,
            OutputPath = output,
            Fps = Math.Clamp(fps, 1, 240),
            Width = Math.Clamp(width, 320, 16384),
            Height = Math.Clamp(height, 180, 8640),
            Encoder = string.IsNullOrWhiteSpace(encoder) ? "h264_nvenc" : encoder,
            BitrateKbps = Math.Clamp(bitrate, 500, 200000),
            Format = string.Equals(format, "mp4", StringComparison.OrdinalIgnoreCase) ? "mp4" : "mkv",
            Quality = string.Equals(quality, "Performance", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(quality, "Quality", StringComparison.OrdinalIgnoreCase)
                ? quality
                : "Balanced"
        };
    }

    private static string ResolveEncoderPreset(string encoder, string quality)
    {
        if (encoder.IndexOf("nvenc", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (string.Equals(quality, "Performance", StringComparison.OrdinalIgnoreCase))
            {
                return "p1";
            }

            return string.Equals(quality, "Quality", StringComparison.OrdinalIgnoreCase) ? "p6" : "p4";
        }

        if (string.Equals(quality, "Performance", StringComparison.OrdinalIgnoreCase))
        {
            return "ultrafast";
        }

        return string.Equals(quality, "Quality", StringComparison.OrdinalIgnoreCase) ? "medium" : "veryfast";
    }
}

internal static class WindowFinder
{
    public static IntPtr FindWindowByProcessId(int processId)
    {
        var best = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out var windowProcessId);
            if (windowProcessId != processId)
            {
                return true;
            }

            if (GetWindowTextLength(hwnd) <= 0)
            {
                return true;
            }

            best = hwnd;
            return false;
        }, IntPtr.Zero);
        return best;
    }

    public static IntPtr FindWindowByTitle(string title)
    {
        var best = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            var length = GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return true;
            }

            var builder = new StringBuilder(length + 1);
            _ = GetWindowText(hwnd, builder, builder.Capacity);
            var windowTitle = builder.ToString();
            if (windowTitle.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                best = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return best;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);
}

internal static class GraphicsCaptureItemFactory
{
    private static readonly Guid GraphicsCaptureItemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
    {
        var factory = (IGraphicsCaptureItemInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
        var itemPointer = factory.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
        try
        {
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, in Guid iid);

        IntPtr CreateForMonitor(IntPtr monitor, in Guid iid);
    }
}

internal static class Direct3DDeviceFactory
{
    private const uint D3D11CreateDeviceBgraSupport = 0x20;
    private const uint D3D11SdkVersion = 7;
    private const int D3DDriverTypeHardware = 1;

    public static IDirect3DDevice Create()
    {
        var hr = D3D11CreateDevice(
            IntPtr.Zero,
            D3DDriverTypeHardware,
            IntPtr.Zero,
            D3D11CreateDeviceBgraSupport,
            IntPtr.Zero,
            0,
            D3D11SdkVersion,
            out var d3dDevice,
            out _,
            out var d3dContext);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        try
        {
            hr = CreateDirect3D11DeviceFromDXGIDevice(d3dDevice, out var inspectable);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            try
            {
                return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            }
            finally
            {
                Marshal.Release(inspectable);
            }
        }
        finally
        {
            if (d3dContext != IntPtr.Zero)
            {
                Marshal.Release(d3dContext);
            }

            if (d3dDevice != IntPtr.Zero)
            {
                Marshal.Release(d3dDevice);
            }
        }
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out IntPtr device,
        out int featureLevel,
        out IntPtr immediateContext);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);
}

internal static class WindowsRuntimeMarshal
{
    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", CharSet = CharSet.Unicode)]
    private static extern int RoGetActivationFactory(
        IntPtr activatableClassId,
        in Guid iid,
        out IntPtr factory);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(
        string sourceString,
        int length,
        out IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    public static object GetActivationFactory(Type type)
    {
        var iid = typeof(IActivationFactory).GUID;
        var classNameText = type.FullName ?? throw new InvalidOperationException("WinRT type did not have a full name.");
        var hr = WindowsCreateString(classNameText, classNameText.Length, out var className);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        try
        {
            hr = RoGetActivationFactory(className, iid, out var factoryPointer);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            try
            {
                return Marshal.GetObjectForIUnknown(factoryPointer);
            }
            finally
            {
                Marshal.Release(factoryPointer);
            }
        }
        finally
        {
            _ = WindowsDeleteString(className);
        }
    }

    [ComImport]
    [Guid("00000035-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivationFactory
    {
        void ActivateInstance(out IntPtr instance);
    }
}
