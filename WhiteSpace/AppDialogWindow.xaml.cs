using System.Windows;
using System.Windows.Media;

namespace WhiteSpace
{
    public partial class AppDialogWindow : Window
    {
        public AppDialogWindow(
            string title,
            string message,
            Services.AppDialogType type,
            string primaryButtonText = "Понятно",
            string? secondaryButtonText = null)
        {
            InitializeComponent();

            Owner = Application.Current?.MainWindow;
            TitleText.Text = title;
            MessageText.Text = message;
            PrimaryButton.Content = primaryButtonText;

            if (!string.IsNullOrWhiteSpace(secondaryButtonText))
            {
                SecondaryButton.Content = secondaryButtonText;
                SecondaryButton.Visibility = Visibility.Visible;
            }

            ApplyType(type);
        }

        private void ApplyType(Services.AppDialogType type)
        {
            string accentColor;
            string badgeColor;
            string icon;

            switch (type)
            {
                case Services.AppDialogType.Success:
                    accentColor = "#2E7D32";
                    badgeColor = "#EAF6EC";
                    icon = "OK";
                    break;
                case Services.AppDialogType.Warning:
                    accentColor = "#C47F00";
                    badgeColor = "#FFF5DE";
                    icon = "!";
                    break;
                case Services.AppDialogType.Error:
                    accentColor = "#C62828";
                    badgeColor = "#FDECEC";
                    icon = "X";
                    break;
                case Services.AppDialogType.Question:
                    accentColor = "#1565C0";
                    badgeColor = "#EAF2FD";
                    icon = "?";
                    break;
                default:
                    accentColor = "#333333";
                    badgeColor = "#F2F2F2";
                    icon = "i";
                    break;
            }

            AccentBar.Background = CreateBrush(accentColor);
            IconBadge.Background = CreateBrush(badgeColor);
            IconText.Foreground = CreateBrush(accentColor);
            IconText.Text = icon;
        }

        private static SolidColorBrush CreateBrush(string hex)
        {
            return new BrushConverter().ConvertFromString(hex) as SolidColorBrush
                ?? Brushes.Gray;
        }

        private void PrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void SecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
