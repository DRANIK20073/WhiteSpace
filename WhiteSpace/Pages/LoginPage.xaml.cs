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
            string email = EmailBox.Text; 
            string password = PasswordBox.Password; 

            bool result = await _supabaseService.SignInAsync(email, password);

            if (result)
            {
                NavigateAndClear(new UserHomePage());
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
