using System.Diagnostics;
using System.Globalization;
using BSAutoReplayRecorder.Core;

namespace BSAutoReplayRecorder.RecorderHost;

public sealed class SyncMarkerAnalysisResult
{
    public string Status { get; set; } = "Corrected";

    public double AudioOffsetSeconds { get; set; }

    public double SyncCorrectionMilliseconds { get; set; }

    public List<double> VideoPulseTimesSeconds { get; set; } = new List<double>();

    public List<double> AudioPulseTimesSeconds { get; set; } = new List<double>();
}

public static class SyncMarkerAnalyzer
{
    public static SyncMarkerAnalysisResult AnalyzeSamples(
        IReadOnlyList<double> videoBrightnessSamples,
        double videoSamplesPerSecond,
        IReadOnlyList<double> audioSamples,
        double audioSamplesPerSecond)
    {
        if (videoBrightnessSamples == null)
        {
            throw new ArgumentNullException(nameof(videoBrightnessSamples));
        }

        if (audioSamples == null)
        {
            throw new ArgumentNullException(nameof(audioSamples));
        }

        if (videoSamplesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(videoSamplesPerSecond));
        }

        if (audioSamplesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(audioSamplesPerSecond));
        }

        var videoPulses = SelectPulseSequence(
            FindPulseTimes(
                videoBrightnessSamples,
                videoSamplesPerSecond,
                RecordingSyncMarker.MinimumVisualBrightness,
                useAbsoluteValue: false,
                usePeakTime: false),
            "visual");
        var audioPulses = SelectPulseSequence(
            FindPulseTimes(
                audioSamples,
                audioSamplesPerSecond,
                RecordingSyncMarker.MinimumAudioPeak,
                useAbsoluteValue: true,
                usePeakTime: true),
            "audio");

        var offsets = new List<double>();
        for (var index = 0; index < RecordingSyncMarker.PulseCount; index++)
        {
            var offset = videoPulses[index] - audioPulses[index];
            if (Math.Abs(offset) > RecordingSyncMarker.MaximumPairOffsetSeconds)
            {
                throw new InvalidOperationException(
                    "Sync marker pulse " + (index + 1).ToString(CultureInfo.InvariantCulture) +
                    " offset is outside the allowed range.");
            }

            offsets.Add(offset);
        }

        var minOffset = offsets.Min();
        var maxOffset = offsets.Max();
        if (maxOffset - minOffset > RecordingSyncMarker.PulseSpacingToleranceSeconds)
        {
            throw new InvalidOperationException("Sync marker pulse offsets are inconsistent.");
        }

        var averageOffset = offsets.Average();
        return new SyncMarkerAnalysisResult
        {
            AudioOffsetSeconds = averageOffset,
            SyncCorrectionMilliseconds = Math.Round(averageOffset * 1000.0, 1),
            VideoPulseTimesSeconds = videoPulses,
            AudioPulseTimesSeconds = audioPulses
        };
    }

    internal static async Task<SyncMarkerAnalysisResult> AnalyzeFilesAsync(
        string ffmpegPath,
        string videoPath,
        string audioPath,
        CancellationToken cancellationToken)
    {
        var searchSeconds = RecordingSyncMarker.SearchWindowSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var videoBytes = await CaptureFfmpegBytesAsync(
            ffmpegPath,
            new[]
            {
                "-hide_banner",
                "-v",
                "error",
                "-t",
                searchSeconds,
                "-i",
                videoPath,
                "-map",
                "0:v:0",
                "-vf",
                "fps=" + RecordingSyncMarker.VideoSampleRate.ToString("0.###", CultureInfo.InvariantCulture) + ",scale=1:1,format=gray",
                "-f",
                "rawvideo",
                "-pix_fmt",
                "gray",
                "pipe:1"
            },
            cancellationToken).ConfigureAwait(false);

        var audioBytes = await CaptureFfmpegBytesAsync(
            ffmpegPath,
            new[]
            {
                "-hide_banner",
                "-v",
                "error",
                "-t",
                searchSeconds,
                "-i",
                audioPath,
                "-map",
                "0:a:0",
                "-ac",
                "1",
                "-ar",
                RecordingSyncMarker.AudioSampleRate.ToString(CultureInfo.InvariantCulture),
                "-f",
                "s16le",
                "pipe:1"
            },
            cancellationToken).ConfigureAwait(false);

        if (videoBytes.Length == 0)
        {
            throw new InvalidOperationException("Sync marker analysis could not decode video samples.");
        }

        if (audioBytes.Length < 2)
        {
            throw new InvalidOperationException("Sync marker analysis could not decode audio samples.");
        }

        var videoSamples = videoBytes.Select(item => item / 255.0).ToArray();
        var audioSamples = ConvertPcm16ToDoubles(audioBytes);
        return AnalyzeSamples(
            videoSamples,
            RecordingSyncMarker.VideoSampleRate,
            audioSamples,
            RecordingSyncMarker.AudioSampleRate);
    }

    private static IReadOnlyList<double> FindPulseTimes(
        IReadOnlyList<double> samples,
        double samplesPerSecond,
        double minimumPeak,
        bool useAbsoluteValue,
        bool usePeakTime)
    {
        if (samples.Count == 0)
        {
            throw new InvalidOperationException("Sync marker analysis had no samples.");
        }

        var peak = samples.Max(item => useAbsoluteValue ? Math.Abs(item) : item);
        if (peak < minimumPeak)
        {
            throw new InvalidOperationException("Sync marker peak was below the required confidence threshold.");
        }

        var threshold = Math.Max(minimumPeak, peak * 0.60);
        var minimumSeparation = RecordingSyncMarker.PulseSpacingSeconds * 0.50;
        var pulses = new List<PulseCandidate>();
        var inPulse = false;
        var startIndex = 0;
        var peakIndex = 0;
        var peakValue = 0.0;

        for (var index = 0; index < samples.Count; index++)
        {
            var value = useAbsoluteValue ? Math.Abs(samples[index]) : samples[index];
            if (value >= threshold)
            {
                if (!inPulse)
                {
                    inPulse = true;
                    startIndex = index;
                    peakIndex = index;
                    peakValue = value;
                }
                else if (value > peakValue)
                {
                    peakIndex = index;
                    peakValue = value;
                }

                continue;
            }

            if (inPulse)
            {
                AddPulse();
                inPulse = false;
            }
        }

        if (inPulse)
        {
            AddPulse();
        }

        return pulses.Select(item => item.TimeSeconds).ToList();

        void AddPulse()
        {
            var timeIndex = usePeakTime ? peakIndex : startIndex;
            var timeSeconds = timeIndex / samplesPerSecond;
            if (pulses.Count > 0 && timeSeconds - pulses[pulses.Count - 1].TimeSeconds < minimumSeparation)
            {
                if (peakValue > pulses[pulses.Count - 1].PeakValue)
                {
                    pulses[pulses.Count - 1] = new PulseCandidate(timeSeconds, peakValue);
                }

                return;
            }

            pulses.Add(new PulseCandidate(timeSeconds, peakValue));
        }
    }

    private static List<double> SelectPulseSequence(IReadOnlyList<double> candidates, string label)
    {
        if (candidates.Count < RecordingSyncMarker.PulseCount)
        {
            throw new InvalidOperationException(
                "Sync marker analysis found " + candidates.Count.ToString(CultureInfo.InvariantCulture) +
                " " + label + " pulse(s), expected " +
                RecordingSyncMarker.PulseCount.ToString(CultureInfo.InvariantCulture) + ".");
        }

        for (var start = 0; start <= candidates.Count - RecordingSyncMarker.PulseCount; start++)
        {
            var sequence = candidates.Skip(start).Take(RecordingSyncMarker.PulseCount).ToList();
            if (HasExpectedSpacing(sequence))
            {
                return sequence;
            }
        }

        throw new InvalidOperationException("Sync marker " + label + " pulses did not match the expected spacing.");
    }

    private static bool HasExpectedSpacing(IReadOnlyList<double> sequence)
    {
        for (var index = 1; index < sequence.Count; index++)
        {
            var spacing = sequence[index] - sequence[index - 1];
            if (Math.Abs(spacing - RecordingSyncMarker.PulseSpacingSeconds) >
                RecordingSyncMarker.PulseSpacingToleranceSeconds)
            {
                return false;
            }
        }

        return true;
    }

    private static double[] ConvertPcm16ToDoubles(byte[] bytes)
    {
        var sampleCount = bytes.Length / 2;
        var samples = new double[sampleCount];
        for (var index = 0; index < sampleCount; index++)
        {
            var raw = BitConverter.ToInt16(bytes, index * 2);
            samples[index] = raw / 32768.0;
        }

        return samples;
    }

    private static async Task<byte[]> CaptureFfmpegBytesAsync(
        string ffmpegPath,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start FFmpeg for sync marker analysis.");
        }

        await using var memory = new MemoryStream();
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(memory, cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await outputTask.ConfigureAwait(false);
        var stderr = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "FFmpeg sync marker analysis failed. ExitCode=" +
                process.ExitCode.ToString(CultureInfo.InvariantCulture) +
                (string.IsNullOrWhiteSpace(stderr) ? "." : ": " + stderr.Trim()));
        }

        return memory.ToArray();
    }

    private readonly struct PulseCandidate
    {
        public PulseCandidate(double timeSeconds, double peakValue)
        {
            TimeSeconds = timeSeconds;
            PeakValue = peakValue;
        }

        public double TimeSeconds { get; }

        public double PeakValue { get; }
    }
}
