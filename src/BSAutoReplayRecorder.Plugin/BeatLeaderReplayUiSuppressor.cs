using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IPA.Logging;

namespace BSAutoReplayRecorder.Plugin;

internal static class BeatLeaderReplayUiSuppressor
{
    private const string HarmonyId = "BSAutoReplayRecorder.BeatLeaderReplayUiSuppressor";
    private static readonly BindingFlags AnyInstanceMethodFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly object Sync = new object();
    private static readonly HashSet<string> PatchedMethods = new HashSet<string>(StringComparer.Ordinal);
    private static Harmony? _harmony;
    private static Logger? _logger;

    public static void Install(Logger logger)
    {
        if (logger == null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        lock (Sync)
        {
            _logger = logger;

            if (_harmony == null)
            {
                _harmony = new Harmony(HarmonyId);
                AppDomain.CurrentDomain.AssemblyLoad += HandleAssemblyLoaded;
            }

            var patchCount = PatchLoadedAssemblies(_harmony);
            if (patchCount > 0)
            {
                logger.Info("Installed BeatLeader replay UI suppressor patches: " + patchCount + ".");
            }
        }
    }

    private static void HandleAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
    {
        lock (Sync)
        {
            if (_harmony == null)
            {
                return;
            }

            var patchCount = PatchAssembly(_harmony, args.LoadedAssembly);
            if (patchCount > 0)
            {
                _logger?.Info("Installed BeatLeader replay UI suppressor patches from loaded assembly: " + patchCount + ".");
            }
        }
    }

    private static int PatchLoadedAssemblies(Harmony harmony)
    {
        var patchCount = 0;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            patchCount += PatchAssembly(harmony, assembly);
        }

        return patchCount;
    }

    private static int PatchAssembly(Harmony harmony, Assembly assembly)
    {
        var patchCount = 0;
        patchCount += PatchMethods(
            harmony,
            assembly,
            "BeatLeader.UI.Replayer.QuickSettingsPanel",
            "SetShown",
            nameof(ForceFirstBooleanArgumentFalsePrefix));
        patchCount += PatchMethods(
            harmony,
            assembly,
            "BeatLeader.UI.Replayer.ToolbarWithSettings",
            "Setup",
            nameof(SuppressOriginalPrefix));
        patchCount += PatchMethods(
            harmony,
            assembly,
            "BeatLeader.UI.Replayer.ToolbarWithSettings",
            "Present",
            nameof(SuppressOriginalPrefix));
        patchCount += PatchMethods(
            harmony,
            assembly,
            "BeatLeader.UI.Replayer.ReplayerSettingsPanel",
            "Setup",
            nameof(SuppressOriginalPrefix));
        patchCount += PatchMethods(
            harmony,
            assembly,
            "BeatLeader.UI.Replayer.SettingsUIView",
            "Setup",
            nameof(SuppressOriginalPrefix));
        patchCount += PatchMethods(
            harmony,
            assembly,
            "BeatLeader.UI.Replayer.Desktop.ReplayerDesktopUIBinder",
            "SetupUI",
            nameof(SuppressOriginalPrefix));
        patchCount += PatchMethods(
            harmony,
            assembly,
            "BeatLeader.UI.Replayer.ReplayerUIBinder",
            "RefreshUIVisibility",
            nameof(SuppressReplayUiVisibilityPrefix));
        patchCount += PatchMethods(
            harmony,
            assembly,
            "BeatLeader.Components.ReplayerSettingsPanel",
            "SetActive",
            nameof(ForceFirstBooleanArgumentFalsePrefix));
        patchCount += PatchMethods(
            harmony,
            assembly,
            "BeatLeader.Components.ReplayerSettingsPanel",
            "set_Active",
            nameof(ForceFirstBooleanArgumentFalsePrefix));

        return patchCount;
    }

    private static int PatchMethods(
        Harmony harmony,
        Assembly assembly,
        string typeName,
        string methodName,
        string prefixName)
    {
        var type = assembly.GetType(typeName, throwOnError: false);
        if (type == null)
        {
            return 0;
        }

        var prefix = typeof(BeatLeaderReplayUiSuppressor).GetMethod(
            prefixName,
            BindingFlags.NonPublic | BindingFlags.Static);
        if (prefix == null)
        {
            return 0;
        }

        var patchCount = 0;
        foreach (var method in type.GetMethods(AnyInstanceMethodFlags))
        {
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) ||
                method.IsAbstract ||
                method.ContainsGenericParameters)
            {
                continue;
            }

            patchCount += PatchMethod(harmony, method, prefix);
        }

        return patchCount;
    }

    private static int PatchMethod(Harmony harmony, MethodInfo method, MethodInfo prefix)
    {
        var patchKey = method.Module.ModuleVersionId.ToString("N") + ":" + method.MetadataToken;
        if (!PatchedMethods.Add(patchKey))
        {
            return 0;
        }

        try
        {
            harmony.Patch(method, prefix: new HarmonyMethod(prefix));
            return 1;
        }
        catch (Exception ex)
        {
            PatchedMethods.Remove(patchKey);
            _logger?.Warn("Failed to patch BeatLeader replay UI method " + FormatMethod(method) + ": " + ex.Message);
            return 0;
        }
    }

    private static string FormatMethod(MethodInfo method)
    {
        return method.DeclaringType?.FullName + "." + method.Name;
    }

    private static bool ForceFirstBooleanArgumentFalsePrefix([HarmonyArgument(0)] ref bool value)
    {
        value = false;
        return true;
    }

    private static bool SuppressOriginalPrefix()
    {
        return false;
    }

    private static bool SuppressReplayUiVisibilityPrefix(object __instance)
    {
        try
        {
            foreach (var method in __instance.GetType().GetMethods(AnyInstanceMethodFlags))
            {
                if (!string.Equals(method.Name, "SetUIEnabled", StringComparison.Ordinal))
                {
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                {
                    method.Invoke(__instance, new object[] { false });
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn("Failed to force BeatLeader replay UI hidden: " + ex.Message);
        }

        return false;
    }
}
