using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WhiteSpace.Services;

namespace WhiteSpace.Pages
{
    public partial class UserHomePage : Page, INotifyPropertyChanged
    {
        private enum DashboardSection
        {
            MyBoards,
            SharedBoards,
            RecentBoards
        }

        private static readonly string RecentBoardsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhiteSpace",
            "recent-boards.json");

        private readonly SupabaseService _service = new SupabaseService();
        private List<HomeBoardCard> _allBoards = new();
        private List<HomeBoardCard> _visibleBoards = new();
        private DashboardSection _currentSection = DashboardSection.MyBoards;
        private bool _isCompactView;
        private string _userName = "Пользователь";
        private string _userEmail = "email@example.com";
        private readonly DispatcherTimer _searchDebounceTimer;
        private AppPreferences _preferences = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public List<HomeBoardCard> VisibleBoards
        {
            get => _visibleBoards;
            set
            {
                _visibleBoards = value;
                OnPropertyChanged();
                UpdateBoardsVisibility();
            }
        }

        public double BoardCardWidth => _isCompactView ? 250 : 360;

        public double BoardCardHeight => _isCompactView ? 214 : 248;

        public UserHomePage()
        {
            InitializeComponent();
            DataContext = this;
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadPreferences();
            UiAnimationHelper.ApplyFadeIn(RootPageGrid, _preferences.EnableAnimations);
            ApplySidebarSelection();
            ApplyViewModeSelection();
            await LoadDashboardAsync();
        }

        private async Task LoadDashboardAsync()
        {
            await LoadUserProfileAsync();
            await LoadBoardsAsync();
        }

        private async Task LoadUserProfileAsync()
        {
            try
            {
                var profile = await _service.GetMyProfileAsync();

                _userName = !string.IsNullOrWhiteSpace(profile?.Username)
                    ? profile.Username
                    : "Пользователь";

                _userEmail = !string.IsNullOrWhiteSpace(profile?.Email)
                    ? profile.Email
                    : "email@example.com";

                GreetingTextBlock.Text = $"Здравствуйте, {_userName} 👋";
                HeaderEmailTextBlock.Text = _userEmail;
                SidebarUserNameTextBlock.Text = _userName;
                SidebarUserEmailTextBlock.Text = _userEmail;
                var initials = GetInitials(_userName);
                UserInitialsTextBlock.Text = initials;
            }
            catch (Exception ex)
            {
                GreetingTextBlock.Text = "Здравствуйте!";
                HeaderEmailTextBlock.Text = "Профиль временно недоступен";
                AppDialogService.ShowError($"Ошибка загрузки профиля: {ex.Message}", "Профиль");
            }
        }

        private async Task LoadBoardsAsync()
        {
            try
            {
                var boardsWithRoles = await _service.GetAllAccessibleBoardsWithRoleAsync();
                var palette = GetBoardPalette();
                var cards = new List<HomeBoardCard>();

                for (int index = 0; index < boardsWithRoles.Count; index++)
                {
                    var (board, role) = boardsWithRoles[index];
                    var paletteItem = palette[index % palette.Length];

                    cards.Add(new HomeBoardCard
                    {
                        Id = board.Id,
                        Title = string.IsNullOrWhiteSpace(board.Title) ? "Новая доска" : board.Title,
                        Role = role,
                        RoleLabel = role switch
                        {
                            "owner" => "Владелец",
                            "editor" => "Редактор",
                            _ => "Наблюдатель"
                        },
                        Subtitle = role switch
                        {
                            "owner" => $"Личная доска • Код {board.AccessCode}",
                            "editor" => $"Общая доска • Код {board.AccessCode}",
                            _ => $"Только просмотр • Код {board.AccessCode}"
                        },
                        CreatedAt = board.CreatedAt,
                        CreatedText = FormatRelativeDate(board.CreatedAt),
                        AccentStart = paletteItem.Start,
                        AccentEnd = paletteItem.End,
                        RoleBadgeBackground = role switch
                        {
                            "owner" => new SolidColorBrush(Color.FromRgb(139, 92, 246)),
                            "editor" => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                            _ => new SolidColorBrush(Color.FromRgb(241, 245, 249))
                        },
                        RoleBadgeForeground = role == "viewer"
                            ? new SolidColorBrush(Color.FromRgb(100, 116, 139))
                            : Brushes.White,
                        DeleteVisibility = role == "owner" ? Visibility.Visible : Visibility.Collapsed
                    });
                }

                _allBoards = cards;
                RefreshVisibleBoards();
            ApplyAnimationPreference();
            }
            catch (Exception ex)
            {
                AppDialogService.ShowError($"Ошибка загрузки досок: {ex.Message}", "Доски");
            }
        }

        private void RefreshVisibleBoards()
        {
            IEnumerable<HomeBoardCard> query = _currentSection switch
            {
                DashboardSection.MyBoards => _allBoards.Where(board => board.Role == "owner"),
                DashboardSection.SharedBoards => _allBoards.Where(board => board.Role != "owner"),
                DashboardSection.RecentBoards => GetRecentBoards(),
                _ => _allBoards
            };

            var search = SearchTextBox?.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(board => board.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            query = ApplySorting(query);

            VisibleBoards = query.ToList();
            UpdateSectionPresentation();
        }

        private IEnumerable<HomeBoardCard> ApplySorting(IEnumerable<HomeBoardCard> boards)
        {
            var selectedSort = (SortComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Новые сначала";

            if (_currentSection == DashboardSection.RecentBoards && selectedSort == "Новые сначала")
            {
                var positions = LoadRecentBoardIds()
                    .Select((id, index) => new { id, index })
                    .ToDictionary(item => item.id, item => item.index);

                return boards.OrderBy(board => positions.TryGetValue(board.Id, out var position) ? position : int.MaxValue);
            }

            return selectedSort switch
            {
                "Старые сначала" => boards.OrderBy(board => board.CreatedAt),
                "Название А-Я" => boards.OrderBy(board => board.Title),
                "Название Я-А" => boards.OrderByDescending(board => board.Title),
                _ => boards.OrderByDescending(board => board.CreatedAt)
            };
        }

        private IEnumerable<HomeBoardCard> GetRecentBoards()
        {
            var orderedIds = LoadRecentBoardIds();
            if (orderedIds.Count == 0)
            {
                foreach (var board in _allBoards.OrderByDescending(board => board.CreatedAt).Take(8))
                {
                    yield return board;
                }

                yield break;
            }

            var boardsById = _allBoards.ToDictionary(board => board.Id, board => board);

            foreach (var boardId in orderedIds)
            {
                if (boardsById.TryGetValue(boardId, out var board))
                {
                    yield return board;
                }
            }
        }

        private void UpdateSectionPresentation()
        {
            var myBoardsCount = _allBoards.Count(board => board.Role == "owner");
            var sharedBoardsCount = _allBoards.Count(board => board.Role != "owner");
            var recentCount = GetRecentBoards().Count();

            if (EmptyStateActionButton != null)
            {
                EmptyStateActionButton.Visibility = Visibility.Visible;
            }

            switch (_currentSection)
            {
                case DashboardSection.MyBoards:
                    SectionTitleTextBlock.Text = "Мои доски";
                    SectionSubtitleTextBlock.Text = $"{myBoardsCount} досок";
                    EmptyStateTitleTextBlock.Text = "У вас пока нет личных досок";
                    EmptyStateSubtitleTextBlock.Text = "Создайте новую доску, и она появится здесь.";
                    break;
                case DashboardSection.SharedBoards:
                    SectionTitleTextBlock.Text = "Общие доски";
                    SectionSubtitleTextBlock.Text = $"{sharedBoardsCount} досок";
                    EmptyStateTitleTextBlock.Text = "Пока нет общих досок";
                    EmptyStateSubtitleTextBlock.Text = "Подключитесь по коду, чтобы доски других пользователей появились здесь.";
                    if (EmptyStateActionButton != null)
                    {
                        EmptyStateActionButton.Visibility = Visibility.Collapsed;
                    }
                    break;
                default:
                    SectionTitleTextBlock.Text = "Недавние";
                    SectionSubtitleTextBlock.Text = $"{recentCount} досок";
                    EmptyStateTitleTextBlock.Text = "История ещё не заполнена";
                    EmptyStateSubtitleTextBlock.Text = "Открытые вами доски будут появляться в этом разделе.";
                    break;
            }

            ApplySidebarSelection();
        }

        private void ApplySidebarSelection()
        {
            var activeBackground = ResolveBrush("WsSurfaceMutedBrush", new SolidColorBrush(Color.FromRgb(51, 65, 85)));
            var transparent = Brushes.Transparent;

            MyBoardsButton.Background = _currentSection == DashboardSection.MyBoards ? activeBackground : transparent;
            SharedBoardsButton.Background = _currentSection == DashboardSection.SharedBoards ? activeBackground : transparent;
            RecentBoardsButton.Background = _currentSection == DashboardSection.RecentBoards ? activeBackground : transparent;
        }

        private void ApplyViewModeSelection()
        {
            var activeBackground = ResolveBrush("WsPurpleBrush", new SolidColorBrush(Color.FromRgb(139, 92, 246)));
            var activeBorder = ResolveBrush("WsPurpleBrush", new SolidColorBrush(Color.FromRgb(139, 92, 246)));
            var inactiveBackground = ResolveBrush("WsSurfaceBrush", new SolidColorBrush(Color.FromRgb(255, 255, 255)));
            var inactiveForeground = ResolveBrush("WsTextPrimaryBrush", new SolidColorBrush(Color.FromRgb(30, 41, 59)));
            var inactiveBorder = ResolveBrush("WsBorderBrush", new SolidColorBrush(Color.FromRgb(226, 232, 240)));

            LargeViewButton.Background = _isCompactView ? inactiveBackground : activeBackground;
            LargeViewButton.Foreground = _isCompactView ? inactiveForeground : Brushes.White;
            LargeViewButton.BorderBrush = _isCompactView ? inactiveBorder : activeBorder;

            CompactViewButton.Background = _isCompactView ? activeBackground : inactiveBackground;
            CompactViewButton.Foreground = _isCompactView ? Brushes.White : inactiveForeground;
            CompactViewButton.BorderBrush = _isCompactView ? activeBorder : inactiveBorder;
        }

        private static Brush ResolveBrush(string resourceKey, Brush fallback)
        {
            return Application.Current?.TryFindResource(resourceKey) as Brush ?? fallback;
        }

        private void UpdateBoardsVisibility()
        {
            if (NoBoardsState == null || BoardsItemsControl == null)
            {
                return;
            }

            bool hasBoards = VisibleBoards != null && VisibleBoards.Count > 0;
            NoBoardsState.Visibility = hasBoards ? Visibility.Collapsed : Visibility.Visible;
            BoardsItemsControl.Visibility = hasBoards ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string GetInitials(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                return "W";
            }

            var parts = userName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0]));

            var initials = string.Concat(parts);
            return string.IsNullOrWhiteSpace(initials) ? "W" : initials;
        }

        private static (Color Start, Color End)[] GetBoardPalette()
        {
            return new[]
            {
                (Color.FromRgb(139, 92, 246), Color.FromRgb(59, 130, 246)),
                (Color.FromRgb(124, 58, 237), Color.FromRgb(96, 165, 250)),
                (Color.FromRgb(59, 130, 246), Color.FromRgb(139, 92, 246)),
                (Color.FromRgb(30, 41, 59), Color.FromRgb(139, 92, 246))
            };
        }

        private static string FormatRelativeDate(DateTime date)
        {
            var value = date.ToLocalTime().Date;
            var today = DateTime.Now.Date;
            var days = (today - value).Days;

            return days switch
            {
                <= 0 => "сегодня",
                1 => "1 день назад",
                < 5 => $"{days} дня назад",
                _ => $"{days} дней назад"
            };
        }

        private static List<Guid> LoadRecentBoardIds()
        {
            try
            {
                if (!File.Exists(RecentBoardsPath))
                {
                    return new List<Guid>();
                }

                var json = File.ReadAllText(RecentBoardsPath);
                return JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>();
            }
            catch
            {
                return new List<Guid>();
            }
        }

        private static void SaveRecentBoardIds(List<Guid> boardIds)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecentBoardsPath)!);
            File.WriteAllText(RecentBoardsPath, JsonSerializer.Serialize(boardIds));
        }

        private static void RememberBoard(Guid boardId)
        {
            var boardIds = LoadRecentBoardIds();
            boardIds.Remove(boardId);
            boardIds.Insert(0, boardId);
            SaveRecentBoardIds(boardIds.Take(20).ToList());
        }

        private void SetSection(DashboardSection section)
        {
            _currentSection = section;
            _preferences.LastSection = section.ToString();
            _preferences.Save();
            RefreshVisibleBoards();
        }

        private void MyBoardsButton_Click(object sender, RoutedEventArgs e) => SetSection(DashboardSection.MyBoards);

        private void SharedBoardsButton_Click(object sender, RoutedEventArgs e) => SetSection(DashboardSection.SharedBoards);

        private void RecentBoardsButton_Click(object sender, RoutedEventArgs e) => SetSection(DashboardSection.RecentBoards);

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedSort = (SortComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(selectedSort))
            {
                _preferences.SortMode = selectedSort;
                _preferences.Save();
            }

            if (IsLoaded)
            {
                RefreshVisibleBoards();
            }
        }

        private void LargeViewButton_Click(object sender, RoutedEventArgs e)
        {
            _isCompactView = false;
            OnPropertyChanged(nameof(BoardCardWidth));
            OnPropertyChanged(nameof(BoardCardHeight));
            ApplyViewModeSelection();
            _preferences.UseCompactView = false;
            _preferences.Save();
            RefreshVisibleBoards();
        }

        private void CompactViewButton_Click(object sender, RoutedEventArgs e)
        {
            _isCompactView = true;
            OnPropertyChanged(nameof(BoardCardWidth));
            OnPropertyChanged(nameof(BoardCardHeight));
            ApplyViewModeSelection();
            _preferences.UseCompactView = true;
            _preferences.Save();
            RefreshVisibleBoards();
        }

        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_preferences.EnableAnimations && RootPageGrid != null)
            {
                RootPageGrid.BeginAnimation(UIElement.OpacityProperty, null);
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                fade.Completed += (_, _) => OpenSettings();
                RootPageGrid.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            else
            {
                OpenSettings();
            }
        }

        private void OpenSettings()
        {
            NavigationService?.Navigate(new UserSettingsPage());
        }

        private async void CreateBoard_Click(object sender, RoutedEventArgs e)
        {
            var boardTitle = AppDialogService.ShowTextInput(
                "Создание новой доски",
                "Введите имя для новой доски:",
                "Создать",
                "Отмена",
                "Новая доска");

            if (boardTitle == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(boardTitle))
            {
                AppDialogService.ShowWarning("Название доски не может быть пустым.", "Создание доски");
                return;
            }

            var newBoard = await _service.CreateBoardAsync(boardTitle.Trim());
            if (newBoard == null)
            {
                AppDialogService.ShowError("Не удалось создать доску.", "Создание доски");
                return;
            }

            RememberBoard(newBoard.Id);
            NavigationService?.Navigate(new BoardPage(newBoard.Id));
        }

        private async void JoinByCode_Click(object sender, RoutedEventArgs e)
        {
            var accessCode = AppDialogService.ShowTextInput(
                "Подключение по коду",
                "Введите код доступа к доске:",
                "Подключиться",
                "Отмена",
                "");

            if (accessCode == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(accessCode))
            {
                AppDialogService.ShowWarning("Код доступа не может быть пустым.", "Подключение по коду");
                return;
            }

            var board = await _service.JoinBoardAsync(accessCode.Trim().ToUpperInvariant());
            if (board == null)
            {
                AppDialogService.ShowError("Не удалось присоединиться к доске. Проверьте код доступа.", "Подключение по коду");
                return;
            }

            await LoadBoardsAsync();
            RememberBoard(board.Id);

            if (AppDialogService.ShowConfirmation($"Вы подключились к доске \"{board.Title}\". Открыть её сейчас?", "Подключение по коду"))
            {
                NavigationService?.Navigate(new BoardPage(board.Id));
            }
        }

        private void OpenBoard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.CommandParameter is not Guid boardId)
            {
                return;
            }

            RememberBoard(boardId);
            NavigationService?.Navigate(new BoardPage(boardId));
        }

        private void OpenBoardCard_Click(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is Button)
                {
                    return;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            if (sender is not Border border || border.Tag is not Guid boardId)
            {
                return;
            }

            RememberBoard(boardId);
            NavigationService?.Navigate(new BoardPage(boardId));
        }

        private async void DeleteBoard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.CommandParameter is not Guid boardId)
            {
                return;
            }

            var success = await _service.DeleteBoardAsync(boardId);
            if (success)
            {
                await LoadBoardsAsync();
            }
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            if (_preferences.ConfirmBeforeLogout &&
                !AppDialogService.ShowConfirmation("Выйти из аккаунта?", "Подтверждение выхода", "Выйти", "Отмена"))
            {
                return;
            }

            SessionStorage.ClearSession();
            SupabaseService.ClearLocalAdminSession();
            SupabaseService.Client.Auth.SignOut();
            NavigationService?.Navigate(new LoginPage());
        }

        private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            RefreshVisibleBoards();
        }

        private void LoadPreferences()
        {
            _preferences = AppPreferences.Load();
            _isCompactView = _preferences.UseCompactView;

            if (Enum.TryParse<DashboardSection>(_preferences.LastSection, out var section))
            {
                _currentSection = section;
            }

            if (SortComboBox != null)
            {
                foreach (var item in SortComboBox.Items.OfType<ComboBoxItem>())
                {
                    if (string.Equals(item.Content?.ToString(), _preferences.SortMode, StringComparison.Ordinal))
                    {
                        SortComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void ApplyAnimationPreference()
        {
            if (_preferences.EnableAnimations)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var border in FindVisualChildren<Border>(BoardsItemsControl))
                {
                    border.Opacity = 1;
                    if (border.RenderTransform is TranslateTransform transform)
                    {
                        transform.Y = 0;
                    }
                    else if (border.RenderTransform is TransformGroup transformGroup &&
                             transformGroup.Children.Count > 1 &&
                             transformGroup.Children[1] is TranslateTransform translateTransform)
                    {
                        translateTransform.Y = 0;
                    }
                }
            }), DispatcherPriority.Loaded);
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null)
            {
                yield break;
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed)
                {
                    yield return typed;
                }

                foreach (var nested in FindVisualChildren<T>(child))
                {
                    yield return nested;
                }
            }
        }

        public static void ClearRecentBoards()
        {
            try
            {
                if (File.Exists(RecentBoardsPath))
                {
                    File.Delete(RecentBoardsPath);
                }
            }
            catch
            {
                // Nothing critical: history cleanup can fail silently.
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class HomeBoardCard
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public string RoleLabel { get; set; } = string.Empty;

        public string Subtitle { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public string CreatedText { get; set; } = string.Empty;

        public Color AccentStart { get; set; }

        public Color AccentEnd { get; set; }

        public Brush RoleBadgeBackground { get; set; } = Brushes.White;

        public Brush RoleBadgeForeground { get; set; } = Brushes.Black;

        public Visibility DeleteVisibility { get; set; } = Visibility.Collapsed;
    }
}
