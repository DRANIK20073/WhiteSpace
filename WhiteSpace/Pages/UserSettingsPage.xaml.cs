using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using WhiteSpace;
using WhiteSpace.Services;

namespace WhiteSpace.Pages;

public partial class UserSettingsPage : Page
{
    private readonly SupabaseService _service = new();
    private AppPreferences _preferences = new();
    private bool _isThemeInitializing;

    public UserSettingsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _preferences = AppPreferences.Load();
        _isThemeInitializing = true;
        CompactViewCheckBox.IsChecked = _preferences.UseCompactView;
        ConfirmLogoutCheckBox.IsChecked = _preferences.ConfirmBeforeLogout;
        AnimationsCheckBox.IsChecked = _preferences.EnableAnimations;

        SyncThemeComboFromPreferences();
        _isThemeInitializing = false;

        var fadeIn = _preferences.EnableAnimations;
        if (fadeIn)
        {
            Opacity = 0;
        }

        var profile = await _service.GetMyProfileAsync();
        UsernameBox.Text = profile?.Username ?? string.Empty;
        EmailBox.Text = profile?.Email ?? string.Empty;
        EmailBox.IsReadOnly = true;
        EmailBox.Focusable = false;

        SyncThemeComboFromPreferences();

        if (fadeIn)
        {
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, anim);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        NavigationService?.Navigate(new UserHomePage());
    }

    private async void SaveUsername_Click(object sender, RoutedEventArgs e)
    {
        if (await _service.UpdateUsernameAsync(UsernameBox.Text.Trim()))
        {
            AppDialogService.ShowSuccess("Имя пользователя обновлено.", "Настройки");
        }
    }

    private async void SavePassword_Click(object sender, RoutedEventArgs e)
    {
        var newPassword = NewPasswordBox.Password.Trim();
        var confirm = ConfirmPasswordBox.Password.Trim();

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            AppDialogService.ShowWarning("Введите новый пароль.", "Настройки");
            return;
        }

        if (!string.Equals(newPassword, confirm, System.StringComparison.Ordinal))
        {
            AppDialogService.ShowWarning("Пароли не совпадают.", "Настройки");
            return;
        }

        if (await _service.UpdatePasswordAsync(newPassword))
        {
            NewPasswordBox.Password = string.Empty;
            ConfirmPasswordBox.Password = string.Empty;
        }
    }

    private void SyncThemeComboFromPreferences()
    {
        var isDark = WhiteSpaceThemeManager.HasAppliedTheme
            ? WhiteSpaceThemeManager.IsDarkApplied
            : string.Equals(_preferences.Theme, "Dark", StringComparison.OrdinalIgnoreCase);

        ThemeComboBox.SelectedIndex = isDark ? 1 : 0;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isThemeInitializing || !IsLoaded)
        {
            return;
        }

        var selectedTheme = ThemeComboBox.SelectedIndex == 1 ? "Dark" : "Light";
        if (string.Equals(_preferences.Theme, selectedTheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _preferences.Theme = selectedTheme;
        if (_preferences.TrySave(out _))
        {
            WhiteSpaceThemeManager.Apply(_preferences);
        }
    }

    private void SavePreferences_Click(object sender, RoutedEventArgs e)
    {
        _preferences.UseCompactView = CompactViewCheckBox.IsChecked == true;
        _preferences.ConfirmBeforeLogout = ConfirmLogoutCheckBox.IsChecked == true;
        _preferences.EnableAnimations = AnimationsCheckBox.IsChecked == true;
        _preferences.Theme = ThemeComboBox.SelectedIndex == 1 ? "Dark" : "Light";

        if (!_preferences.TrySave(out var err))
        {
            AppDialogService.ShowError(
                string.IsNullOrWhiteSpace(err)
                    ? "Не удалось сохранить файл настроек."
                    : $"Не удалось сохранить настройки: {err}",
                "Настройки");
            return;
        }

        WhiteSpaceThemeManager.Apply(_preferences);

        AppDialogService.ShowSuccess("Настройки интерфейса сохранены.", "Настройки");
    }

    private void ClearRecentBoards_Click(object sender, RoutedEventArgs e)
    {
        UserHomePage.ClearRecentBoards();
        AppDialogService.ShowSuccess("История недавних досок очищена.", "Настройки");
    }
}
