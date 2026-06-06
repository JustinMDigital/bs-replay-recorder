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
            var requestedExit = InvokeReturnToMenuFromPauseControllers(logger) ||
                                InvokeReturnToMenuControllers(logger);
            if (requestedExit)
            {
                logger.Warn("Requested active replay exit through ReturnToMenuController.");
                return;
            }

            logger.Warn("No ReturnToMenuController was available; falling back to PauseMenuManager.");
            requestedExit = InvokePauseMenuExit(logger);
            if (requestedExit)
            {
                logger.Warn("Requested active replay exit through PauseMenuManager.");
            }
            else
            {
                logger.Warn("Could not find a compatible active replay exit hook.");
            }
        }
        catch (Exception ex)
        {
            logger.Error("Failed to request active replay exit: " + ex);
        }
    }

    private static bool InvokeReturnToMenuFromPauseControllers(IPA.Logging.Logger logger)
    {
        var pauseControllerType = FindType("PauseController");
        if (pauseControllerType == null)
        {
            return false;
        }

        var controllers = Resources.FindObjectsOfTypeAll(pauseControllerType);
        if (controllers == null || controllers.Length == 0)
        {
            return false;
        }

        var returnToMenuField = pauseControllerType.GetField(
            "_returnToMenuController",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (returnToMenuField == null)
        {
            return false;
        }

        var invoked = false;
        foreach (var controller in controllers)
        {
            var returnToMenuController = returnToMenuField.GetValue(controller);
            if (returnToMenuController == null)
            {
                continue;
            }

            invoked |= InvokeReturnToMenu(returnToMenuController, logger, "PauseController._returnToMenuController");
        }

        return invoked;
    }

    private static bool InvokeReturnToMenuControllers(IPA.Logging.Logger logger)
    {
        var invoked = false;
        foreach (var typeName in new[]
                 {
                     "StandardLevelReturnToMenuController",
                     "MissionLevelReturnToMenuController",
                     "TutorialReturnToMenuController"
                 })
        {
            var controllerType = FindType(typeName);
            if (controllerType == null)
            {
                continue;
            }

            var controllers = Resources.FindObjectsOfTypeAll(controllerType);
            if (controllers == null || controllers.Length == 0)
            {
                continue;
            }

            foreach (var controller in controllers)
            {
                invoked |= InvokeReturnToMenu(controller, logger, typeName);
            }
        }

        return invoked;
    }

    private static bool InvokeReturnToMenu(
        object controller,
        IPA.Logging.Logger logger,
        string source)
    {
        var method = controller.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "ReturnToMenu", StringComparison.Ordinal) &&
                candidate.GetParameters().Length == 0);
        if (method == null)
        {
            return false;
        }

        method.Invoke(controller, null);
        logger.Info("Invoked " + source + ".ReturnToMenu for active replay exit.");
        return true;
    }

    private static bool InvokePauseMenuExit(IPA.Logging.Logger logger)
    {
        try
        {
            var pauseMenuType = FindType("PauseMenuManager");
            if (pauseMenuType == null)
            {
                logger.Warn("Could not leave active replay because PauseMenuManager was not found.");
                return false;
            }

            var managers = Resources.FindObjectsOfTypeAll(pauseMenuType);
            if (managers == null || managers.Length == 0)
            {
                logger.Warn("Could not leave active replay because no PauseMenuManager instance was found.");
                return false;
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

            return requestedExit;
        }
        catch (Exception ex)
        {
            logger.Error("Failed to request active replay exit through PauseMenuManager: " + ex);
            return false;
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
        logger.Info("Invoked " + type.Name + "." + methodName + " for active replay exit.");
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
