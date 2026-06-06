using System;
using System.Linq;
using System.Reflection;
using IPA.Logging;
using UnityEngine;

namespace BSAutoReplayRecorder.Plugin;

internal static class ActiveReplayTerminator
{
    public static void RequestLeaveActiveReplay(IPA.Logging.Logger logger)
    {
        try
        {
            var pauseMenuType = FindType("PauseMenuManager");
            if (pauseMenuType == null)
            {
                logger.Warn("Could not leave active replay because PauseMenuManager was not found.");
                return;
            }

            var managers = Resources.FindObjectsOfTypeAll(pauseMenuType);
            if (managers == null || managers.Length == 0)
            {
                logger.Warn("Could not leave active replay because no PauseMenuManager instance was found.");
                return;
            }

            var requestedExit = false;
            foreach (var manager in managers)
            {
                InvokeNoArgumentMethod(pauseMenuType, manager, "ShowMenu", logger);
                requestedExit |= InvokeFirstNoArgumentMethod(
                    pauseMenuType,
                    manager,
                    new[]
                    {
                        "HandleMenuButtonPressed",
                        "MenuButtonPressed",
                        "BackButtonWasPressed",
                        "HandleBackButtonWasPressed",
                        "HandleQuitButtonPressed"
                    },
                    logger);
            }

            if (requestedExit)
            {
                logger.Warn("Requested active replay exit through PauseMenuManager.");
            }
            else
            {
                logger.Warn("Opened pause menu, but no compatible pause-menu exit method was found.");
            }
        }
        catch (Exception ex)
        {
            logger.Error("Failed to request active replay exit: " + ex);
        }
    }

    private static bool InvokeFirstNoArgumentMethod(
        Type type,
        object instance,
        string[] methodNames,
        IPA.Logging.Logger logger)
    {
        foreach (var methodName in methodNames)
        {
            if (InvokeNoArgumentMethod(type, instance, methodName, logger))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InvokeNoArgumentMethod(
        Type type,
        object instance,
        string methodName,
        IPA.Logging.Logger logger)
    {
        var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, methodName, StringComparison.Ordinal) &&
                candidate.GetParameters().Length == 0);
        if (method == null)
        {
            return false;
        }

        method.Invoke(instance, null);
        logger.Info("Invoked PauseMenuManager." + methodName + " for active replay exit.");
        return true;
    }

    private static Type? FindType(string typeName)
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType(typeName, throwOnError: false))
            .FirstOrDefault(type => type != null);
    }
}
