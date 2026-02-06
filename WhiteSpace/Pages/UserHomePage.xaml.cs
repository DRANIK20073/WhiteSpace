using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
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
            var boards = await service.GetBoardsAsync();
            Boards = boards;
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

            // Окно для ввода имени доски 
            string boardTitle = Microsoft.VisualBasic.Interaction.InputBox(
                "Введите имя для новой доски:",
                "Создание новой доски",
                "Новая доска", // Значение по умолчанию
                -1, -1); // Позиция окна

            if (string.IsNullOrWhiteSpace(boardTitle))
            {
                MessageBox.Show("Название доски не может быть пустым.");
                return;
            }

            var newBoard = await service.CreateBoardAsync(boardTitle);

            if (newBoard != null)
            {
                var newBoardId = newBoard.Id; // Извлекаем ID доски

                // Переход на страницу доски с передачей ID
                this.NavigationService.Navigate(new BoardPage(newBoardId));  // Переход на страницу доски
            }
            else
            {
                MessageBox.Show("Не удалось создать доску.");
            }
        }

        private void OpenBoard_Click(object sender, RoutedEventArgs e)
        {
            // Получаем ID доски из CommandParameter
            var boardId = (Guid)((Button)sender).CommandParameter;

            // Переход на страницу доски с передачей ID
            this.NavigationService.Navigate(new BoardPage(boardId));
        }
    }
}
