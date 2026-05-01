using System.Windows;
using WhiteSpace.Pages;
using WhiteSpace.Services;

namespace WhiteSpace
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var prefs = AppPreferences.Load();
            UiAnimationHelper.ApplyFadeIn(WindowRoot, prefs.EnableAnimations);
        }
    }
}
