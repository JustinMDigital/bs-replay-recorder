using System;
using System.IO;
using System.Text;

namespace BSAutoReplayRecorder.Core.Replay;

public sealed class BsorInfoReader
{
    private const int MagicNumber = 0x442d3d69;
    private const int MaxStringBytes = 64 * 1024;
    private const byte InfoSectionMarker = 0;
    private const byte FramesSectionMarker = 1;
    private const int FramePayloadBytesAfterTimeAndFps = 84;

    public BsorInfo Read(string replayPath)
    {
        if (string.IsNullOrWhiteSpace(replayPath))
        {
            throw new ArgumentException("Replay path is required.", nameof(replayPath));
        }

        using var stream = File.OpenRead(replayPath);
        return Read(stream);
    }

    public BsorInfo Read(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var magic = reader.ReadInt32();
        if (magic != MagicNumber)
        {
            throw new InvalidDataException("The file is not a BSOR replay. Magic number did not match.");
        }

        var info = new BsorInfo
        {
            FormatVersion = reader.ReadByte()
        };

        var infoMarker = reader.ReadByte();
        if (infoMarker != InfoSectionMarker)
        {
            throw new InvalidDataException("BSOR info section was not found.");
        }

        ReadInfoSection(reader, info);
        TryReadFramesSection(reader, info);

        return info;
    }

    private static void ReadInfoSection(BinaryReader reader, BsorInfo info)
    {
        info.FileVersion = ReadBsorString(reader);
        info.GameVersion = ReadBsorString(reader);
        info.Timestamp = ReadBsorString(reader);
        info.PlayerId = ReadBsorString(reader);
        info.PlayerName = ReadBsorString(reader);
        info.Platform = ReadBsorString(reader);
        info.TrackingSystem = ReadBsorString(reader);
        info.Hmd = ReadBsorString(reader);
        info.Controller = ReadBsorString(reader);
        info.LevelHash = ReadBsorString(reader);
        info.SongName = ReadBsorString(reader);
        info.Mapper = ReadBsorString(reader);
        info.Difficulty = ReadBsorString(reader);
        info.Score = reader.ReadInt32();
        info.Mode = ReadBsorString(reader);
        info.Environment = ReadBsorString(reader);
        info.Modifiers = ReadBsorString(reader);
        info.JumpDistance = reader.ReadSingle();
        info.LeftHanded = reader.ReadBoolean();
        info.Height = reader.ReadSingle();
        info.StartTime = reader.ReadSingle();
        info.FailTime = reader.ReadSingle();
        info.Speed = reader.ReadSingle();
    }

    private static void TryReadFramesSection(BinaryReader reader, BsorInfo info)
    {
        if (!TryReadByte(reader, out var marker))
        {
            return;
        }

        if (marker != FramesSectionMarker)
        {
            return;
        }

        var frameCount = reader.ReadInt32();
        if (frameCount < 0)
        {
            throw new InvalidDataException("BSOR frame count cannot be negative.");
        }

        info.FrameCount = frameCount;

        var lastFrameTime = 0f;
        for (var index = 0; index < frameCount; index++)
        {
            var time = reader.ReadSingle();
            _ = reader.ReadInt32();
            SkipBytes(reader, FramePayloadBytesAfterTimeAndFps);

            if (time > lastFrameTime)
            {
                lastFrameTime = time;
            }
        }

        info.LastFrameTime = lastFrameTime;
    }

    private static bool TryReadByte(BinaryReader reader, out byte value)
    {
        if (reader.BaseStream.Position >= reader.BaseStream.Length)
        {
            value = 0;
            return false;
        }

        value = reader.ReadByte();
        return true;
    }

    private static string ReadBsorString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > MaxStringBytes)
        {
            throw new InvalidDataException("BSOR string length is invalid: " + length);
        }

        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException("Unexpected end of BSOR string.");
        }

        return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
    }

    private static void SkipBytes(BinaryReader reader, int bytesToSkip)
    {
        if (reader.BaseStream.CanSeek)
        {
            reader.BaseStream.Seek(bytesToSkip, SeekOrigin.Current);
            return;
        }

        var skipped = reader.ReadBytes(bytesToSkip);
        if (skipped.Length != bytesToSkip)
        {
            throw new EndOfStreamException("Unexpected end of BSOR frame payload.");
        }
    }
}

