using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WhiteSpace.Pages
{
    public partial class RegisterPage : Page
    {
        private SupabaseService _supabaseService;

        public RegisterPage()
        {
            InitializeComponent();
            _supabaseService = new SupabaseService();
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
                MessageBox.Show("Не удалось зарегистрироваться. Проверьте введенные данные.");
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
