using System.Windows;

namespace WhiteSpace.Services;

/// <summary>Окно справки: одно на приложение, повторный вызов просто активирует его.</summary>
public static class HelpService
{
    private static HelpWindow? _open;

    /// <summary>Открывает окно справки; повторный вызов активирует уже открытое.</summary>
    public static void Show(Window? owner, string? initialTopicId = null)
    {
        if (_open != null)
        {
            if (initialTopicId != null)
                _open.SelectTopic(initialTopicId);
            _open.Activate();
            return;
        }

        var w = new HelpWindow { Owner = owner };
        if (initialTopicId != null)
            w.SelectTopic(initialTopicId);
        w.Closed += (_, _) => _open = null;
        _open = w;
        w.Show();
    }
}
