using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WhiteSpace;
using WhiteSpace.Services;

namespace WhiteSpace.Pages
{
    /// <summary>Запрос письма для сброса пароля.</summary>
    public partial class ForgotPasswordPage : Page
    {
        private readonly SupabaseService _supabaseService;

        /// <param name="email">Можно передать email с формы входа, чтобы не вводить заново.</param>
        public ForgotPasswordPage(string? email = null)
        {
            InitializeComponent();
            _supabaseService = new SupabaseService();
            EmailBox.Text = email?.Trim() ?? string.Empty;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var prefs = AppPreferences.Load();
            WhiteSpaceThemeManager.Apply(prefs);
            UiAnimationHelper.ApplyFadeIn(AuthRootGrid, prefs.EnableAnimations);
        }

        /// <summary>Отправляем reset-link на указанный email.</summary>
        private async void SendResetLink_Click(object sender, RoutedEventArgs e)
        {
            bool success = await _supabaseService.SendPasswordResetEmailAsync(EmailBox.Text.Trim());

            if (success)
            {
                NavigateAndClear(new LoginPage());
            }
        }

        private void BackToLogin_Click(object sender, RoutedEventArgs e)
        {
            NavigateAndClear(new LoginPage());
        }

        private void EmailBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendResetLink_Click(sender, e);
            }
        }

        /// <summary>Навигация без истории — обычно обратно на LoginPage.</summary>
        private void NavigateAndClear(Page page)
        {
            var navigationService = NavigationService
                ?? (Application.Current.MainWindow as WhiteSpace.MainWindow)?.MainFrame.NavigationService;

            if (navigationService == null)
            {
                AppDialogService.ShowError("Не удалось выполнить переход на страницу входа.", "Ошибка навигации");
                return;
            }

            navigationService.Navigate(page);

            while (navigationService.CanGoBack)
            {
                navigationService.RemoveBackEntry();
            }
        }
    }
}
