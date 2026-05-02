using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WhiteSpace;
using WhiteSpace.Services;

namespace WhiteSpace.Pages
{
    public partial class RegisterPage : Page
    {
        private readonly SupabaseService _supabaseService;

        public RegisterPage()
        {
            InitializeComponent();
            _supabaseService = new SupabaseService();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var prefs = AppPreferences.Load();
            WhiteSpaceThemeManager.Apply(prefs);
            UiAnimationHelper.ApplyFadeIn(AuthRootGrid, prefs.EnableAnimations);
        }

        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            string email = EmailBox.Text;
            string password = PasswordBox.Password;

            bool result = await _supabaseService.SignUpAsync(email, password);

            if (result)
            {
                NavigateAndClear(new LoginPage());
            }
            else
            {
                AppDialogService.ShowError("Не удалось зарегистрироваться. Проверьте введенные данные.", "Регистрация");
            }
        }

        private void LoginLink_Click(object sender, RoutedEventArgs e)
        {
            NavigateAndClear(new LoginPage());
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Register_Click(sender, e);
            }
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Register_Click(sender, e);
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
