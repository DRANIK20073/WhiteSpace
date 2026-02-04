using System.Windows;
using System.Windows.Controls;

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
    }
}
