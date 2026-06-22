using System;
using System.Threading.Tasks;
using System.Windows.Threading;
namespace WhiteSpace.Services;

/// <summary>Периодически проверяет блокировку и выкидывает пользователя из приложения.</summary>
public static class AccountBanGuard
{
    private static readonly SupabaseService Service = new();
    private static DispatcherTimer? _timer;
    private static bool _checkInProgress;

    /// <summary>Запускает фоновую проверку бана каждые 3 секунды.</summary>
    public static void Start()
    {
        Stop();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    /// <summary>Останавливает таймер проверки (при выходе из аккаунта).</summary>
    public static void Stop()
    {
        if (_timer == null)
        {
            return;
        }

        _timer.Tick -= Timer_Tick;
        _timer.Stop();
        _timer = null;
    }

    /// <summary>Один тик таймера — не наслаиваем параллельные проверки.</summary>
    private static async void Timer_Tick(object? sender, EventArgs e)
    {
        if (_checkInProgress)
        {
            return;
        }

        _checkInProgress = true;
        try
        {
            await Service.EnforceBanLogoutIfNeededAsync();
        }
        catch
        {
            // ignore transient network errors
        }
        finally
        {
            _checkInProgress = false;
        }
    }
}
