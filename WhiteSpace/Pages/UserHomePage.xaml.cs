using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Navigation;
using WhiteSpace;
using WhiteSpace.Helpers;
using WhiteSpace.Rendering;
using WhiteSpace.Services;

namespace WhiteSpace.Pages
{
    public partial class UserHomePage : Page, INotifyPropertyChanged
    {
        private enum DashboardSection
        {
            MyBoards,
            SharedBoards,
            RecentBoards,
            FavoriteBoards
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
        private AdaptiveWidthTier _layoutTier = AdaptiveWidthTier.Wide;
        private bool _sidebarOverlayOpen;
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

        public double BoardCardWidth => _isCompactView
            ? (_layoutTier == AdaptiveWidthTier.Compact ? 220 : 250)
            : (_layoutTier <= AdaptiveWidthTier.Narrow ? 300 : 360);

        public double BoardCardHeight => _isCompactView
            ? (_layoutTier == AdaptiveWidthTier.Compact ? 210 : 228)
            : (_layoutTier <= AdaptiveWidthTier.Narrow ? 248 : 262);

        private bool _boardsLoadedOnce;
        private bool _initialLoadComplete;
        private bool _initialLoadInProgress;
        private bool _refreshInProgress;
        private bool _refreshScheduled;
        private bool _playCardEntrance;
        private readonly DispatcherTimer _adaptiveLayoutTimer;

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

            _adaptiveLayoutTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(80)
            };
            _adaptiveLayoutTimer.Tick += AdaptiveLayoutTimer_Tick;
        }

        private void AdaptiveLayoutTimer_Tick(object? sender, EventArgs e)
        {
            _adaptiveLayoutTimer.Stop();
            if (RootPageGrid != null)
            {
                ApplyAdaptiveLayout(RootPageGrid.ActualWidth);
            }
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
            _adaptiveLayoutTimer.Stop();
        }

        public void RequestRefreshAfterNavigation()
        {
            if (!_initialLoadComplete || _refreshScheduled)
            {
                return;
            }

            _refreshScheduled = true;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                _refreshScheduled = false;
                await RefreshAfterNavigationAsync();
            }), DispatcherPriority.Loaded);
        }

        public async Task RefreshAfterNavigationAsync()
        {
            if (!_initialLoadComplete || _initialLoadInProgress || _refreshInProgress)
            {
                return;
            }

            _refreshInProgress = true;
            try
            {
                LoadPreferences();
                UiAnimationHelper.ApplyReturnFadeIn(RootPageGrid, _preferences.EnableAnimations);

                ApplySidebarSelection();
                ApplyViewModeSelection();
                await LoadBoardsAsync(animateCards: false);
                UpdateNotificationBadge();
                UpdateNotificationEmptyHint();
                ApplyAdaptiveLayout(RootPageGrid.ActualWidth);
            }
            finally
            {
                _refreshInProgress = false;
            }
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initialLoadComplete)
            {
                ApplyAdaptiveLayout(RootPageGrid.ActualWidth);
                return;
            }

            if (_initialLoadInProgress)
            {
                return;
            }

            _initialLoadInProgress = true;
            try
            {
                if (await _service.EnforceBanLogoutIfNeededAsync())
                {
                    return;
                }

                AccountBanGuard.Start();

                LoadPreferences();
                WhiteSpaceThemeManager.Apply(_preferences);
                _playCardEntrance = _preferences.EnableAnimations;
                UiAnimationHelper.ApplyFadeIn(RootPageGrid, _preferences.EnableAnimations, force: true);
                ApplySidebarSelection();
                ApplyViewModeSelection();
                await LoadDashboardAsync();
                _boardsLoadedOnce = true;
                _initialLoadComplete = true;

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

                ApplyAdaptiveLayout(RootPageGrid.ActualWidth);
            }
            finally
            {
                _initialLoadInProgress = false;
            }
        }

        private async Task LoadDashboardAsync()
        {
            await LoadUserProfileAsync();
            await LoadBoardsAsync(animateCards: _playCardEntrance);
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

        private async Task LoadBoardsAsync(bool animateCards = false)
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
                        DeleteVisibility = role == "owner" ? Visibility.Visible : Visibility.Collapsed,
                        IsFavorite = FavoriteBoardsStorage.IsFavorite(board.Id)
                    });
                }

                _allBoards = cards;
                RefreshVisibleBoards();
                FinalizeBoardCardsPresentation(animateCards);
                _ = PopulateBoardThumbnailsAsync(cards);
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
                DashboardSection.FavoriteBoards => GetFavoriteBoards(),
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

            if (_currentSection == DashboardSection.FavoriteBoards && selectedSort == "Новые сначала")
            {
                var positions = FavoriteBoardsStorage.Load()
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

        private async Task PopulateBoardThumbnailsAsync(IReadOnlyList<HomeBoardCard> cards)
        {
            using var gate = new SemaphoreSlim(4);
            var tasks = cards.Select(async card =>
            {
                await gate.WaitAsync();
                try
                {
                    var shapes = await _service.LoadBoardShapesForPreviewAsync(card.Id);
                    var thumbnail = await BoardThumbnailRenderer.EnsureThumbnailAsync(card.Id, shapes);
                    if (thumbnail == null)
                    {
                        return;
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        var target = _allBoards.FirstOrDefault(board => board.Id == card.Id) ?? card;
                        target.Thumbnail = thumbnail;
                    });
                }
                catch
                {
                    // thumbnails are best-effort on the home page
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        private IEnumerable<HomeBoardCard> GetFavoriteBoards()
        {
            var boardsById = _allBoards.ToDictionary(board => board.Id, board => board);

            foreach (var boardId in FavoriteBoardsStorage.Load())
            {
                if (boardsById.TryGetValue(boardId, out var board))
                {
                    yield return board;
                }
            }
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
            var favoriteCount = GetFavoriteBoards().Count();

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
                case DashboardSection.FavoriteBoards:
                    SectionTitleTextBlock.Text = "Избранные";
                    SectionSubtitleTextBlock.Text = $"{favoriteCount} досок";
                    EmptyStateTitleTextBlock.Text = "В избранном пока пусто";
                    EmptyStateSubtitleTextBlock.Text = "Наведите на доску и нажмите ★, чтобы добавить её сюда.";
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
            FavoriteBoardsButton.Background = transparent;
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

        private void FavoriteBoardsButton_Click(object sender, RoutedEventArgs e) => SetSection(DashboardSection.FavoriteBoards);

        private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button { DataContext: HomeBoardCard card })
            {
                return;
            }

            card.IsFavorite = FavoriteBoardsStorage.Toggle(card.Id);

            if (_currentSection == DashboardSection.FavoriteBoards)
            {
                RefreshVisibleBoards();
            }
        }

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
                UiAnimationHelper.ApplyFadeOut(RootPageGrid, true, OpenSettings);
                return;
            }

            OpenSettings();
        }

        private void OpenSettings()
        {
            AppNavigation.NavigateTo(NavigationService, new UserSettingsPage(), clearBackStack: false);
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
            AppNavigation.NavigateToBoard(NavigationService, newBoard.Id);
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

            var parsedCode = BoardInviteLinkParser.TryGetAccessCode(accessCode);
            if (string.IsNullOrEmpty(parsedCode))
            {
                AppDialogService.ShowWarning(
                    "Не удалось распознать код или ссылку-приглашение.",
                    "Подключение по коду");
                return;
            }

            var board = await _service.JoinBoardAsync(parsedCode);
            if (board == null)
            {
                AppDialogService.ShowError("Не удалось присоединиться к доске. Проверьте код доступа.", "Подключение по коду");
                return;
            }

            await LoadBoardsAsync();
            RememberBoard(board.Id);

            if (AppDialogService.ShowConfirmation($"Вы подключились к доске \"{board.Title}\". Открыть её сейчас?", "Подключение по коду"))
            {
                AppNavigation.NavigateToBoard(NavigationService, board.Id);
            }
        }

        private void OpenBoard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.CommandParameter is not Guid boardId)
            {
                return;
            }

            RememberBoard(boardId);
            AppNavigation.NavigateToBoard(NavigationService, boardId);
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

            OpenBoard(boardId);
        }

        private void OpenBoardFromContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { DataContext: HomeBoardCard card })
            {
                return;
            }

            OpenBoard(card.Id);
        }

        private async void DeleteBoardFromContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { DataContext: HomeBoardCard card })
            {
                return;
            }

            FavoriteBoardsStorage.Remove(card.Id);

            var success = await _service.DeleteBoardAsync(card.Id);
            if (success)
            {
                await LoadBoardsAsync();
            }
        }

        private void OpenBoard(Guid boardId)
        {
            RememberBoard(boardId);
            AppNavigation.NavigateToBoard(NavigationService, boardId);
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
            AppNavigation.NavigateToLogin(NavigationService);
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

        private void FinalizeBoardCardsPresentation(bool animateCards)
        {
            if (animateCards && _preferences.EnableAnimations)
            {
                _playCardEntrance = false;
                return;
            }

            _playCardEntrance = false;
            SnapBoardCardsVisible();
        }

        private void SnapBoardCardsVisible()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var border in FindBoardCardBorders())
                {
                    border.BeginAnimation(UIElement.OpacityProperty, null);
                    border.Opacity = 1;
                    if (border.RenderTransform is TranslateTransform transform)
                    {
                        transform.Y = 0;
                    }
                }
            }), DispatcherPriority.Loaded);
        }

        private IEnumerable<Border> FindBoardCardBorders()
        {
            foreach (var border in FindVisualChildren<Border>(BoardsItemsControl))
            {
                if (border.Tag is Guid)
                {
                    yield return border;
                }
            }
        }

        private void ApplyAnimationPreference()
        {
            if (_preferences.EnableAnimations)
            {
                return;
            }

            SnapBoardCardsVisible();
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
            AppNavigation.NavigateToBoard(NavigationService, boardId);
        }

        private void NotificationClearAll_Click(object sender, RoutedEventArgs e)
        {
            BoardChatNotificationHub.ClearAll();
            UpdateNotificationBadge();
            UpdateNotificationEmptyHint();
        }

        private void RootPageGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _adaptiveLayoutTimer.Stop();
            _adaptiveLayoutTimer.Start();
        }

        private void SidebarToggleButton_Click(object sender, RoutedEventArgs e) =>
            SetSidebarOverlayOpen(!_sidebarOverlayOpen);

        private void SidebarBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            SetSidebarOverlayOpen(false);

        private void ApplyAdaptiveLayout(double width)
        {
            if (width <= 0 || RootPageGrid == null)
            {
                return;
            }

            var tier = AdaptiveLayout.GetTier(width);
            var tierChanged = tier != _layoutTier;
            _layoutTier = tier;

            switch (tier)
            {
                case AdaptiveWidthTier.Wide:
                    _sidebarOverlayOpen = false;
                    ConfigureSidebarDockedLayout(AdaptiveLayout.SidebarFullWidth);
                    SetSidebarNavLabelsVisible(true);
                    SidebarLogoText.Visibility = Visibility.Visible;
                    SidebarProfileTextPanel.Visibility = Visibility.Visible;
                    SidebarToggleButton.Visibility = Visibility.Collapsed;
                    SetSidebarButtonsAlignment(false);
                    break;

                case AdaptiveWidthTier.Medium:
                    _sidebarOverlayOpen = false;
                    ConfigureSidebarDockedLayout(AdaptiveLayout.SidebarIconWidth);
                    SetSidebarNavLabelsVisible(false);
                    SidebarLogoText.Visibility = Visibility.Collapsed;
                    SidebarProfileTextPanel.Visibility = Visibility.Collapsed;
                    SidebarToggleButton.Visibility = Visibility.Collapsed;
                    SetSidebarButtonsAlignment(true);
                    break;

                default:
                    ConfigureSidebarOverlayLayout();
                    SidebarToggleButton.Visibility = Visibility.Visible;
                    SetSidebarNavLabelsVisible(true);
                    SidebarLogoText.Visibility = Visibility.Visible;
                    SidebarProfileTextPanel.Visibility = Visibility.Visible;
                    SetSidebarButtonsAlignment(false);
                    if (!_sidebarOverlayOpen)
                    {
                        SidebarSlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
                        SidebarSlideTransform.X = -AdaptiveLayout.SidebarFullWidth;
                        SidebarBackdrop.Visibility = Visibility.Collapsed;
                        SidebarBackdrop.Opacity = 1;
                    }

                    break;
            }

            ApplyFilterBarLayout(tier == AdaptiveWidthTier.Compact);
            ApplyHeaderLabelsVisibility(tier);

            if (!tierChanged)
            {
                return;
            }

            OnPropertyChanged(nameof(BoardCardWidth));
            OnPropertyChanged(nameof(BoardCardHeight));
            if (_boardsLoadedOnce)
            {
                RefreshVisibleBoards();
            }
        }

        private void ConfigureSidebarDockedLayout(double columnWidth)
        {
            SidebarSlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
            SidebarSlideTransform.X = 0;
            Grid.SetColumn(SidebarBorder, 0);
            Grid.SetColumnSpan(SidebarBorder, 1);
            SidebarBorder.ClearValue(FrameworkElement.WidthProperty);
            SidebarBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
            SidebarBorder.VerticalAlignment = VerticalAlignment.Stretch;
            SidebarColumn.Width = new GridLength(columnWidth);
            SidebarBackdrop.Visibility = Visibility.Collapsed;
            SidebarBackdrop.BeginAnimation(UIElement.OpacityProperty, null);
            SidebarBackdrop.Opacity = 1;
        }

        private void ConfigureSidebarOverlayLayout()
        {
            SidebarColumn.Width = new GridLength(0);
            Grid.SetColumn(SidebarBorder, 0);
            Grid.SetColumnSpan(SidebarBorder, 2);
            SidebarBorder.Width = AdaptiveLayout.SidebarFullWidth;
            SidebarBorder.HorizontalAlignment = HorizontalAlignment.Left;
            SidebarBorder.VerticalAlignment = VerticalAlignment.Stretch;
        }

        private void SetSidebarOverlayOpen(bool open)
        {
            if (_layoutTier < AdaptiveWidthTier.Narrow)
            {
                _sidebarOverlayOpen = false;
                return;
            }

            if (_sidebarOverlayOpen == open)
            {
                return;
            }

            _sidebarOverlayOpen = open;
            ConfigureSidebarOverlayLayout();

            var width = AdaptiveLayout.SidebarFullWidth;
            var anim = _preferences.EnableAnimations;

            if (open)
            {
                SidebarBackdrop.Visibility = Visibility.Visible;
                UiAnimationHelper.ApplyFadeVisibilityToggle(SidebarBackdrop, true, anim);
                UiAnimationHelper.AnimateHorizontalSlide(
                    SidebarSlideTransform,
                    slideIn: true,
                    width,
                    fromLeft: true,
                    anim);
                return;
            }

            UiAnimationHelper.ApplyFadeVisibilityToggle(SidebarBackdrop, false, anim);
            UiAnimationHelper.AnimateHorizontalSlide(
                SidebarSlideTransform,
                slideIn: false,
                width,
                fromLeft: true,
                anim);
        }

        private void SetSidebarNavLabelsVisible(bool visible)
        {
            var v = visible ? Visibility.Visible : Visibility.Collapsed;
            MyBoardsNavLabel.Visibility = v;
            SharedBoardsNavLabel.Visibility = v;
            RecentBoardsNavLabel.Visibility = v;
            FavoriteBoardsNavLabel.Visibility = v;
        }

        private void SetSidebarButtonsAlignment(bool iconOnly)
        {
            var alignment = iconOnly ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            MyBoardsButton.HorizontalContentAlignment = alignment;
            SharedBoardsButton.HorizontalContentAlignment = alignment;
            RecentBoardsButton.HorizontalContentAlignment = alignment;
            FavoriteBoardsButton.HorizontalContentAlignment = alignment;
            ProfileButton.HorizontalContentAlignment = alignment;
        }

        private void ApplyHeaderLabelsVisibility(AdaptiveWidthTier tier)
        {
            var showLabel = tier < AdaptiveWidthTier.Medium;

            AdaptiveLayout.SetIconLabelPair(showLabel, NotificationsHeaderLabel, NotificationsHeaderIcon);
            AdaptiveLayout.SetIconLabelPair(showLabel, CreateBoardHeaderLabel, CreateBoardHeaderIcon);
            AdaptiveLayout.SetIconLabelPair(showLabel, JoinByCodeHeaderLabel, JoinByCodeHeaderIcon);
            AdaptiveLayout.SetIconLabelPair(showLabel, LogoutHeaderLabel, LogoutHeaderIcon);

            var compact = !showLabel;
            AdaptiveLayout.SetCompactButtonPadding(NotificationCenterButton, compact);
            AdaptiveLayout.SetCompactButtonPadding(JoinByCodeHeaderButton, compact);
            AdaptiveLayout.SetCompactButtonPadding(LogoutHeaderButton, compact);

            CreateBoardHeaderButton.Tag = compact ? "compact" : null;
            if (compact)
            {
                AdaptiveLayout.SetCompactButtonPadding(CreateBoardHeaderButton, true, 0, 0);
            }
            else
            {
                CreateBoardHeaderButton.ClearValue(FrameworkElement.WidthProperty);
                CreateBoardHeaderButton.ClearValue(FrameworkElement.HeightProperty);
                CreateBoardHeaderButton.ClearValue(FrameworkElement.MinWidthProperty);
                AdaptiveLayout.SetCompactButtonPadding(CreateBoardHeaderButton, false);
            }

            GreetingTextBlock.FontSize = tier == AdaptiveWidthTier.Compact ? 22 : 28;
        }

        private void ApplyFilterBarLayout(bool stacked)
        {
            if (FilterBarGrid == null || SortComboBox == null || ViewModePanel == null)
            {
                return;
            }

            if (stacked)
            {
                FilterBarGrid.RowDefinitions[1].Height = GridLength.Auto;
                Grid.SetRow(SortComboBox, 1);
                Grid.SetColumn(SortComboBox, 0);
                Grid.SetColumnSpan(SortComboBox, 2);
                SortComboBox.Margin = new Thickness(0, 10, 10, 0);
                SortComboBox.Width = double.NaN;
                SortComboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
                Grid.SetRow(ViewModePanel, 1);
                Grid.SetColumn(ViewModePanel, 2);
                ViewModePanel.Margin = new Thickness(0, 10, 0, 0);
            }
            else
            {
                FilterBarGrid.RowDefinitions[1].Height = new GridLength(0);
                Grid.SetRow(SortComboBox, 0);
                Grid.SetColumn(SortComboBox, 1);
                Grid.SetColumnSpan(SortComboBox, 1);
                SortComboBox.Margin = new Thickness(14, 0, 10, 0);
                SortComboBox.Width = 180;
                SortComboBox.HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetRow(ViewModePanel, 0);
                Grid.SetColumn(ViewModePanel, 2);
                ViewModePanel.Margin = new Thickness(0);
            }
        }
    }

    public sealed class HomeBoardCard : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

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

        private ImageSource? _thumbnail;

        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (ReferenceEquals(_thumbnail, value))
                {
                    return;
                }

                _thumbnail = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }

        public bool HasThumbnail => Thumbnail != null;

        private bool _isFavorite;

        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (_isFavorite == value)
                {
                    return;
                }

                _isFavorite = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
