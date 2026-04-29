using System.Windows;
using System.Windows.Media.Animation;
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
                    accentColor = "#8B5CF6";
                    badgeColor = "#F5F3FF";
                    icon = "OK";
                    break;
                case Services.AppDialogType.Warning:
                    accentColor = "#3B82F6";
                    badgeColor = "#EFF6FF";
                    icon = "!";
                    break;
                case Services.AppDialogType.Error:
                    accentColor = "#DC2626";
                    badgeColor = "#FEF2F2";
                    icon = "X";
                    break;
                case Services.AppDialogType.Question:
                    accentColor = "#3B82F6";
                    badgeColor = "#EFF6FF";
                    icon = "?";
                    break;
                default:
                    accentColor = "#1E293B";
                    badgeColor = "#F1F5F9";
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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(220)
            };

            var slideIn = new DoubleAnimation
            {
                From = 18,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(240),
                EasingFunction = new PowerEase
                {
                    Power = 3,
                    EasingMode = EasingMode.EaseOut
                }
            };

            BeginAnimation(OpacityProperty, fadeIn);
            DialogTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideIn);
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
