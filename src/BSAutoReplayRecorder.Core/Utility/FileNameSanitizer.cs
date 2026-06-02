using System;
using System.IO;
using System.Text;

namespace BSAutoReplayRecorder.Core.Utility;

public static class FileNameSanitizer
{
    public static string SanitizeBaseName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "recording";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;

        foreach (var ch in value)
        {
            var replacement = Array.IndexOf(invalid, ch) >= 0 ? ' ' : ch;
            if (char.IsWhiteSpace(replacement))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            builder.Append(replacement);
            previousWasSpace = false;
        }

        var sanitized = builder.ToString().Trim(' ', '.', '-');
        return sanitized.Length == 0 ? "recording" : sanitized;
    }
}

