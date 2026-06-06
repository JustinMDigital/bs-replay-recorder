using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using IPA.Logging;

namespace BSAutoReplayRecorder.Plugin;

internal sealed class ScoreSubmissionDisabler : IDisposable
{
    private const string HarmonyId = "BSAutoReplayRecorder.ScoreSubmissionDisabler";

    private static readonly BindingFlags StaticMethodFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly BindingFlags AnyMethodFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    private static readonly HashSet<string> BeatLeaderBlockedScoreUtilMethods =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "EnableSubmission",
            "ProcessReplay",
            "UploadReplay",
            "UploadPlay"
        };

    private static readonly HashSet<string> ScoreSaberKnownUploadDaemonMethods =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "SetupUploader",
            "Three",
            "Four",
            "Five",
            "Six",
            "Seven"
        };

    private static readonly MethodInfo ReturnFalsePrefixMethod = GetPrefix(nameof(ReturnFalsePrefix));
    private static readonly MethodInfo BlockVoidPrefixMethod = GetPrefix(nameof(BlockVoidPrefix));
    private static readonly MethodInfo BlockTaskPrefixMethod = GetPrefix(nameof(BlockTaskPrefix));

    private readonly Logger _logger;
    private readonly Harmony _harmony = new Harmony(HarmonyId);
    private readonly HashSet<string> _patchedMethods = new HashSet<string>(StringComparer.Ordinal);
    private bool _disposed;

    private ScoreSubmissionDisabler(Logger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static ScoreSubmissionDisabler Install(Logger logger)
    {
        var disabler = new ScoreSubmissionDisabler(logger);
        disabler.Install();
        return disabler;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppDomain.CurrentDomain.AssemblyLoad -= HandleAssemblyLoaded;
        _harmony.UnpatchSelf();
    }

    private void Install()
    {
        AppDomain.CurrentDomain.AssemblyLoad += HandleAssemblyLoaded;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            TryDisableAssembly(assembly);
        }
    }

    private void HandleAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
    {
        TryDisableAssembly(args.LoadedAssembly);
    }

    private void TryDisableAssembly(Assembly assembly)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            DisableBeatLeaderSubmission(assembly);
            DisableScoreSaberSubmission(assembly);
        }
        catch (Exception ex)
        {
            _logger.Warn("Failed to install score submission guard for " +
                         assembly.GetName().Name + ": " + ex.Message);
        }
    }

    private void DisableBeatLeaderSubmission(Assembly assembly)
    {
        var scoreUtilType = assembly.GetType("BeatLeader.Utils.ScoreUtil", throwOnError: false);
        if (scoreUtilType == null)
        {
            return;
        }

        SetStaticBooleanField(scoreUtilType, "Submission", false, "BeatLeader.ScoreUtil.Submission");
        SetStaticBooleanField(scoreUtilType, "SiraSubmission", false, "BeatLeader.ScoreUtil.SiraSubmission");
        SetStaticBooleanField(scoreUtilType, "BS_UtilsSubmission", false, "BeatLeader.ScoreUtil.BS_UtilsSubmission");

        PatchMethods(
            scoreUtilType,
            method => string.Equals(method.Name, "ShouldSubmit", StringComparison.Ordinal) &&
                      method.ReturnType == typeof(bool),
            ReturnFalsePrefixMethod,
            "BeatLeader.Utils.ScoreUtil.ShouldSubmit");

        PatchMethods(
            scoreUtilType,
            method => BeatLeaderBlockedScoreUtilMethods.Contains(method.Name) &&
                      method.ReturnType == typeof(void),
            BlockVoidPrefixMethod,
            "BeatLeader.Utils.ScoreUtil score upload method");

        PatchVoidMethodsByName(
            assembly.GetType("BeatLeader.API.Methods.UploadReplayRequest", throwOnError: false),
            "SendRequest",
            "BeatLeader.API.Methods.UploadReplayRequest.SendRequest");

        PatchVoidMethodsByName(
            assembly.GetType("BeatLeader.API.Methods.UploadPlayRequest", throwOnError: false),
            "SendRequest",
            "BeatLeader.API.Methods.UploadPlayRequest.SendRequest");

        PatchVoidMethodsByName(
            assembly.GetType("BeatLeader.Interop.BeatSaviorInterop", throwOnError: false),
            "UploadScorePostfix",
            "BeatLeader.Interop.BeatSaviorInterop.UploadScorePostfix");
    }

    private void DisableScoreSaberSubmission(Assembly assembly)
    {
        var pluginType = assembly.GetType("ScoreSaber.Plugin", throwOnError: false);
        if (pluginType == null)
        {
            return;
        }

        SetStaticBooleanProperty(pluginType, "ScoreSubmission", false, "ScoreSaber.Plugin.ScoreSubmission");

        PatchMethods(
            pluginType,
            method => string.Equals(method.Name, "get_ScoreSubmission", StringComparison.Ordinal) &&
                      method.ReturnType == typeof(bool),
            ReturnFalsePrefixMethod,
            "ScoreSaber.Plugin.ScoreSubmission getter");

        PatchVoidMethodsByName(
            pluginType,
            "set_ScoreSubmission",
            "ScoreSaber.Plugin.ScoreSubmission setter");

        var uploadDaemonType = assembly.GetType("ScoreSaber.Core.Daemons.UploadDaemon", throwOnError: false);
        PatchScoreSaberUploadDaemon(uploadDaemonType);

        PatchVoidMethodsByName(
            assembly.GetType(
                "ScoreSaber.Core.ReplaySystem.UI.ResultsViewReplayButtonController",
                throwOnError: false),
            "UploadDaemon_ReplaySerialized",
            "ScoreSaber replay upload callback");
    }

    private void PatchScoreSaberUploadDaemon(Type? uploadDaemonType)
    {
        if (uploadDaemonType == null)
        {
            return;
        }

        foreach (var method in uploadDaemonType.GetMethods(AnyMethodFlags))
        {
            if (!ShouldBlockScoreSaberUploadDaemonMethod(method))
            {
                continue;
            }

            PatchBlockingMethod(method, "ScoreSaber.Core.Daemons.UploadDaemon." + method.Name);
        }
    }

    private static bool ShouldBlockScoreSaberUploadDaemonMethod(MethodInfo method)
    {
        if (method.IsSpecialName ||
            method.IsConstructor ||
            string.Equals(method.Name, "Dispose", StringComparison.Ordinal) ||
            string.Equals(method.Name, "SaveLocalReplay", StringComparison.Ordinal))
        {
            return false;
        }

        if (ScoreSaberKnownUploadDaemonMethods.Contains(method.Name))
        {
            return true;
        }

        if (Contains(method.Name, "Upload") || Contains(method.Name, "Submit"))
        {
            return true;
        }

        return method.GetParameters().Any(parameter =>
        {
            var fullName = parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
            return Contains(fullName, "LevelCompletionResults") ||
                   Contains(fullName, "MultiplayerResultsData") ||
                   Contains(fullName, "ScoreSaberUploadData");
        });
    }

    private void PatchVoidMethodsByName(Type? type, string methodName, string label)
    {
        if (type == null)
        {
            return;
        }

        PatchMethods(
            type,
            method => string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                      method.ReturnType == typeof(void),
            BlockVoidPrefixMethod,
            label);
    }

    private void PatchMethods(
        Type type,
        Func<MethodInfo, bool> predicate,
        MethodInfo prefixMethod,
        string label)
    {
        foreach (var method in type.GetMethods(AnyMethodFlags).Where(predicate))
        {
            PatchMethod(method, prefixMethod, label + " (" + FormatMethod(method) + ")");
        }
    }

    private void PatchBlockingMethod(MethodInfo method, string label)
    {
        if (method.ReturnType == typeof(void))
        {
            PatchMethod(method, BlockVoidPrefixMethod, label + " (" + FormatMethod(method) + ")");
            return;
        }

        if (method.ReturnType == typeof(Task))
        {
            PatchMethod(method, BlockTaskPrefixMethod, label + " (" + FormatMethod(method) + ")");
            return;
        }

        if (method.ReturnType == typeof(bool))
        {
            PatchMethod(method, ReturnFalsePrefixMethod, label + " (" + FormatMethod(method) + ")");
            return;
        }

        _logger.Warn("Skipped score submission guard for " + FormatMethod(method) +
                     " because return type " + method.ReturnType.FullName + " is not supported.");
    }

    private void PatchMethod(MethodInfo method, MethodInfo prefixMethod, string label)
    {
        if (method.IsAbstract || method.ContainsGenericParameters)
        {
            return;
        }

        var patchKey = method.Module.ModuleVersionId.ToString("N") + ":" + method.MetadataToken;
        if (!_patchedMethods.Add(patchKey))
        {
            return;
        }

        _harmony.Patch(method, prefix: new HarmonyMethod(prefixMethod));
        _logger.Info("Score submission disabled: " + label + ".");
    }

    private void SetStaticBooleanProperty(Type type, string propertyName, bool value, string label)
    {
        var property = type.GetProperty(propertyName, StaticMethodFlags);
        if (property == null || property.PropertyType != typeof(bool) || property.SetMethod == null)
        {
            return;
        }

        try
        {
            property.SetValue(null, value, null);
            _logger.Info("Score submission setting forced off: " + label + ".");
        }
        catch (Exception ex)
        {
            _logger.Warn("Failed to force score submission setting off for " + label + ": " + ex.Message);
        }
    }

    private void SetStaticBooleanField(Type type, string fieldName, bool value, string label)
    {
        var field = type.GetField(fieldName, StaticMethodFlags);
        if (field == null || field.FieldType != typeof(bool))
        {
            return;
        }

        try
        {
            field.SetValue(null, value);
            _logger.Info("Score submission field forced off: " + label + ".");
        }
        catch (Exception ex)
        {
            _logger.Warn("Failed to force score submission field off for " + label + ": " + ex.Message);
        }
    }

    private static string FormatMethod(MethodInfo method)
    {
        return method.DeclaringType?.FullName + "." + method.Name;
    }

    private static bool Contains(string value, string part)
    {
        return value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static MethodInfo GetPrefix(string name)
    {
        return typeof(ScoreSubmissionDisabler).GetMethod(name, StaticMethodFlags)
               ?? throw new InvalidOperationException("Missing score submission guard prefix: " + name);
    }

    private static bool ReturnFalsePrefix(ref bool __result)
    {
        __result = false;
        return false;
    }

    private static bool BlockVoidPrefix()
    {
        return false;
    }

    private static bool BlockTaskPrefix(ref Task __result)
    {
        __result = Task.CompletedTask;
        return false;
    }
}
