using System.Windows;
using System.Windows.Input;
using WhiteSpace.Services;

namespace WhiteSpace
{
    /// <summary>Диалог «Поделиться доской»: код доступа и invite-ссылка в буфер.</summary>
    public partial class BoardShareWindow : Window
    {
        private readonly string _code;
        private readonly string _link;

        public BoardShareWindow(string accessCode, string inviteLink)
        {
            InitializeComponent();

            Owner = Application.Current?.MainWindow;
            _code = accessCode;
            _link = inviteLink;

            CodeDisplayText.Text = accessCode;
            LinkDisplayText.Text = string.IsNullOrEmpty(inviteLink) ? "—" : inviteLink;
        }

        private void CopyCode_Click(object sender, RoutedEventArgs e)
        {
            TryCopy(_code);
        }

        private void InviteLinkArea_Click(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_link))
            {
                AppDialogService.ShowWarning("Ссылка недоступна.", "Поделиться доской");
                return;
            }

            TryCopy(_link);
        }

        /// <summary>Копируем в буфер; при ошибке показываем warning.</summary>
        private static void TryCopy(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
                AppDialogService.ShowWarning("Не удалось скопировать в буфер обмена.", "Поделиться доской");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
