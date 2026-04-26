using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace WhiteSpace
{
    public partial class AppTextInputWindow : Window
    {
        public string InputText => InputTextBox.Text.Trim();

        public AppTextInputWindow(
            string title,
            string message,
            string confirmText,
            string cancelText,
            string initialValue = "")
        {
            InitializeComponent();
            Owner = Application.Current?.MainWindow;

            TitleText.Text = title;
            MessageText.Text = message;
            ConfirmButton.Content = confirmText;
            CancelButton.Content = cancelText;
            InputTextBox.Text = initialValue;
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

            InputTextBox.Focus();
            InputTextBox.SelectAll();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
