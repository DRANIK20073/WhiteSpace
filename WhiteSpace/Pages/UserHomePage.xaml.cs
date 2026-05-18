using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using System.Windows.Navigation;
using WhiteSpace;
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
        private readonly DispatcherTimer _searchDebounceTimer;
        private readonly DispatcherTimer _toastAutoHideTimer;
        private AppPreferences _preferences = new();
        private bool _suppressPreferenceEvents;

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

        private bool _boardsLoadedOnce;
        private bool _homeNavHooked;

        public UserHomePage()
        {
            InitializeComponent();
            DataContext = this;
            Unloaded += UserHomePage_Unloaded;
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            _toastAutoHideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _toastAutoHideTimer.Tick += ToastAutoHideTimer_Tick;

            HomeToastService.ToastRequested += OnHomeToastRequested;
            BoardChatNotificationHub.UnreadCountChanged += OnNotificationUnreadCountChanged;
        }

        private void UserHomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            AccountBanGuard.Stop();
            HomeToastService.ToastRequested -= OnHomeToastRequested;
            BoardChatNotificationHub.UnreadCountChanged -= OnNotificationUnreadCountChanged;
            if (BoardChatNotificationHub.Items is INotifyCollectionChanged notify)
            {
                notify.CollectionChanged -= NotificationItems_CollectionChanged;
            }

            _toastAutoHideTimer.Stop();

            if (NavigationService != null && _homeNavHooked)
            {
                NavigationService.LoadCompleted -= HomeNavigation_LoadCompleted;
                _homeNavHooked = false;
            }
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (await _service.EnforceBanLogoutIfNeededAsync())
            {
                return;
            }

            AccountBanGuard.Start();

            LoadPreferences();
            _currentSection = DashboardSection.MyBoards;
            WhiteSpaceThemeManager.Apply(_preferences);
            UiAnimationHelper.ApplyFadeIn(RootPageGrid, _preferences.EnableAnimations);
            ApplySidebarSelection();
            ApplyViewModeSelection();
            await LoadDashboardAsync();
            _boardsLoadedOnce = true;

            if (BoardChatNotificationHub.Items is INotifyCollectionChanged notify)
            {
                notify.CollectionChanged -= NotificationItems_CollectionChanged;
                notify.CollectionChanged += NotificationItems_CollectionChanged;
            }

            if (NotificationListBox != null)
            {
                NotificationListBox.ItemsSource = BoardChatNotificationHub.Items;
            }

            UpdateNotificationBadge();
            UpdateNotificationEmptyHint();

            await BoardInviteNavigation.TryNavigateFromPendingAsync(NavigationService);

            if (NavigationService != null && !_homeNavHooked)
            {
                NavigationService.LoadCompleted += HomeNavigation_LoadCompleted;
                _homeNavHooked = true;
            }
        }

        private async void HomeNavigation_LoadCompleted(object sender, NavigationEventArgs e)
        {
            if (!_boardsLoadedOnce || NavigationService?.Content != this)
            {
                return;
            }

            await LoadBoardsAsync();
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

                GreetingTextBlock.Text = $"Здравствуйте, {_userName}";
                SidebarUserNameTextBlock.Text = _userName;
                var initials = GetInitials(_userName);
                UserInitialsTextBlock.Text = initials;
            }
            catch (Exception ex)
            {
                GreetingTextBlock.Text = "Здравствуйте!";
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

                    var createdUtc = board.CreatedAt.Kind == DateTimeKind.Utc
                        ? board.CreatedAt
                        : board.CreatedAt.ToUniversalTime();
                    var lastUtc = BoardActivityStorage.TryGetLastActivityUtc(board.Id);
                    var displayUtc = lastUtc.HasValue && lastUtc.Value >= createdUtc
                        ? lastUtc.Value
                        : createdUtc;

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
                        CreatedText = FormatRelativeDate(displayUtc.ToLocalTime()),
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
                await BoardChatNotificationHub.SyncSubscriptionsAsync(_service);
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
            // Без постоянного «выбранного» фона: подсветка только из шаблона кнопки при наведении
            var transparent = Brushes.Transparent;
            MyBoardsButton.Background = transparent;
            SharedBoardsButton.Background = transparent;
            RecentBoardsButton.Background = transparent;
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
            BoardActivityStorage.Touch(boardId);
            var boardIds = LoadRecentBoardIds();
            boardIds.Remove(boardId);
            boardIds.Insert(0, boardId);
            SaveRecentBoardIds(boardIds.Take(20).ToList());
        }

        /// <summary>Обновляет недавние доски при переходе по ссылке-приглашению.</summary>
        public static void RememberBoardActivity(Guid boardId) => RememberBoard(boardId);

        private void SetSection(DashboardSection section)
        {
            _currentSection = section;
            AppPreferences.MutateAndSave(p => p.LastSection = section.ToString(), out _);
            _preferences = AppPreferences.Load();
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
            if (_suppressPreferenceEvents)
            {
                return;
            }

            var selectedSort = (SortComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(selectedSort))
            {
                AppPreferences.MutateAndSave(p => p.SortMode = selectedSort, out _);
                _preferences = AppPreferences.Load();
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
            AppPreferences.MutateAndSave(p => p.UseCompactView = false, out _);
            _preferences = AppPreferences.Load();
            RefreshVisibleBoards();
        }

        private void CompactViewButton_Click(object sender, RoutedEventArgs e)
        {
            _isCompactView = true;
            OnPropertyChanged(nameof(BoardCardWidth));
            OnPropertyChanged(nameof(BoardCardHeight));
            ApplyViewModeSelection();
            AppPreferences.MutateAndSave(p => p.UseCompactView = true, out _);
            _preferences = AppPreferences.Load();
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

            BoardChatNotificationHub.Stop();
            SessionStorage.ClearSession();
            SupabaseService.ClearLocalAdminSession();
            SupabaseService.Client.Auth.SignOut();
            NavigationService?.Navigate(new LoginPage());
        }

        private void Help_Click(object sender, RoutedEventArgs e) =>
            HelpService.Show(Window.GetWindow(this), "home");

        private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            RefreshVisibleBoards();
        }

        private void LoadPreferences()
        {
            _suppressPreferenceEvents = true;
            try
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
            finally
            {
                _suppressPreferenceEvents = false;
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

        private void OnHomeToastRequested(string message)
        {
            _ = Dispatcher.BeginInvoke(new Action(() => ShowHomeToast(message)));
        }

        private void ShowHomeToast(string message)
        {
            if (HomeToastBorder == null || HomeToastMessageText == null)
            {
                return;
            }

            HomeToastMessageText.Text = message;
            HomeToastBorder.Visibility = Visibility.Visible;
            HomeToastBorder.Opacity = 1;
            HomeToastBorder.BeginAnimation(UIElement.OpacityProperty, null);

            _toastAutoHideTimer.Stop();
            _toastAutoHideTimer.Start();

            if (_preferences.EnableAnimations)
            {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                HomeToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
        }

        private void DismissHomeToast()
        {
            _toastAutoHideTimer.Stop();

            if (HomeToastBorder == null)
            {
                return;
            }

            if (_preferences.EnableAnimations)
            {
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                fade.Completed += (_, _) =>
                {
                    HomeToastBorder.Visibility = Visibility.Collapsed;
                    HomeToastBorder.Opacity = 1;
                };
                HomeToastBorder.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            else
            {
                HomeToastBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void HomeToastDismiss_Click(object sender, RoutedEventArgs e) => DismissHomeToast();

        private void ToastAutoHideTimer_Tick(object? sender, EventArgs e)
        {
            _toastAutoHideTimer.Stop();
            DismissHomeToast();
        }

        private void OnNotificationUnreadCountChanged() =>
            Dispatcher.BeginInvoke(new Action(UpdateNotificationBadge));

        private void NotificationItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            Dispatcher.BeginInvoke(new Action(UpdateNotificationEmptyHint));

        private void UpdateNotificationBadge()
        {
            if (NotificationUnreadBadge == null || NotificationUnreadBadgeText == null)
            {
                return;
            }

            var n = BoardChatNotificationHub.UnreadCount;
            if (n <= 0)
            {
                NotificationUnreadBadge.Visibility = Visibility.Collapsed;
                return;
            }

            NotificationUnreadBadge.Visibility = Visibility.Visible;
            NotificationUnreadBadgeText.Text = n > 99 ? "99+" : n.ToString();
        }

        private void UpdateNotificationEmptyHint()
        {
            if (NotificationEmptyHint == null || NotificationListBox == null)
            {
                return;
            }

            var empty = BoardChatNotificationHub.Items.Count == 0;
            NotificationEmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            NotificationListBox.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }

        private void NotificationCenter_Click(object sender, RoutedEventArgs e)
        {
            if (NotificationCenterPopup == null)
            {
                return;
            }

            var open = !NotificationCenterPopup.IsOpen;
            NotificationCenterPopup.IsOpen = open;
            if (open)
            {
                BoardChatNotificationHub.MarkAllRead();
                UpdateNotificationBadge();
                UpdateNotificationEmptyHint();
            }
        }

        private void NotificationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NotificationListBox?.SelectedItem is not BoardChatNotificationItem item)
            {
                return;
            }

            item.IsRead = true;
            BoardChatNotificationHub.RecountUnread();
            UpdateNotificationBadge();
            NotificationCenterPopup.IsOpen = false;
            var boardId = item.BoardId;
            NotificationListBox.SelectedItem = null;
            NavigationService?.Navigate(new BoardPage(boardId));
        }

        private void NotificationClearAll_Click(object sender, RoutedEventArgs e)
        {
            BoardChatNotificationHub.ClearAll();
            UpdateNotificationBadge();
            UpdateNotificationEmptyHint();
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
