using System.Linq;

namespace WhiteSpace.Services;

public static class InviteLaunchArgs
{
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
