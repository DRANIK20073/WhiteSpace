using System;

namespace WhiteSpace.Services;

/// <summary>Событийное уведомление на главной странице (справа сверху).</summary>
public static class HomeToastService
{
    public static event Action<string>? ToastRequested;

    public static void Show(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        ToastRequested?.Invoke(message.Trim());
    }
}
