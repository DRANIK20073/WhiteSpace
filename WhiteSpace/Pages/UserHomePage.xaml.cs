using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Linq;
using System.Windows.Controls;
using System.Collections.Generic;
using System;

namespace WhiteSpace.Pages
{
    public partial class UserHomePage : Page, INotifyPropertyChanged
    {
        private string _userGreeting = "Добро пожаловать!";
        private List<Board> _boards = new List<Board>();
        private string _userName = "";

        public List<Board> Boards
        {
            get => _boards;
            set
            {
                _boards = value;
                OnPropertyChanged();
                UpdateBoardsVisibility();
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
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadUserProfile(); // Сначала загружаем профиль пользователя
            await LoadBoards();       // Затем загружаем доски
        }

        // Загрузка профиля пользователя
        private async System.Threading.Tasks.Task LoadUserProfile()
        {
            try
            {
                var service = new SupabaseService();
                var profile = await service.GetMyProfileAsync();

                if (profile != null && !string.IsNullOrEmpty(profile.Username))
                {
                    _userName = profile.Username;
                    UserGreeting = $"Здравствуйте, {profile.Username} 👋";
                }
                else
                {
                    UserGreeting = "Здравствуйте!";
                }
            }
            catch (Exception ex)
            {
                // В случае ошибки показываем стандартное приветствие
                UserGreeting = "Здравствуйте!";
                MessageBox.Show($"Ошибка загрузки профиля: {ex.Message}");
            }
        }

        //Загрузка списка досок
        private async System.Threading.Tasks.Task LoadBoards()
        {
            try
            {
                var service = new SupabaseService();
                var boardsWithRoles = await service.GetAllAccessibleBoardsWithRoleAsync();
                Boards = boardsWithRoles.Select(x => x.Board).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки досок: {ex.Message}");
            }
        }

        private void UpdateBoardsVisibility()
        {
            if (NoBoardsTextBlock != null)
            {
                NoBoardsTextBlock.Visibility = Boards == null || Boards.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
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
            try
            {
                Guid boardId = Guid.Empty;

                // Проверяем тип отправителя
                if (sender is Button button && button.CommandParameter is Guid buttonBoardId)
                {
                    boardId = buttonBoardId;
                }
                else if (sender is MenuItem menuItem && menuItem.CommandParameter is Guid menuBoardId)
                {
                    boardId = menuBoardId;
                }

                if (boardId != Guid.Empty)
                {
                    this.NavigationService.Navigate(new BoardPage(boardId));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии доски: {ex.Message}");
            }
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
            var board = await service.JoinBoardAsync(accessCode);

            if (board != null)
            {
                MessageBox.Show($"✅ Вы успешно присоединились к доске \"{board.Title}\".");

                // Обновляем список досок на главной странице
                await LoadBoards();

                // Спрашиваем пользователя, хочет ли он перейти на доску сейчас
                var result = MessageBox.Show(
                    "Хотите перейти на доску сейчас?",
                    "Переход на доску",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Переходим на доску
                    this.NavigationService.Navigate(new BoardPage(board.Id));
                }
            }
            else
            {
                MessageBox.Show("Не удалось присоединиться к доске. Проверьте код доступа.");
            }
        }

        private async void DeleteBoard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Guid boardId = Guid.Empty;

                // Проверяем тип отправителя
                if (sender is MenuItem menuItem && menuItem.CommandParameter is Guid menuBoardId)
                {
                    boardId = menuBoardId;
                }

                if (boardId != Guid.Empty)
                {
                    var service = new SupabaseService();
                    var success = await service.DeleteBoardAsync(boardId);

                    if (success)
                    {
                        // Обновляем список досок после удаления
                        await LoadBoards();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при удалении доски: {ex.Message}");
            }
        }
    }
}