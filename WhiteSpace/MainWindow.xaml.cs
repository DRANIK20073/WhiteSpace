using System.Windows;
using WhiteSpace.Pages;

namespace WhiteSpace
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Загружаем страницу регистрации при запуске
            MainFrame.Navigate(new RegisterPage());
        }
    }
}
