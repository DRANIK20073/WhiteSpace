using System;
using System.IO;

namespace WhiteSpace.Services;

/// <summary>Передаёт код приглашения во второй экземпляр приложения (файл в AppData).</summary>
public static class InviteRelay
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WhiteSpace",
        "relay_invite.code");

    public static void WritePendingInvite(string code)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(FilePath, code.Trim().ToUpperInvariant());
        }
        catch
        {
            // не блокируем закрытие второго экземпляра
        }
    }

    public static bool TryReadAndClear(out string code)
    {
        code = string.Empty;
        try
        {
            if (!File.Exists(FilePath))
            {
                return false;
            }

            code = File.ReadAllText(FilePath).Trim();
            File.Delete(FilePath);
            return !string.IsNullOrEmpty(code);
        }
        catch
        {
            return false;
        }
    }
}
