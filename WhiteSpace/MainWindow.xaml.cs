using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WpfApp2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SupabaseService _supabaseService;

        public MainWindow()
        {
            InitializeComponent();
            _supabaseService = new SupabaseService();
            SupabaseService.InitAsync().Wait();
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string email = emailTextBox.Text;
            string password = passwordTextBox.Password;
            await _supabaseService.SignUpAsync(email, password); // Передаем имя пользователя в метод
        }

        private async void UpdateUsernameButton_Click(object sender, RoutedEventArgs e)
        {
            // Получаем новое имя пользователя из текстового поля
            string newUsername = newUsernameTextBox.Text;

            // Если новое имя пользователя пустое, показываем ошибку
            if (string.IsNullOrWhiteSpace(newUsername))
            {
                MessageBox.Show("Пожалуйста, введите новое имя пользователя.");
                return;
            }

            // Вызываем метод для обновления имени пользователя
            await _supabaseService.UpdateUsernameAsync(newUsername);
        }

        private async void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            string email = emailTextBox.Text;
            string password = passwordTextBox.Password;
            await _supabaseService.SignInAsync(email, password);
        }

        private async void GetUserButton_Click(object sender, RoutedEventArgs e)
        {
            // Вызов метода для получения текущего пользователя
            await _supabaseService.GetCurrentUserAsync();
        }


    }


}