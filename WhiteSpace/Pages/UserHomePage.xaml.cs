using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Linq;
using System.Windows.Controls;

namespace WhiteSpace.Pages
{
    public partial class UserHomePage : Page, INotifyPropertyChanged
    {
        private string _userGreeting = "Добро пожаловать!";
        private List<Board> _boards = new List<Board>();

        public List<Board> Boards
        {
            get => _boards;
            set
            {
                _boards = value;
                OnPropertyChanged();
            }
        }

        public string UserGreeting
        {
            get => _userGreeting;
            set
            {
                _userGreeting = value;
                OnPropertyChanged();
            }
        }

        public UserHomePage()
        {
            InitializeComponent();
            DataContext = this;
            LoadUserProfile();
            LoadBoards();
        }

        private async void LoadUserProfile()
        {
            var service = new SupabaseService();
            var profile = await service.GetMyProfileAsync();

            if (profile != null && !string.IsNullOrEmpty(profile.Username))
            {
                UserGreeting = $"Здравствуйте, {profile.Username} 👋";
            }
            else
            {
                UserGreeting = "Здравствуйте!";
            }
        }

        //Загрузка списка досок
        private async void LoadBoards()
        {
            var service = new SupabaseService();
            var boardsWithRoles = await service.GetAllAccessibleBoardsWithRoleAsync();
            Boards = boardsWithRoles.Select(x => x.Board).ToList();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        //Создать доску
        private async void CreateBoard_Click(object sender, RoutedEventArgs e)
        {
            var service = new SupabaseService();

            string boardTitle = Microsoft.VisualBasic.Interaction.InputBox(
                "Введите имя для новой доски:",
                "Создание новой доски",
                "Новая доска",
                -1, -1);

            if (string.IsNullOrWhiteSpace(boardTitle))
            {
                MessageBox.Show("Название доски не может быть пустым.");
                return;
            }

            var newBoard = await service.CreateBoardAsync(boardTitle);

            if (newBoard != null)
            {
                var newBoardId = newBoard.Id;

                this.NavigationService.Navigate(new BoardPage(newBoardId)); 
            }
            else
            {
                MessageBox.Show("Не удалось создать доску.");
            }
        }

        //Открыть доску
        private void OpenBoard_Click(object sender, RoutedEventArgs e)
        {
            var boardId = (Guid)((Button)sender).CommandParameter;

            this.NavigationService.Navigate(new BoardPage(boardId));
        }

        //Выход из аккаунта
        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            SessionStorage.ClearSession();

            SupabaseService.Client.Auth.SignOut();

            this.NavigationService.Navigate(new LoginPage());
        }

        private async void JoinByCode_Click(object sender, RoutedEventArgs e)
        {
            string accessCode = Microsoft.VisualBasic.Interaction.InputBox(
                "Введите код доступа к доске:",
                "Подключение по коду",
                "",
                -1, -1);

            if (string.IsNullOrWhiteSpace(accessCode))
            {
                MessageBox.Show("Код доступа не может быть пустым.");
                return;
            }

            accessCode = accessCode.Trim().ToUpperInvariant();

            var service = new SupabaseService();
            var board = await service.JoinBoardAsync(accessCode); // ← теперь вызывает правильный метод из сервиса

            if (board != null)
            {
                MessageBox.Show($"✅ Вы успешно присоединились к доске \"{board.Title}\".");
                LoadBoards(); // обновить список
            }
        }

    }
}
