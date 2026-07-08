using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Replayer;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Playback;
using BSAutoReplayRecorder.Core.Replay;
using HarmonyLib;
using UnityEngine;
using Logger = IPA.Logging.Logger;

namespace BSAutoReplayRecorder.Plugin;

public sealed class ScoreSaberReplayPlaybackDriver : IReplayPlaybackDriver
{
    private const string HarmonyId = "BSAutoReplayRecorder.ScoreSaberReplayPlaybackDriver";
    private const int UiSuppressionAttempts = 16;
    private const int UiSuppressionDelayMs = 150;
    private const int UiSuppressionHierarchyDepth = 8;
    private static readonly string[] ReplayUiNameKeywords =
    {
        "Replay",
        "Results",
        "Result",
        "HUD",
        "Menu",
        "Overlay",
        "Flow",
        "Coordinator",
        "Screen",
        "Popup",
        "Modal",
        "ReplaySystem",
        "ReplayCanvas"
    };
    private static readonly string[] ReplayUiHostKeywords =
    {
        "ScoreSaber",
        "ReplaySystem"
    };
    private static readonly string[] ReplayUiTypeKeywords =
    {
        "Replay",
        "Results",
        "Result",
        "Controller",
        "View",
        "ViewController",
        "Menu",
        "Coordinator",
        "Screen",
        "Popup",
        "Modal",
    };
    private static readonly object HarmonySync = new object();
    private static readonly object UiSync = new object();
    private static readonly object UiSuppressionControllerSync = new object();
    private static readonly HashSet<int> SuppressedReplayUiObjectIds = new();
    private static ScoreSaberReplayUiSuppressionController? _uiSuppressionController;
    private static Harmony? _harmony;
    private static event Action? ReplayStarted;
    private static event Action? ReplayFinished;

    private readonly Logger _logger;
    private readonly bool _suppressReplayUi;

    public ScoreSaberReplayPlaybackDriver(Logger logger, bool suppressReplayUi)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _suppressReplayUi = suppressReplayUi;
    }

    public string DriverName => "ScoreSaber";

    internal static (bool Ready, string Status) CheckRuntimeCompatibility()
    {
        try
        {
            var assembly = FindScoreSaberAssembly();
            if (assembly == null)
            {
                return (false, "ScoreSaber.dll is not loaded");
            }

            var pluginType = FindScoreSaberPluginType(assembly);
            var replayLoaderType = FindReplayLoaderType(assembly);
            if (pluginType == null || replayLoaderType == null)
            {
                return (false, "ScoreSaber replay loader types were not found");
            }

            if (ResolveScoreSaberContainer(pluginType, replayLoaderType) == null)
            {
                return (false, "ScoreSaber Zenject container is not available yet");
            }

            var loadMethod = TryFindReplayLoaderLoadMethod(replayLoaderType);
            if (loadMethod == null)
            {
                return (false, "ScoreSaber ReplayLoader.Load(byte[], ..., ..., ..., string) was not found. " +
                               FormatAvailableLoadMethods(replayLoaderType));
            }

            if (FindReplayEndMethod(replayLoaderType) == null)
            {
                return (false, "ScoreSaber ReplayLoader.ReplayEnd was not found");
            }

            return (true, "ScoreSaber replay loader ready (" + replayLoaderType.FullName + "): " +
                          FormatMethodSignature(loadMethod));
        }
        catch (Exception ex)
        {
            return (false, "ScoreSaber readiness check failed: " + ex.Message);
        }
    }

    public bool CanPlay(ReplayReference replayReference)
    {
        if (replayReference == null)
        {
            return false;
        }

        return replayReference.Provider == ReplayProvider.ScoreSaber2 &&
               (replayReference.Kind == ReplayReferenceKind.LocalScoreSaberDatFile ||
                replayReference.Kind == ReplayReferenceKind.ScoreSaber2ScoreUrl) &&
               replayReference.LocalPath != null;
    }

    public IReplayPlaybackWait CreateStartWait()
    {
        return new EventWait(
            handler => ReplayStarted += handler,
            handler => ReplayStarted -= handler,
            throwOnTimeout: false,
            timeoutMessage: "");
    }

    public IReplayPlaybackWait CreateFinishWait()
    {
        return new EventWait(
            handler => ReplayFinished += handler,
            handler => ReplayFinished -= handler,
            throwOnTimeout: true,
            timeoutMessage: "Timed out waiting for ScoreSaber replay to finish.");
    }

    public async Task ValidateReplayAsync(ReplayReference replayReference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanPlay(replayReference))
        {
            throw new InvalidOperationException("Replay reference is not a local ScoreSaber .dat file.");
        }

        var normalizedReference = NormalizeReplayReference(replayReference);
        var replayPath = normalizedReference.LocalPath!;
        if (!File.Exists(replayPath))
        {
            throw new FileNotFoundException("Replay file was not found.", replayPath);
        }

        new ScoreSaberReplayInfoReader().Validate(replayPath);
        EnsureReplayEndPatchInstalled();
        await LoadBeatmapAsync(normalizedReference, cancellationToken).ConfigureAwait(false);
        _ = ResolveReplayLoader();
    }

    public async Task<ReplayPlaybackSession> StartReplayAsync(
        ReplayQueueItem queueItem,
        ReplayReference replayReference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ValidateReplayAsync(replayReference, cancellationToken).ConfigureAwait(false);

        var normalizedReplayReference = NormalizeReplayReference(replayReference);
        var replayBytes = File.ReadAllBytes(normalizedReplayReference.LocalPath!);
        var loadResult = await LoadBeatmapAsync(normalizedReplayReference, cancellationToken).ConfigureAwait(false);
        var replayLoader = ResolveReplayLoader();
        var loadMethod = FindReplayLoaderLoadMethod(replayLoader.GetType());
        var modifiers = CreateDefaultGameplayModifiers(loadMethod.GetParameters()[3].ParameterType);
        var playerName = string.IsNullOrWhiteSpace(queueItem.ReplayInfo.PlayerName)
            ? "ScoreSaber"
            : queueItem.ReplayInfo.PlayerName;

        _logger.Info("Launching ScoreSaber replay: " + queueItem.ReplayInfo.SongName +
                     " [" + queueItem.ReplayInfo.Difficulty + "]");
        if (_suppressReplayUi)
        {
            StartReplayUiSuppression();
        }
        ReplayStarted?.Invoke();

        try
        {
            var task = (Task)loadMethod.Invoke(
                replayLoader,
                new[] { replayBytes, loadResult.Level, loadResult.BeatmapKey, modifiers, playerName })!;
            await task.ConfigureAwait(false);
            await SuppressReplayUiForRecordingUntilStableAsync(cancellationToken).ConfigureAwait(false);
            return new ReplayPlaybackSession(queueItem, normalizedReplayReference, DateTimeOffset.Now);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new InvalidOperationException(
                "ScoreSaber replay load failed: " + ex.InnerException.GetType().FullName +
                ": " + ex.InnerException.Message,
                ex.InnerException);
        }
        catch
        {
            if (_suppressReplayUi)
            {
                StopReplayUiSuppression();
                RestoreReplayUiForRecording();
            }

            throw;
        }
    }

    private static async Task SuppressReplayUiForRecordingUntilStableAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < UiSuppressionAttempts; attempt++)
        {
            SuppressReplayUiForRecording();
            if (attempt < UiSuppressionAttempts - 1)
            {
                await Task.Delay(UiSuppressionDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void StartReplayUiSuppression()
    {
        GetUiSuppressionController().StartSuppression();
    }

    private static void StopReplayUiSuppression()
    {
        _uiSuppressionController?.StopSuppression();
    }

    private static ScoreSaberReplayUiSuppressionController GetUiSuppressionController()
    {
        lock (UiSuppressionControllerSync)
        {
            if (_uiSuppressionController != null)
            {
                return _uiSuppressionController;
            }

            var gameObject = new GameObject("Auto Replay Recorder ScoreSaber UI Suppressor");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            _uiSuppressionController = gameObject.AddComponent<ScoreSaberReplayUiSuppressionController>();
            return _uiSuppressionController;
        }
    }

    private static void RestoreReplayUiForRecording()
    {
        List<GameObject> toRestore;
        lock (UiSync)
        {
            if (SuppressedReplayUiObjectIds.Count == 0)
            {
                return;
            }

            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            toRestore = new List<GameObject>(allObjects.Length);
            var lookup = new HashSet<int>(SuppressedReplayUiObjectIds);
            for (var index = 0; index < allObjects.Length; index++)
            {
                var gameObject = allObjects[index];
                if (gameObject == null)
                {
                    continue;
                }

                if (lookup.Contains(gameObject.GetInstanceID()))
                {
                    toRestore.Add(gameObject);
                }
            }

            SuppressedReplayUiObjectIds.Clear();
        }

        foreach (var gameObject in toRestore)
        {
            if (gameObject == null || gameObject.activeSelf)
            {
                continue;
            }

            gameObject.SetActive(true);
        }
    }

    private static void SuppressReplayUiForRecording()
    {
        try
        {
            var uiObjects = Resources.FindObjectsOfTypeAll(typeof(GameObject));
            foreach (var gameObject in uiObjects)
            {
                var castGameObject = gameObject as GameObject;
                if (castGameObject == null || !ShouldDisableGameObject(castGameObject))
                {
                    continue;
                }

                if (!castGameObject.activeSelf)
                {
                    continue;
                }

                foreach (var suppressedObject in GetReplayUiObjectsToSuppress(castGameObject))
                {
                    if (!suppressedObject.activeSelf)
                    {
                        continue;
                    }

                    suppressedObject.SetActive(false);
                    lock (UiSync)
                    {
                        SuppressedReplayUiObjectIds.Add(suppressedObject.GetInstanceID());
                    }
                }
            }
        }
        catch
        {
            // Best-effort: if game object traversal fails (for example during scene transitions),
            // we intentionally do not block replay playback.
        }
    }

    private static IEnumerable<GameObject> GetReplayUiObjectsToSuppress(GameObject gameObject)
    {
        var suppress = new List<GameObject>();
        var current = gameObject.transform;
        for (var depth = 0; depth < UiSuppressionHierarchyDepth && current != null; depth++, current = current.parent)
        {
            if (current == null)
            {
                break;
            }

            suppress.Add(current.gameObject);
            if (current.GetComponents<Component>().Any(
                    component => string.Equals(component?.GetType().Name, "Canvas", StringComparison.Ordinal)))
            {
                break;
            }
        }

        return suppress;
    }

    private static bool ShouldDisableGameObject(GameObject gameObject)
    {
        var objectName = gameObject.name;
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        if (IsLikelyReplayUiGameObject(gameObject))
        {
            return true;
        }

        if (IsLikelyReplayUiObjectName(objectName))
        {
            return true;
        }

        var components = gameObject.GetComponents<Component>();
        if (components.Length == 0)
        {
            return false;
        }

        return components.Any(IsReplaySystemUiComponent);
    }

    private static bool IsReplaySystemUiComponent(Component component)
    {
        if (component == null)
        {
            return false;
        }

        var type = component.GetType();
        var fullName = type.FullName ?? "";
        if (type.Assembly.GetName().Name?.IndexOf("ScoreSaber", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        if (!ContainsAnyIgnoreCase(fullName, ReplayUiTypeKeywords))
        {
            return false;
        }

        if (fullName.IndexOf("ScoreSaber.Core.ReplaySystem.UI", StringComparison.OrdinalIgnoreCase) >= 0 ||
            fullName.IndexOf("ScoreSaber.Core.ReplaySystem.Replay", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (fullName.IndexOf("ScoreSaber.Features.Replays", StringComparison.OrdinalIgnoreCase) >= 0 &&
            (fullName.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0 ||
             fullName.IndexOf("View", StringComparison.OrdinalIgnoreCase) >= 0 ||
             fullName.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) >= 0 ||
             fullName.IndexOf("Results", StringComparison.OrdinalIgnoreCase) >= 0 ||
             fullName.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0 ||
             fullName.IndexOf("Replay", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return true;
        }

        if (fullName.IndexOf("ScoreSaber.Core.ReplaySystem", StringComparison.OrdinalIgnoreCase) >= 0 &&
            (fullName.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0 ||
             fullName.IndexOf("View", StringComparison.OrdinalIgnoreCase) >= 0 ||
             fullName.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) >= 0 ||
             fullName.IndexOf("Results", StringComparison.OrdinalIgnoreCase) >= 0 ||
             fullName.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return true;
        }

        return false;
    }

    private static bool IsLikelyReplayUiGameObject(GameObject gameObject)
    {
        var current = gameObject.transform;
        var sawReplayToken = false;
        for (var depth = 0; depth < UiSuppressionHierarchyDepth && current != null; depth++, current = current.parent)
        {
            if (current == null)
            {
                break;
            }

            if (ContainsAnyIgnoreCase(current.name, ReplayUiNameKeywords))
            {
                sawReplayToken = true;
            }

            if (sawReplayToken &&
                ContainsAnyIgnoreCase(current.name, ReplayUiHostKeywords))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLikelyReplayUiObjectName(string objectName)
    {
        var containsReplayKeyword = ContainsAnyIgnoreCase(objectName, ReplayUiNameKeywords);
        if (containsReplayKeyword &&
            ContainsAnyIgnoreCase(objectName, ReplayUiHostKeywords))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsAnyIgnoreCase(string value, IEnumerable<string> values)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var item in values)
        {
            if (value.IndexOf(item, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<LoadedBeatmap> LoadBeatmapAsync(
        ReplayReference replayReference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ScoreSaberReplayInfoReader().Read(replayReference.LocalPath!);
        if (string.IsNullOrWhiteSpace(info.LevelHash))
        {
            throw new InvalidOperationException("ScoreSaber replay metadata did not include a level hash.");
        }

        var loader = ReplayerMenuLoader.Instance;
        if (loader == null)
        {
            throw new InvalidOperationException("BeatLeader ReplayerMenuLoader.Instance is not available yet for map lookup.");
        }

        object? loadResult = await loader
            .LoadBeatmapAsync(info.LevelHash, NormalizeMode(info.Mode), NormalizeDifficulty(info.Difficulty), cancellationToken)
            .ConfigureAwait(false);

        if (!BeatLeaderCompatibility.TryExtractBeatmapLevelWithKey(loadResult, out var level, out var beatmapKey))
        {
            throw new InvalidOperationException(
                "ScoreSaber cannot launch replay. The map may be missing or the replay metadata may not match an installed map.");
        }

        return new LoadedBeatmap(level, beatmapKey);
    }

    private object ResolveReplayLoader()
    {
        var assembly = FindScoreSaberAssembly();
        if (assembly == null)
        {
            throw new InvalidOperationException("ScoreSaber.dll is not loaded.");
        }

        var pluginType = FindScoreSaberPluginType(assembly);
        var replayLoaderType = FindReplayLoaderType(assembly);
        if (pluginType == null || replayLoaderType == null)
        {
            throw new InvalidOperationException("ScoreSaber replay loader types were not found.");
        }

        var container = ResolveScoreSaberContainer(pluginType, replayLoaderType);
        if (container == null)
        {
            throw new InvalidOperationException("ScoreSaber Zenject container is not available yet.");
        }

        var resolveMethod = FindContainerResolveMethod(container);
        if (resolveMethod == null)
        {
            throw new InvalidOperationException("ScoreSaber Zenject container does not expose Resolve(Type).");
        }

        try
        {
            return ResolveFromContainer(container, resolveMethod, replayLoaderType)
                   ?? throw new InvalidOperationException("ScoreSaber replay loader could not be resolved.");
        }
        catch (InvalidOperationException ex)
        {
            _logger.Warn("ScoreSaber replay loader is not bound; constructing it from container services. " + ex.Message);
            return CreateReplayLoader(container, resolveMethod, replayLoaderType);
        }
    }

    private static MethodInfo? FindContainerResolveMethod(object container)
    {
        return container.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
                string.Equals(method.Name, "Resolve", StringComparison.Ordinal) &&
                !method.IsGenericMethodDefinition &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == typeof(Type));
    }

    private static object? ResolveFromContainer(object container, MethodInfo resolveMethod, Type type)
    {
        try
        {
            return resolveMethod.Invoke(container, new object[] { type });
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new InvalidOperationException(
                "ScoreSaber container resolve failed for " + type.FullName + ": " +
                ex.InnerException.GetType().FullName +
                ": " + ex.InnerException.Message,
                ex.InnerException);
        }
    }

    private static object CreateReplayLoader(object container, MethodInfo resolveMethod, Type replayLoaderType)
    {
        var constructor = FindReplayLoaderConstructor(replayLoaderType);
        if (constructor == null)
        {
            throw new InvalidOperationException("ScoreSaber ReplayLoader constructor was not found.");
        }

        var parameters = constructor.GetParameters();
        var args = new object?[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            args[index] = ResolveFromContainer(container, resolveMethod, parameters[index].ParameterType)
                          ?? throw new InvalidOperationException(
                              "ScoreSaber container returned null for " + parameters[index].ParameterType.FullName + ".");
        }

        return constructor.Invoke(args);
    }

    private void EnsureReplayEndPatchInstalled()
    {
        lock (HarmonySync)
        {
            if (_harmony != null)
            {
                return;
            }

            var assembly = FindScoreSaberAssembly();
            var replayLoaderType = assembly == null ? null : FindReplayLoaderType(assembly);
            var replayEndMethod = replayLoaderType == null ? null : FindReplayEndMethod(replayLoaderType);
            if (replayEndMethod == null)
            {
                throw new InvalidOperationException("ScoreSaber ReplayLoader.ReplayEnd was not found.");
            }

            _harmony = new Harmony(HarmonyId);
            var postfix = typeof(ScoreSaberReplayPlaybackDriver).GetMethod(
                nameof(ReplayEndPostfix),
                BindingFlags.NonPublic | BindingFlags.Static);
            _harmony.Patch(replayEndMethod, postfix: new HarmonyMethod(postfix));
            _logger.Info("ScoreSaber replay finish hook installed.");
        }
    }

    private static void ReplayEndPostfix()
    {
        StopReplayUiSuppression();
        RestoreReplayUiForRecording();
        ReplayFinished?.Invoke();
    }

    private static MethodInfo FindReplayLoaderLoadMethod(Type replayLoaderType)
    {
        var method = TryFindReplayLoaderLoadMethod(replayLoaderType);
        return method ?? throw new InvalidOperationException(
            "ScoreSaber ReplayLoader.Load(byte[], ..., ..., ..., string) was not found. " +
            FormatAvailableLoadMethods(replayLoaderType));
    }

    private static Assembly? FindScoreSaberAssembly()
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(
                candidate.GetName().Name,
                "ScoreSaber",
                StringComparison.OrdinalIgnoreCase));
    }

    private static Type? FindScoreSaberPluginType(Assembly assembly)
    {
        return assembly.GetType("ScoreSaber.Plugin", throwOnError: false);
    }

    private static Type? FindReplayLoaderType(Assembly assembly)
    {
        return assembly.GetType("ScoreSaber.Core.ReplaySystem.ReplayLoader", throwOnError: false) ??
               assembly.GetType("ScoreSaber.Features.Replays.ReplayLoader", throwOnError: false);
    }

    private static object? ResolveScoreSaberContainer(Type pluginType, Type replayLoaderType)
    {
        var containerField = pluginType.GetField(
            "Container",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var container = containerField?.GetValue(null);
        if (container != null)
        {
            return container;
        }

        foreach (var candidate in ResolveZenjectContainers())
        {
            var resolveMethod = FindContainerResolveMethod(candidate);
            if (resolveMethod == null)
            {
                continue;
            }

            if (CanResolveReplayLoader(candidate, resolveMethod, replayLoaderType))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<object> ResolveZenjectContainers()
    {
        var projectContextType = FindType("Zenject.ProjectContext");
        if (projectContextType != null &&
            projectContextType.GetProperty("HasInstance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is bool hasProjectContext &&
            hasProjectContext &&
            projectContextType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is object projectContext)
        {
            var container = projectContextType
                .GetProperty("Container", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(projectContext, null);
            if (container != null)
            {
                yield return container;
            }
        }

        var zenUtilType = FindType("Zenject.Internal.ZenUtilInternal");
        var sceneContexts = zenUtilType
            ?.GetMethod("GetAllSceneContexts", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, null) as IEnumerable;
        if (sceneContexts == null)
        {
            yield break;
        }

        foreach (var sceneContext in sceneContexts)
        {
            if (sceneContext == null)
            {
                continue;
            }

            var container = sceneContext.GetType()
                .GetProperty("Container", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(sceneContext, null);
            if (container != null)
            {
                yield return container;
            }
        }
    }

    private static bool CanResolveReplayLoader(object container, MethodInfo resolveMethod, Type replayLoaderType)
    {
        if (TryResolveFromContainer(container, resolveMethod, replayLoaderType) != null)
        {
            return true;
        }

        var constructor = FindReplayLoaderConstructor(replayLoaderType);
        if (constructor == null)
        {
            return false;
        }

        return constructor
            .GetParameters()
            .All(parameter => TryResolveFromContainer(container, resolveMethod, parameter.ParameterType) != null);
    }

    private static object? TryResolveFromContainer(object container, MethodInfo resolveMethod, Type type)
    {
        try
        {
            return ResolveFromContainer(container, resolveMethod, type);
        }
        catch
        {
            return null;
        }
    }

    private static ConstructorInfo? FindReplayLoaderConstructor(Type replayLoaderType)
    {
        return replayLoaderType
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .FirstOrDefault();
    }

    private static Type? FindType(string fullName)
    {
        var type = Type.GetType(fullName, throwOnError: false);
        if (type != null)
        {
            return type;
        }

        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType(fullName, throwOnError: false))
            .FirstOrDefault(candidate => candidate != null);
    }

    private static MethodInfo? FindReplayEndMethod(Type replayLoaderType)
    {
        return replayLoaderType.GetMethod(
            "ReplayEnd",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    }

    private static MethodInfo? TryFindReplayLoaderLoadMethod(Type replayLoaderType)
    {
        return replayLoaderType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(IsCompatibleReplayLoaderLoadMethod);
    }

    private static bool IsCompatibleReplayLoaderLoadMethod(MethodInfo candidate)
    {
        if (!string.Equals(candidate.Name, "Load", StringComparison.Ordinal) ||
            !typeof(Task).IsAssignableFrom(candidate.ReturnType))
        {
            return false;
        }

        var parameters = candidate.GetParameters();
        return parameters.Length == 5 &&
               parameters[0].ParameterType == typeof(byte[]) &&
               parameters[4].ParameterType == typeof(string) &&
               !parameters[1].ParameterType.IsByRef &&
               !parameters[2].ParameterType.IsByRef &&
               !parameters[3].ParameterType.IsByRef;
    }

    private static string FormatAvailableLoadMethods(Type replayLoaderType)
    {
        var overloads = replayLoaderType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(method => string.Equals(method.Name, "Load", StringComparison.Ordinal))
            .Select(FormatMethodSignature)
            .ToArray();

        return overloads.Length == 0
            ? "No Load overloads were found."
            : "Available Load overloads: " + string.Join("; ", overloads) + ".";
    }

    private static string FormatMethodSignature(MethodInfo method)
    {
        var parameters = method.GetParameters()
            .Select(parameter => FormatTypeName(parameter.ParameterType))
            .ToArray();
        return FormatTypeName(method.ReturnType) + " " + method.Name + "(" + string.Join(", ", parameters) + ")";
    }

    private static string FormatTypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name;
        var tickIndex = name.IndexOf('`');
        if (tickIndex >= 0)
        {
            name = name.Substring(0, tickIndex);
        }

        return name + "<" + string.Join(", ", type.GetGenericArguments().Select(FormatTypeName)) + ">";
    }

    private static ReplayReference NormalizeReplayReference(ReplayReference replayReference)
    {
        if (replayReference == null)
        {
            throw new ArgumentNullException(nameof(replayReference));
        }

        if (replayReference.Provider != ReplayProvider.ScoreSaber2 ||
            replayReference.Kind == ReplayReferenceKind.LocalScoreSaberDatFile)
        {
            return replayReference;
        }

        return new ReplayReference(
            ReplayProvider.ScoreSaber2,
            ReplayReferenceKind.LocalScoreSaberDatFile,
            replayReference.OriginalValue,
            replayReference.LocalPath,
            replayReference.Uri,
            replayReference.ScoreId);
    }

    private static object CreateDefaultGameplayModifiers(Type gameplayModifiersType)
    {
        return Activator.CreateInstance(gameplayModifiersType)
               ?? throw new InvalidOperationException("Could not create default GameplayModifiers.");
    }

    private static string NormalizeMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "Standard";
        }

        if (mode.EndsWith("Standard", StringComparison.OrdinalIgnoreCase))
        {
            return "Standard";
        }

        if (mode.IndexOf("OneSaber", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "OneSaber";
        }

        return mode;
    }

    private static string NormalizeDifficulty(string difficulty)
    {
        if (string.IsNullOrWhiteSpace(difficulty))
        {
            return "Expert";
        }

        if (difficulty.IndexOf("ExpertPlus", StringComparison.OrdinalIgnoreCase) >= 0 ||
            difficulty.IndexOf("Expert+", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "ExpertPlus";
        }

        if (difficulty.IndexOf("Expert", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Expert";
        }

        if (difficulty.IndexOf("Hard", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Hard";
        }

        if (difficulty.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Normal";
        }

        if (difficulty.IndexOf("Easy", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Easy";
        }

        return difficulty;
    }

    private sealed class ScoreSaberReplayUiSuppressionController : MonoBehaviour
    {
        private static readonly float UiSuppressionTickSeconds = UiSuppressionDelayMs / 1000f;
        private float _suppressTimer;
        private bool _isSuppressionActive;

        public void StartSuppression()
        {
            _isSuppressionActive = true;
            _suppressTimer = 0f;
            enabled = true;
        }

        public void StopSuppression()
        {
            _isSuppressionActive = false;
            enabled = false;
        }

        private void Update()
        {
            if (!_isSuppressionActive)
            {
                return;
            }

            _suppressTimer += Time.unscaledDeltaTime;
            if (_suppressTimer < UiSuppressionTickSeconds)
            {
                return;
            }

            _suppressTimer = 0f;
            SuppressReplayUiForRecording();
        }
    }

    private sealed class EventWait : IReplayPlaybackWait
    {
        private readonly Action<Action> _unsubscribe;
        private readonly bool _throwOnTimeout;
        private readonly string _timeoutMessage;
        private readonly TaskCompletionSource<DateTimeOffset> _completion = new TaskCompletionSource<DateTimeOffset>();

        public EventWait(
            Action<Action> subscribe,
            Action<Action> unsubscribe,
            bool throwOnTimeout,
            string timeoutMessage)
        {
            _unsubscribe = unsubscribe;
            _throwOnTimeout = throwOnTimeout;
            _timeoutMessage = timeoutMessage;
            subscribe(HandleEvent);
        }

        public async Task<DateTimeOffset?> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var timeoutTask = Task.Delay(timeout, cancellationToken);
            var completed = await Task.WhenAny(_completion.Task, timeoutTask).ConfigureAwait(false);
            if (completed == _completion.Task)
            {
                return await _completion.Task.ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (_throwOnTimeout)
            {
                throw new TimeoutException(_timeoutMessage);
            }

            return null;
        }

        public void Dispose()
        {
            _unsubscribe(HandleEvent);
        }

        private void HandleEvent()
        {
            _completion.TrySetResult(DateTimeOffset.UtcNow);
        }
    }

    private sealed class LoadedBeatmap
    {
        public LoadedBeatmap(object level, object beatmapKey)
        {
            Level = level;
            BeatmapKey = beatmapKey;
        }

        public object Level { get; }

        public object BeatmapKey { get; }
    }
}
