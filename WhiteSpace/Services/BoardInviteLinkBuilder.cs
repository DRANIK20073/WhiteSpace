using System;

namespace WhiteSpace.Services;

public static class BoardInviteLinkBuilder
{
    /// <summary>Ссылка для обработчика протокола whitespace:// (регистрация в системе).</summary>
    public static string BuildInviteLink(string accessCode)
    {
        if (string.IsNullOrWhiteSpace(accessCode))
        {
            return string.Empty;
        }

        return $"whitespace://join?code={Uri.EscapeDataString(accessCode.Trim().ToUpperInvariant())}";
    }
}
