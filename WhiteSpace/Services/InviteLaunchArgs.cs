using System;
using System.Linq;

namespace WhiteSpace.Services;

/// <summary>Разбор аргументов запуска: мульти-инстанс и код приглашения из ссылки.</summary>
public static class InviteLaunchArgs
{
    /// <summary>
    /// Разрешить несколько полноценных процессов (второй запуск не завершается сразу).
    /// Аргументы: <c>--multi-instance</c>, <c>-multi</c>.
    /// Переменная окружения: <c>WHITESPACE_ALLOW_MULTI_INSTANCE=1</c>.
    /// В конфигурации <c>Debug</c> по умолчанию включено; отключить: <c>--single-instance</c>.
    /// </summary>
    public static bool AllowsMultipleInstancesForCurrentProcess(string[]? startupArgs)
    {
        if (AllowsMultipleInstancesFromEnvironment())
        {
            return true;
        }

        if (ContainsMultiInstanceFlag(ParseArgs(startupArgs)))
        {
            return true;
        }

        try
        {
            var cla = Environment.GetCommandLineArgs();
            if (cla.Length > 1 && ContainsMultiInstanceFlag(cla.AsSpan(1)))
            {
                return true;
            }
        }
        catch
        {
            // ignore
        }

#if DEBUG
        if (!ContainsForceSingleInstanceFlag(startupArgs))
        {
            try
            {
                var cla = Environment.GetCommandLineArgs();
                if (cla.Length <= 1 || !ContainsForceSingleInstanceFlag(cla.AsSpan(1)))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }
#endif

        return false;
    }

    private static bool AllowsMultipleInstancesFromEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("WHITESPACE_ALLOW_MULTI_INSTANCE");
        return !string.IsNullOrWhiteSpace(env) &&
               (env == "1" || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase));
    }

    private static ReadOnlySpan<string> ParseArgs(string[]? args) =>
        args == null || args.Length == 0 ? ReadOnlySpan<string>.Empty : args.AsSpan();

    private static bool ContainsMultiInstanceFlag(ReadOnlySpan<string> args)
    {
        foreach (var raw in args)
        {
            var a = raw.Trim().Trim('"');
            if (a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(a, "--multi-instance", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-multi", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsForceSingleInstanceFlag(ReadOnlySpan<string> args)
    {
        foreach (var raw in args)
        {
            var a = raw.Trim().Trim('"');
            if (a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(a, "--single-instance", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsForceSingleInstanceFlag(string[]? args) =>
        args != null && ContainsForceSingleInstanceFlag(args.AsSpan());

    /// <summary>Извлекает код доступа из аргументов командной строки (URI или код).</summary>
    public static string? TryParseInviteCode(string[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return null;
        }

        foreach (var raw in args)
        {
            var a = raw.Trim().Trim('"');
            if (a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var code = BoardInviteLinkParser.TryGetAccessCode(a);
            if (!string.IsNullOrEmpty(code))
            {
                return code;
            }
        }

        var joined = string.Join(" ", args.Select(x => x.Trim().Trim('"')));
        return BoardInviteLinkParser.TryGetAccessCode(joined);
    }
}
