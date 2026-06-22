using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WhiteSpace.Services;

/// <summary>Регистрирует обработчик ссылок whitespace:// в HKCU (без прав администратора).</summary>
public static class InviteProtocolRegistration
{
    public const string Scheme = "whitespace";

    /// <summary>Прописывает whitespace:// в реестре, если exe найден.</summary>
    public static void EnsureRegistered()
    {
        try
        {
            var exePath = ResolveExecutablePath();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return;
            }

            var command = $"\"{exePath}\" \"%1\"";
            using var protocolKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}", true);
            if (protocolKey == null)
            {
                return;
            }

            protocolKey.SetValue(string.Empty, $"URL:{Scheme} Protocol");
            protocolKey.SetValue("URL Protocol", string.Empty);

            using var commandKey = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\{Scheme}\shell\open\command",
                true);
            commandKey?.SetValue(string.Empty, command);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Invite protocol registration: {ex.Message}");
        }
    }

    /// <summary>Путь к текущему exe: ProcessPath или MainModule.</summary>
    private static string? ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath) &&
            File.Exists(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            var main = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(main) && File.Exists(main))
            {
                return main;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }
}
