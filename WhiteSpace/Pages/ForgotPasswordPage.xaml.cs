using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WhiteSpace.Services;

namespace WhiteSpace.Pages
{
    public partial class ForgotPasswordPage : Page
    {
        private readonly SupabaseService _supabaseService;

        public ForgotPasswordPage(string? email = null)
        {
            InitializeComponent();
            _supabaseService = new SupabaseService();
            EmailBox.Text = email?.Trim() ?? string.Empty;
        }

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
