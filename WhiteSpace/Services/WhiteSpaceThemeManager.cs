using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WhiteSpace.Services;

public static class WhiteSpaceThemeManager
{
    private static ResourceDictionary? _paletteDictionary;
    public static bool HasAppliedTheme { get; private set; }
    public static bool IsDarkApplied { get; private set; }

    public static bool IsDarkTheme(AppPreferences preferences) =>
        string.Equals(preferences.Theme, "Dark", StringComparison.OrdinalIgnoreCase);

    /// <summary>Применяет сохранённую тему при старте или после изменения настроек.</summary>
    public static void Apply(AppPreferences preferences)
    {
        Apply(IsDarkTheme(preferences));
    }

    public static void Apply(bool dark)
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        var merged = app.Resources.MergedDictionaries;

        if (_paletteDictionary != null)
        {
            merged.Remove(_paletteDictionary);
            _paletteDictionary = null;
        }

        ApplicationThemeManager.Apply(
            dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.None,
            updateAccent: true);

        // Палитра WhiteSpace — последней, чтобы Ws* и алиасы к ComboBox* не затирали шаблон Fluent,
        // а только переопределяли цвета поверх темы WPF UI.
        var packPath = dark
            ? "pack://application:,,,/Theme/WhiteSpacePalette.Dark.xaml"
            : "pack://application:,,,/Theme/WhiteSpacePalette.Light.xaml";

        _paletteDictionary = new ResourceDictionary { Source = new Uri(packPath, UriKind.Absolute) };
        merged.Add(_paletteDictionary);
        HasAppliedTheme = true;
        IsDarkApplied = dark;

        foreach (Window window in app.Windows)
        {
            RefreshWindowChrome(window);
        }
    }

    public static void RefreshWindowChrome(Window? window)
    {
        if (window == null)
        {
            return;
        }

        try
        {
            if (Application.Current?.TryFindResource("WsPageBgBrush") is not Brush brush)
            {
                return;
            }

            window.Background = brush;
            if (window.FindName("WindowRoot") is Grid rootGrid)
            {
                rootGrid.Background = brush;
            }
        }
        catch
        {
            // безопасное обновление фона окна
        }
    }
}
