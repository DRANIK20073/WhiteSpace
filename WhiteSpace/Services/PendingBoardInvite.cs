namespace WhiteSpace.Services;

/// <summary>Код приглашения из ссылки аргументов запуска до входа на главную.</summary>
public static class PendingBoardInvite
{
    private static readonly object Gate = new();
    private static string? _code;

    public static void Set(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        lock (Gate)
        {
            _code = code.Trim().ToUpperInvariant();
        }
    }

    public static bool TryTake(out string code)
    {
        lock (Gate)
        {
            if (_code == null)
            {
                code = string.Empty;
                return false;
            }

            code = _code;
            _code = null;
            return true;
        }
    }
}
