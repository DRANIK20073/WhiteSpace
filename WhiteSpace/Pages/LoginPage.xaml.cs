using Supabase;
using Supabase.Gotrue;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfButton = System.Windows.Controls.Button;

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

        private void ForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            WpfMessageBox.Show(
                "Функция восстановления пароля будет доступна позже.\n\n" +
                "Пожалуйста, обратитесь в службу поддержки или используйте вход через Google.",
                "Восстановление пароля",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Information);
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

        // Обработчик нажатия Enter в поле пароля
        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Login_Click(sender, e);
            }
        }

        private void NavigateAndClear(Page page)
        {
            NavigationService.Navigate(page);
            while (NavigationService.CanGoBack)
            {
                NavigationService.RemoveBackEntry();
            }
        }

        // Обработчик нажатия Enter в поле email
        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Login_Click(sender, e);
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
