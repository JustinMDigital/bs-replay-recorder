using System;
using System.Collections.Generic;
using System.Globalization;
using BSAutoReplayRecorder.Core.Utility;

namespace BSAutoReplayRecorder.Core;

public sealed class RecordingPlanner
{
    public IReadOnlyList<RecordingPlan> CreatePlans(
        IReadOnlyList<ReplayQueueItem> queueItems,
        RecordingPlanOptions options)
    {
        if (queueItems == null)
        {
            throw new ArgumentNullException(nameof(queueItems));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var plans = new List<RecordingPlan>(queueItems.Count);
        foreach (var item in queueItems)
        {
            var outputName = RenderTemplate(options.OutputNameTemplate, item);
            plans.Add(new RecordingPlan(item, outputName, options.PreRoll, options.PostRoll));
        }

        return plans;
    }

    private static string RenderTemplate(string template, ReplayQueueItem item)
    {
        var info = item.ReplayInfo;
        var output = template;

        output = ReplaceIndex(output, item.SequenceNumber);
        output = output.Replace("{song}", info.SongName);
        output = output.Replace("{difficulty}", info.Difficulty);
        output = output.Replace("{mapper}", info.Mapper);
        output = output.Replace("{hash}", info.LevelHash);
        output = output.Replace("{player}", info.PlayerName);
        output = output.Replace("{score}", info.Score.ToString(CultureInfo.InvariantCulture));
        output = output.Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return FileNameSanitizer.SanitizeBaseName(output);
    }

    private static string ReplaceIndex(string template, int index)
    {
        const string tokenPrefix = "{index";
        var start = template.IndexOf(tokenPrefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return template;
        }

        var end = template.IndexOf('}', start);
        if (end < 0)
        {
            return template;
        }

        var token = template.Substring(start, end - start + 1);
        var rendered = index.ToString(CultureInfo.InvariantCulture);

        if (token.StartsWith("{index:", StringComparison.Ordinal))
        {
            var format = token.Substring("{index:".Length, token.Length - "{index:".Length - 1);
            rendered = index.ToString(format, CultureInfo.InvariantCulture);
        }

        return template.Replace(token, rendered);
    }
}

