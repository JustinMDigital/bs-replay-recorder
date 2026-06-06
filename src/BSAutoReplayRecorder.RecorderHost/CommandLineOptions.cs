using System.Globalization;

namespace BSAutoReplayRecorder.RecorderHost;

public sealed class CommandLineOptions
{
    private CommandLineOptions()
    {
    }

    public string Command { get; private set; } = "serve";

    public string ConfigPath { get; private set; } = "recorder-host.settings.json";

    public List<string> ConfigPaths { get; } = new List<string>();

    public bool ConfigPathWasProvided { get; private set; }

    public bool Force { get; private set; }

    public string OutputBaseName { get; private set; } = "manual-test";

    public string? WindowTitle { get; private set; }

    public TimeSpan Duration { get; private set; } = TimeSpan.FromSeconds(10);

    public double MinFps { get; private set; } = 60;

    public int? TargetFps { get; private set; }

    public int? CaptureWidth { get; private set; }

    public int? CaptureHeight { get; private set; }

    public string? Encoder { get; private set; }

    public int? VideoBitrateKbps { get; private set; }

    public string? OutputFormat { get; private set; }

    public int? MonitorIndex { get; private set; }

    public string? QualityMode { get; private set; }

    public bool ShowHelp { get; private set; }

    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();
        var index = 0;

        if (args.Length > 0 && !args[0].StartsWith("-", StringComparison.Ordinal))
        {
            options.Command = args[0];
            index = 1;
        }

        while (index < args.Length)
        {
            var arg = args[index];
            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    index++;
                    break;
                case "--config":
                    var configPath = ReadRequiredValue(args, ref index, arg);
                    options.ConfigPath = configPath;
                    options.ConfigPaths.Add(configPath);
                    options.ConfigPathWasProvided = true;
                    break;
                case "--force":
                    options.Force = true;
                    index++;
                    break;
                case "--output":
                    options.OutputBaseName = ReadRequiredValue(args, ref index, arg);
                    break;
                case "--window-title":
                    options.WindowTitle = ReadRequiredValue(args, ref index, arg);
                    break;
                case "--duration":
                    var durationSeconds = double.Parse(
                        ReadRequiredValue(args, ref index, arg),
                        CultureInfo.InvariantCulture);
                    if (durationSeconds <= 0)
                    {
                        throw new InvalidOperationException("--duration must be greater than zero.");
                    }

                    options.Duration = TimeSpan.FromSeconds(durationSeconds);
                    break;
                case "--min-fps":
                    options.MinFps = double.Parse(
                        ReadRequiredValue(args, ref index, arg),
                        CultureInfo.InvariantCulture);
                    if (options.MinFps <= 0)
                    {
                        throw new InvalidOperationException("--min-fps must be greater than zero.");
                    }

                    break;
                case "--fps":
                case "--target-fps":
                    options.TargetFps = int.Parse(ReadRequiredValue(args, ref index, arg), CultureInfo.InvariantCulture);
                    break;
                case "--width":
                    options.CaptureWidth = int.Parse(ReadRequiredValue(args, ref index, arg), CultureInfo.InvariantCulture);
                    break;
                case "--height":
                    options.CaptureHeight = int.Parse(ReadRequiredValue(args, ref index, arg), CultureInfo.InvariantCulture);
                    break;
                case "--encoder":
                    options.Encoder = ReadRequiredValue(args, ref index, arg);
                    break;
                case "--bitrate-kbps":
                    options.VideoBitrateKbps = int.Parse(ReadRequiredValue(args, ref index, arg), CultureInfo.InvariantCulture);
                    break;
                case "--format":
                    options.OutputFormat = ReadRequiredValue(args, ref index, arg);
                    break;
                case "--monitor":
                    options.MonitorIndex = int.Parse(ReadRequiredValue(args, ref index, arg), CultureInfo.InvariantCulture);
                    break;
                case "--quality":
                    options.QualityMode = ReadRequiredValue(args, ref index, arg);
                    break;
                default:
                    throw new InvalidOperationException("Unknown argument: " + arg);
            }
        }

        return options;
    }

    private static string ReadRequiredValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException("Missing value for " + optionName + ".");
        }

        index += 2;
        return args[index - 1];
    }

    public static string GetHelpText()
    {
        return """
        Beat Saber Auto Replay Recorder Host

        Usage:
          dotnet run --project src/BSAutoReplayRecorder.RecorderHost -- serve [--config <path>]
          dotnet run --project src/BSAutoReplayRecorder.RecorderHost -- init-config [--config <path>] [--force]
          dotnet run --project src/BSAutoReplayRecorder.RecorderHost -- print-default-config
          dotnet run --project src/BSAutoReplayRecorder.RecorderHost -- probe [--config <path>]
          dotnet run --project src/BSAutoReplayRecorder.RecorderHost -- record-once --duration <seconds> --output <name> [--window-title <title>] [--config <path>] [--fps 60] [--width 1920] [--height 1080] [--encoder h264_nvenc] [--bitrate-kbps 22000] [--format mp4] [--monitor 1] [--quality Balanced]
          dotnet run --project src/BSAutoReplayRecorder.RecorderHost -- benchmark --duration <seconds> --min-fps 60 --output <prefix> --config <path1> --config <path2> ... [--fps 60] [--width 1920] [--height 1080] [--encoder h264_nvenc] [--bitrate-kbps 22000] [--format mp4] [--monitor 1] [--quality Balanced]

        HTTP API:
          GET  /health
          GET  /status
          POST /recordings/start  { "outputBaseName": "01 - Song" }
          POST /recordings/stop   { "recordingId": "<optional id>" }
        """;
    }
}
