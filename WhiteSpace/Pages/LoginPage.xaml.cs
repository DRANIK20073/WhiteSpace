using System.Windows;
using System.Windows.Controls;
using Supabase;
using Supabase.Gotrue;
using System.Threading.Tasks;

namespace WhiteSpace.Pages
{
    public partial class LoginPage : Page
    {
        private SupabaseService _supabaseService;

        public LoginPage()
        {
            InitializeComponent();
            _supabaseService = new SupabaseService();
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            var service = new SupabaseService();

            bool success = await service.SignInAsync(
                EmailBox.Text,
                PasswordBox.Password,
                RememberMeCheckBox.IsChecked == true
            );

            if (success)
            {
                NavigationService.Navigate(new UserHomePage());
            }
        }

        private void RegisterLink_Click(object sender, RoutedEventArgs e)
        {
            NavigateAndClear(new RegisterPage());
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordBox.Password)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void NavigateAndClear(Page page)
        {
            NavigationService.Navigate(page);
            while (NavigationService.CanGoBack)
            {
                NavigationService.RemoveBackEntry();
            }
        }

        // Обработчик для кнопки Google
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

                // Вызываем обновленный метод
                bool success = await _supabaseService.GoogleSignInAsync(this);

                if (!success)
                {
                    MessageBox.Show(
                        "Не удалось выполнить вход через Google.\n\n" +
                        "Попробуйте позже или используйте вход по email.",
                        "Ошибка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    if (button != null)
                    {
                        button.IsEnabled = true;
                        button.Content = "Войти через Google";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");

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
