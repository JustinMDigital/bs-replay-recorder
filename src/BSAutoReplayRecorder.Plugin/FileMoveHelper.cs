using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IPA.Logging;

namespace BSAutoReplayRecorder.Plugin;

internal static class FileMoveHelper
{
    private const int MaxAttempts = 30;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    public static async Task<bool> MoveWithRetriesAsync(
        string sourcePath,
        string targetPath,
        bool overwrite,
        string label,
        Logger logger,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(sourcePath))
                {
                    logger.Warn(label + " file was not found, so it could not be moved: " + sourcePath);
                    return false;
                }

                if (overwrite && File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                MoveOrCopyDelete(sourcePath, targetPath, overwrite, logger, label);
                logger.Info("Moved " + label + " to: " + targetPath);
                return true;
            }
            catch (IOException ex)
            {
                lastException = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
            }

            CleanupPartialTarget(sourcePath, targetPath, logger, label);

            if (attempt < MaxAttempts)
            {
                logger.Debug("Waiting to move " + label + " file. Attempt " + attempt +
                             " failed: " + lastException.Message);
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        logger.Error("Failed to move " + label + " from " + sourcePath + " to " + targetPath +
                     " after " + MaxAttempts + " attempt(s): " + lastException);
        return false;
    }

    private static void MoveOrCopyDelete(
        string sourcePath,
        string targetPath,
        bool overwrite,
        Logger logger,
        string label)
    {
        try
        {
            File.Move(sourcePath, targetPath);
            return;
        }
        catch (IOException)
        {
            File.Copy(sourcePath, targetPath, overwrite);
            try
            {
                File.Delete(sourcePath);
            }
            catch (Exception ex)
            {
                logger.Warn("Copied " + label + " to " + targetPath +
                            ", but could not delete the original file " + sourcePath + ": " + ex);
            }
        }
    }

    private static void CleanupPartialTarget(
        string sourcePath,
        string targetPath,
        Logger logger,
        string label)
    {
        if (!File.Exists(sourcePath) || !File.Exists(targetPath))
        {
            return;
        }

        try
        {
            File.Delete(targetPath);
        }
        catch (Exception ex)
        {
            logger.Warn("Could not remove partial " + label + " target before retry: " + targetPath + ": " + ex);
        }
    }
}
