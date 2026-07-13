using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Models;
using BeatLeader.Models.Replay;
using BeatLeader.Replayer;

namespace BSAutoReplayRecorder.Plugin;

internal static class BeatLeaderCompatibility
{
    public static async Task StartReplayAsync(
        ReplayerMenuLoader loader,
        Replay replay,
        ReplayerSettings settings,
        Action? finishedCallback,
        CancellationToken cancellationToken)
    {
        if (loader == null)
        {
            throw new ArgumentNullException(nameof(loader));
        }

        var method = FindStartReplayAsync(loader.GetType());
        var player = CreateReplayPlayer(replay);
        var args = BuildStartReplayArguments(method, replay, player, settings, finishedCallback, cancellationToken);

        object? result;
        try
        {
            result = method.Invoke(loader, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException(
            "BeatLeader ReplayerMenuLoader.StartReplayAsync returned " +
            (result?.GetType().FullName ?? "null") +
            " instead of Task.");
    }

    public static (bool LevelFound, bool KeyFound) InspectBeatmapLoadResult(object? loadResult)
    {
        if (loadResult == null)
        {
            return (false, false);
        }

        if (TryGetBooleanMember(loadResult, "HasValue", out var hasValue) && !hasValue)
        {
            return (false, false);
        }

        var level = GetMemberValue(loadResult, "Level") ?? GetMemberValue(loadResult, "Item1");
        var key = GetMemberValue(loadResult, "Key") ?? GetMemberValue(loadResult, "Item2");

        return (level != null, key != null);
    }

    public static bool TryExtractBeatmapLevelWithKey(
        object? loadResult,
        out object level,
        out object beatmapKey)
    {
        level = null!;
        beatmapKey = null!;

        if (loadResult == null)
        {
            return false;
        }

        if (TryGetBooleanMember(loadResult, "HasValue", out var hasValue) && !hasValue)
        {
            return false;
        }

        var levelValue = GetMemberValue(loadResult, "Level") ?? GetMemberValue(loadResult, "Item1");
        var keyValue = GetMemberValue(loadResult, "Key") ?? GetMemberValue(loadResult, "Item2");
        if (levelValue == null || keyValue == null)
        {
            return false;
        }

        level = levelValue;
        beatmapKey = keyValue;
        return true;
    }

    private static MethodInfo FindStartReplayAsync(Type loaderType)
    {
        MethodInfo? best = null;
        foreach (var method in loaderType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(method.Name, "StartReplayAsync", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length < 5 ||
                !typeof(Task).IsAssignableFrom(method.ReturnType) ||
                !HasParameterAssignableFrom(parameters, typeof(Replay)) ||
                !HasParameterAssignableFrom(parameters, typeof(ReplayerSettings)) ||
                !HasParameter(parameters, typeof(Action)) ||
                !HasParameter(parameters, typeof(CancellationToken)))
            {
                continue;
            }

            if (best == null || parameters.Length > best.GetParameters().Length)
            {
                best = method;
            }
        }

        return best ?? throw new MissingMethodException(
            loaderType.FullName,
            "StartReplayAsync(Replay, ..., ReplayerSettings, Action, CancellationToken)");
    }

    private static object?[] BuildStartReplayArguments(
        MethodInfo method,
        Replay replay,
        IPlayer? player,
        ReplayerSettings settings,
        Action? finishedCallback,
        CancellationToken cancellationToken)
    {
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameterType = parameters[index].ParameterType;
            if (parameterType.IsAssignableFrom(typeof(Replay)))
            {
                args[index] = replay;
            }
            else if (typeof(IPlayer).IsAssignableFrom(parameterType))
            {
                args[index] = player;
            }
            else if (parameterType.IsAssignableFrom(typeof(ReplayerSettings)))
            {
                args[index] = settings;
            }
            else if (parameterType == typeof(Action))
            {
                args[index] = finishedCallback;
            }
            else if (parameterType == typeof(CancellationToken))
            {
                args[index] = cancellationToken;
            }
            else
            {
                args[index] = GetDefaultArgumentValue(parameterType);
            }
        }

        return args;
    }

    private static IPlayer? CreateReplayPlayer(Replay replay)
    {
        var info = replay.info;
        if (info == null || string.IsNullOrWhiteSpace(info.playerName))
        {
            return null;
        }

        return new Player
        {
            id = info.playerID ?? "",
            name = info.playerName.Trim(),
            avatar = null,
            country = "not set",
            rank = -1,
            countryRank = -1,
            pp = -1,
            role = "",
            friends = Array.Empty<string>(),
            clans = Array.Empty<Clan>(),
            socials = Array.Empty<ServiceIntegration>()
        };
    }

    private static bool HasParameter(ParameterInfo[] parameters, Type type)
    {
        foreach (var parameter in parameters)
        {
            if (parameter.ParameterType == type)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasParameterAssignableFrom(ParameterInfo[] parameters, Type type)
    {
        foreach (var parameter in parameters)
        {
            if (parameter.ParameterType.IsAssignableFrom(type))
            {
                return true;
            }
        }

        return false;
    }

    private static object? GetDefaultArgumentValue(Type parameterType)
    {
        if (!parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) != null)
        {
            return null;
        }

        return Activator.CreateInstance(parameterType);
    }

    private static object? GetMemberValue(object target, string memberName)
    {
        var targetType = target.GetType();
        var property = targetType.GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null && property.CanRead)
        {
            return property.GetValue(target, null);
        }

        return targetType
            .GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(target);
    }

    private static bool TryGetBooleanMember(object target, string memberName, out bool value)
    {
        var memberValue = GetMemberValue(target, memberName);
        if (memberValue is bool boolean)
        {
            value = boolean;
            return true;
        }

        value = false;
        return false;
    }
}
