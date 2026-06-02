using System;

namespace BSAutoReplayRecorder.Core;

internal static class QueryString
{
    public static string? Get(string query, string key)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        var trimmed = query[0] == '?' ? query.Substring(1) : query;
        var parts = trimmed.Split('&');
        foreach (var part in parts)
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(part.Substring(0, separator));
            if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.UnescapeDataString(part.Substring(separator + 1));
        }

        return null;
    }
}

