using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WhiteSpace;
using WhiteSpace.Services;

namespace WhiteSpace.Pages
{
    public partial class LoginPage : Page
    {
        private readonly SupabaseService _supabaseService;

        public LoginPage()
        {
            InitializeComponent();
            _supabaseService = new SupabaseService();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var prefs = AppPreferences.Load();
            UiAnimationHelper.ApplyFadeIn(AuthRootGrid, prefs.EnableAnimations);
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            if (await _supabaseService.TryAdminLoginAsync(EmailBox.Text, PasswordBox.Password))
            {
                AppDialogService.ShowSuccess("Вход администратора выполнен.", "Админка");
                NavigateAndClear(new AdminPage());
                return;
            }

            bool success = await _supabaseService.SignInAsync(
                EmailBox.Text,
                PasswordBox.Password,
                RememberMeCheckBox.IsChecked == true
            );

            if (success)
            {
                var isAdmin = await _supabaseService.IsCurrentUserAdminAsync();
                NavigateAndClear(isAdmin ? new AdminPage() : new UserHomePage());
            }
        }

        private void ForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            NavigateAndClear(new ForgotPasswordPage(EmailBox.Text));
        }

        private void ClearTextBox_Click(object sender, RoutedEventArgs e)
        {
            EmailBox.Text = string.Empty;
            EmailBox.Focus();
        }

        private void ClearPasswordBox_Click(object sender, RoutedEventArgs e)
        {
            PasswordBox.Password = string.Empty;
            PasswordBox.Focus();
        }

        private void RegisterLink_Click(object sender, RoutedEventArgs e)
        {
            NavigateAndClear(new RegisterPage());
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Login_Click(sender, e);
            }
        }

        private void NavigateAndClear(Page page)
        {
            var navigationService = NavigationService
                ?? (Application.Current.MainWindow as WhiteSpace.MainWindow)?.MainFrame.NavigationService;

            if (navigationService == null)
            {
                AppDialogService.ShowError("Не удалось выполнить переход на другую страницу.", "Ошибка навигации");
                return;
            }

            navigationService.Navigate(page);

            while (navigationService.CanGoBack)
            {
                navigationService.RemoveBackEntry();
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Login_Click(sender, e);
            }
        }

        private async void GoogleLogin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = false;
                    button.Content = "Авторизация...";
                }

                bool success = await _supabaseService.GoogleSignInAsync(this);

                if (!success)
                {
                    AppDialogService.ShowWarning(
                        "Не удалось выполнить вход через Google.\n\nПопробуйте позже или используйте вход по email.",
                        "Вход через Google");

                    if (button != null)
                    {
                        button.IsEnabled = true;
                        button.Content = "Войти через Google";
                    }
                }
            }
            catch (Exception ex)
            {
                AppDialogService.ShowError($"Ошибка: {ex.Message}", "Вход через Google");

                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = true;
                    button.Content = "Войти через Google";
                }
            }
        }
    }
}
