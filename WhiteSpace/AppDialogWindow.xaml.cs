using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace WhiteSpace
{
    /// <summary>Кастомный диалог вместо MessageBox — тип, цвета и две кнопки.</summary>
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

        /// <summary>Раскрашиваем полосу и иконку по типу сообщения.</summary>
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
                    ApplyInfoDialogChromeFromTheme();
                    return;
            }

            AccentBar.Background = CreateBrush(accentColor);
            IconBadge.Background = CreateBrush(badgeColor);
            IconText.Foreground = CreateBrush(accentColor);
            IconText.Text = icon;
        }

        /// <summary>Обычный диалог (инфо): полоса и значок из палитры текущей темы.</summary>
        private void ApplyInfoDialogChromeFromTheme()
        {
            var app = Application.Current;
            AccentBar.Background = app?.TryFindResource("WsPurpleBrush") as Brush ?? CreateBrush("#8B5CF6");
            IconBadge.Background = app?.TryFindResource("WsSurfaceMutedBrush") as Brush ?? CreateBrush("#F2F2F2");
            IconText.Foreground = app?.TryFindResource("WsPurpleBrush") as Brush ?? CreateBrush("#333333");
            IconText.Text = "i";
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
