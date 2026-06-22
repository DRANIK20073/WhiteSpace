using System.Windows;
using System.Windows.Controls;
using WhiteSpace;
using WhiteSpace.Helpers;
using WhiteSpace.Services;

namespace WhiteSpace.Pages;

/// <summary>Настройки профиля, темы и поведения интерфейса.</summary>
public partial class UserSettingsPage : Page
{
    private readonly SupabaseService _service = new();
    private AppPreferences _preferences = new();
    private bool _isThemeInitializing;

    public UserSettingsPage()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            AccountBanGuard.Stop();
            _isThemeInitializing = true;
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (await _service.EnforceBanLogoutIfNeededAsync())
        {
            return;
        }

        AccountBanGuard.Start();

        _preferences = AppPreferences.Load();
        WhiteSpaceThemeManager.Apply(_preferences);
        _isThemeInitializing = true;
        CompactViewCheckBox.IsChecked = _preferences.UseCompactView;
        ConfirmLogoutCheckBox.IsChecked = _preferences.ConfirmBeforeLogout;
        AnimationsCheckBox.IsChecked = _preferences.EnableAnimations;

        SyncThemeComboFromPreferences();
        _isThemeInitializing = false;

        UiAnimationHelper.ApplyFadeIn(SettingsRootGrid, _preferences.EnableAnimations, force: true);

        var profile = await _service.GetMyProfileAsync();
        UsernameBox.Text = profile?.Username ?? string.Empty;
        EmailBox.Text = profile?.Email ?? string.Empty;
        EmailBox.IsReadOnly = true;
        EmailBox.Focusable = false;

        _isThemeInitializing = true;
        SyncThemeComboFromPreferences();
        _isThemeInitializing = false;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _isThemeInitializing = true;
        AppNavigation.NavigateHome(NavigationService);
    }

    private void Help_Click(object sender, RoutedEventArgs e) =>
        HelpService.Show(Window.GetWindow(this), "profile");

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

    /// <summary>Синхронизируем комбобокс темы с файлом настроек (не с runtime-состоянием).</summary>
    private void SyncThemeComboFromPreferences()
    {
        // Всегда опираемся на сохранённый файл, а не на IsDarkApplied — иначе после навигации возможна рассинхронизация и перезапись темы на Light.
        var isDark = string.Equals(_preferences.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
        ThemeComboBox.SelectedIndex = isDark ? 1 : 0;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isThemeInitializing || !IsLoaded)
        {
            return;
        }

        if (e.AddedItems.Count == 0 || ThemeComboBox.SelectedIndex < 0)
        {
            return;
        }

        var selectedTheme = ThemeComboBox.SelectedIndex == 1 ? "Dark" : "Light";
        if (!AppPreferences.MutateAndSave(p =>
            {
                p.Theme = selectedTheme;
                p.UseCompactView = CompactViewCheckBox.IsChecked == true;
                p.ConfirmBeforeLogout = ConfirmLogoutCheckBox.IsChecked == true;
                p.EnableAnimations = AnimationsCheckBox.IsChecked == true;
            }, out var saveErr))
        {
            AppDialogService.ShowError(
                string.IsNullOrWhiteSpace(saveErr)
                    ? "Не удалось сохранить файл настроек (тема)."
                    : $"Не удалось сохранить тему: {saveErr}",
                "Настройки");
            return;
        }

        _preferences = AppPreferences.Load();
        WhiteSpaceThemeManager.Apply(_preferences);
    }

    private void SavePreferences_Click(object sender, RoutedEventArgs e)
    {
        if (!AppPreferences.MutateAndSave(p =>
            {
                p.UseCompactView = CompactViewCheckBox.IsChecked == true;
                p.ConfirmBeforeLogout = ConfirmLogoutCheckBox.IsChecked == true;
                p.EnableAnimations = AnimationsCheckBox.IsChecked == true;
                p.Theme = ThemeComboBox.SelectedIndex == 1 ? "Dark" : "Light";
            }, out var err))
        {
            AppDialogService.ShowError(
                string.IsNullOrWhiteSpace(err)
                    ? "Не удалось сохранить файл настроек."
                    : $"Не удалось сохранить настройки: {err}",
                "Настройки");
            return;
        }

        _preferences = AppPreferences.Load();
        SyncThemeComboFromPreferences();
        WhiteSpaceThemeManager.Apply(_preferences);

        AppDialogService.ShowSuccess("Настройки интерфейса сохранены.", "Настройки");
    }

    private void ClearRecentBoards_Click(object sender, RoutedEventArgs e)
    {
        UserHomePage.ClearRecentBoards();
        AppDialogService.ShowSuccess("История недавних досок очищена.", "Настройки");
    }

    private void SettingsRootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        SettingsRootGrid.Margin = new Thickness(width < AdaptiveLayout.TierNarrow ? 12 : 24);
    }
}
