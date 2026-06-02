using System.Text;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Obs;
using BSAutoReplayRecorder.Core.Replay;

var tempDir = Path.Combine(Path.GetTempPath(), "bsarr-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDir);

try
{
    var firstReplay = Path.Combine(tempDir, "01-first.bsor");
    var secondReplay = Path.Combine(tempDir, "02-second.bsor");

    WriteSampleBsor(firstReplay, "Alpha/Song", "Mapper One", "ExpertPlus", 123456, 91.5f);
    WriteSampleBsor(secondReplay, "Beta Song", "Mapper Two", "Expert", 234567, 120.25f);

    var reader = new BsorInfoReader();
    var firstInfo = reader.Read(firstReplay);

    AssertEqual("Alpha/Song", firstInfo.SongName, "song name");
    AssertEqual("ExpertPlus", firstInfo.Difficulty, "difficulty");
    AssertEqual(3, firstInfo.FrameCount, "frame count");
    AssertEqual(91.5, Math.Round(firstInfo.ReplayLength.TotalSeconds, 2), "replay length");

    var queue = new ReplayQueue().Load(new ReplayQueueOptions
    {
        InputDirectory = tempDir,
        SkipInvalidReplays = true
    });

    AssertEqual(2, queue.Items.Count, "queue item count");
    AssertEqual(0, queue.Failures.Count, "queue failure count");
    AssertEqual(1, queue.Items[0].SequenceNumber, "first sequence");
    AssertEqual(2, queue.Items[1].SequenceNumber, "second sequence");

    var plans = new RecordingPlanner().CreatePlans(
        queue.Items,
        new RecordingPlanOptions
        {
            OutputNameTemplate = "{index:00} - {song} [{difficulty}]",
            PreRoll = TimeSpan.FromSeconds(2),
            PostRoll = TimeSpan.FromSeconds(4)
        });

    AssertEqual("01 - Alpha Song [ExpertPlus]", plans[0].OutputBaseName, "sanitized output name");
    AssertEqual(97.5, Math.Round(plans[0].EstimatedRecordingLength.TotalSeconds, 2), "estimated recording length");

    var startRequest = ObsRequestFactory.CreateRequest("StartRecord", "request-1");
    AssertEqual("{\"op\":6,\"d\":{\"requestType\":\"StartRecord\",\"requestId\":\"request-1\"}}", startRequest, "OBS start request");

    var auth = ObsAuthentication.CreateAuthentication("password", "salt", "challenge");
    AssertEqual("zTM5ki6L2vVvBQiTG9ckH1Lh64AbnCf6XZ226UmnkIA=", auth, "OBS authentication hash");

    var referenceParser = new ReplayReferenceParser();
    var bsorReference = referenceParser.Parse(firstReplay);
    AssertEqual(ReplayProvider.BeatLeader, bsorReference.Provider, "local bsor provider");
    AssertEqual(ReplayReferenceKind.LocalBsorFile, bsorReference.Kind, "local bsor kind");

    var scoreSaberReference = referenceParser.Parse(@"C:\Beat Saber\UserData\ScoreSaber\Replays\example.dat");
    AssertEqual(ReplayProvider.ScoreSaberLegacy, scoreSaberReference.Provider, "legacy scoresaber provider");
    AssertEqual(ReplayReferenceKind.LocalScoreSaberDatFile, scoreSaberReference.Kind, "legacy scoresaber kind");

    var beatLeaderReference = referenceParser.Parse("https://replay.beatleader.xyz/?scoreId=9280912");
    AssertEqual(ReplayProvider.BeatLeader, beatLeaderReference.Provider, "beatleader url provider");
    AssertEqual("9280912", beatLeaderReference.ScoreId, "beatleader score id");

    var scoreSaber2Reference = referenceParser.Parse("https://new.scoresaber.com/scores/123456");
    AssertEqual(ReplayProvider.ScoreSaber2, scoreSaber2Reference.Provider, "scoresaber 2 provider");
    AssertEqual("123456", scoreSaber2Reference.ScoreId, "scoresaber 2 score id");

    Console.WriteLine("All core checks passed.");
}
finally
{
    Directory.Delete(tempDir, recursive: true);
}

static void WriteSampleBsor(
    string path,
    string songName,
    string mapper,
    string difficulty,
    int score,
    float lastFrameTime)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream, Encoding.UTF8);

    writer.Write(0x442d3d69);
    writer.Write((byte)1);
    writer.Write((byte)0);

    WriteString(writer, "1.0.0");
    WriteString(writer, "1.39.1");
    WriteString(writer, "2026-06-02T00:00:00Z");
    WriteString(writer, "player-id");
    WriteString(writer, "Player");
    WriteString(writer, "Steam");
    WriteString(writer, "OpenVR");
    WriteString(writer, "Index");
    WriteString(writer, "Index Controllers");
    WriteString(writer, "ABCDEF123456");
    WriteString(writer, songName);
    WriteString(writer, mapper);
    WriteString(writer, difficulty);
    writer.Write(score);
    WriteString(writer, "Standard");
    WriteString(writer, "DefaultEnvironment");
    WriteString(writer, "");
    writer.Write(18.5f);
    writer.Write(false);
    writer.Write(1.8f);
    writer.Write(0f);
    writer.Write(0f);
    writer.Write(1f);

    writer.Write((byte)1);
    writer.Write(3);
    WriteFrame(writer, 0f);
    WriteFrame(writer, lastFrameTime / 2f);
    WriteFrame(writer, lastFrameTime);
}

static void WriteFrame(BinaryWriter writer, float time)
{
    writer.Write(time);
    writer.Write(90);

    for (var index = 0; index < 21; index++)
    {
        writer.Write(0f);
    }
}

static void WriteString(BinaryWriter writer, string value)
{
    var bytes = Encoding.UTF8.GetBytes(value);
    writer.Write(bytes.Length);
    writer.Write(bytes);
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(label + " failed. Expected " + expected + ", got " + actual + ".");
    }
}
