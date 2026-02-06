using System.Windows;
using System.Windows.Controls;

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
            // Переход на новую страницу
            NavigationService.Navigate(page);

            // Убираем текущую страницу из истории навигации
            while (NavigationService.CanGoBack)
            {
                NavigationService.RemoveBackEntry();
            }
        }

    }
}
