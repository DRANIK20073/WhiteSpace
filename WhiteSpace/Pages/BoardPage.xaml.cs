using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Newtonsoft.Json;
using Supabase;
using IOPath = System.IO.Path;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Linq;
using System.Net;
using System.Reactive.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WhiteSpace;
using WhiteSpace.Services;

namespace WhiteSpace.Pages
{
    public partial class BoardPage : Page
    {
        private List<BoardShape> _shapesOnBoard = new List<BoardShape>();
        private readonly Guid _boardId;
        private Board? _boardInfo;
        private readonly SupabaseService _supabaseService;
        private readonly FirebaseService _firebaseService;
        private bool _isLoadingShapes = false; // Флаг для предотвращения двойной загрузки
        private bool _isRestoringHistory;

        private IDisposable _shapesSubscription;
        private IDisposable _membersSubscription;
        private IDisposable _cursorsSubscription;
        private IDisposable _chatSubscription;
        private enum ToolMode { Hand, Pen, Rect, Ellipse, Text }
        private ToolMode _tool = ToolMode.Hand;

        private bool _isCreatingShape = false;
        private Dictionary<string, Point> _originalCorners;

        // Пан/камера
        private bool _isPanning;
        private Point _panStartScreen;
        private double _panStartX, _panStartY;

        // Рисование (карандаш)
        private bool _isDrawing;
        private Polyline _currentStroke;

        //Выбор цвета
        private Brush _currentBrush = Brushes.Black;
        private string _currentColorString = "black";

        // Призрак фигуры
        private Shape _previewShape;
        private const double DefaultRectW = 140;
        private const double DefaultRectH = 90;
        private const double DefaultEllipse = 100;

        // Изменение размеров фигуры
        private bool _isResizing;
        private UIElement _resizeTarget;
        private Rectangle _resizeBorder;
        private string _resizeDirection;
        private Point _resizeStartWorld;
        private double _startW, _startH, _startX, _startY;

        // Перетаскивание объектов
        private bool _isDraggingElement;
        private UIElement _dragElement;
        private UIElement _selectedElement;
        private Point _dragOffsetWorld;
        private Point _dragStartWorld;
        private bool _wasTextEditingEnabled;

        // Словарь для хранения ручек изменения размера
        private Dictionary<string, Rectangle> _resizeHandles = new Dictionary<string, Rectangle>();
        private readonly Stack<List<BoardShape>> _undoHistory = new Stack<List<BoardShape>>();
        private readonly Stack<List<BoardShape>> _redoHistory = new Stack<List<BoardShape>>();
        private const double DefaultImageW = 280;
        private const double DefaultImageH = 180;
        private static readonly string BoardSnapshotsRoot = IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhiteSpace",
            "board-snapshots");
        private bool _isPresentationMode;
        private Rect _presentationRestoreBounds;
        private WindowState _presentationRestoreWindowState = WindowState.Normal;
        private ResizeMode _presentationRestoreResizeMode = ResizeMode.CanResize;
        private bool _presentationStoredState;
        private DateTime _lastResizeRealtimePushUtc = DateTime.MinValue;
        private bool _removalHandled;
        private readonly DispatcherTimer _accessMonitorTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        private bool _isAccessCheckRunning;
        private readonly Dictionary<Guid, FirebaseBoardMember> _presenceByUserId = new();
        private readonly Dictionary<Guid, FirebaseCursorState> _cursorByUserId = new();
        private readonly Dictionary<Guid, FrameworkElement> _cursorVisuals = new();
        private readonly Dictionary<Guid, Brush> _cursorAccentByUserId = new();
        private DateTime _lastCursorPublishUtc = DateTime.MinValue;
        private const int CursorPublishThrottleMs = 50;
        private static readonly TimeSpan CursorOfflineTimeout = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan PresenceHeartbeatInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PresenceOfflineTimeout = TimeSpan.FromSeconds(15);
        private readonly DispatcherTimer _presenceHeartbeatTimer = new() { Interval = PresenceHeartbeatInterval };
        private readonly DispatcherTimer _presenceUiRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        private bool _isPresenceHeartbeatInFlight;
        private bool _isPresenceUiRefreshInFlight;
        private bool _isPageUnloading;
        private Guid? _myUserId;
        private string? _myUserRole;
        private DateTime _myJoinedAtUtc = DateTime.MinValue;
        private List<BoardMember> _cachedBoardMembers = new();
        private Guid? _cachedCurrentUserId;
        private readonly Dictionary<Guid, (string DisplayName, string Initials)> _profileDisplayNameCache = new();
        private readonly bool _returnToAdminPage;
        private bool _isAdminSession;
        private string _cursorDisplayName = "Участник";
        private readonly ObservableCollection<ChatMessageViewModel> _chatMessages = new();

        public BoardPage(Guid boardId, bool returnToAdminPage = false)
        {
            InitializeComponent();
            _boardId = boardId;
            _returnToAdminPage = returnToAdminPage;
            _supabaseService = new SupabaseService();
            _firebaseService = new FirebaseService();

            // Добавляем обработчики событий для Viewport
            Viewport.MouseDown += Viewport_MouseDown;
            Viewport.MouseMove += Viewport_MouseMove;
            Viewport.MouseUp += Viewport_MouseUp;
            Viewport.MouseWheel += Viewport_MouseWheel;
            Viewport.MouseLeave += Viewport_MouseLeave;

            Loaded += Page_Loaded;
            Unloaded += Page_Unloaded;
            _accessMonitorTimer.Tick += AccessMonitorTimer_Tick;
            _presenceHeartbeatTimer.Tick += PresenceHeartbeatTimer_Tick;
            _presenceUiRefreshTimer.Tick += PresenceUiRefreshTimer_Tick;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var prefs = AppPreferences.Load();
            UiAnimationHelper.ApplyFadeIn(BoardRootGrid, prefs.EnableAnimations);

            await LoadBoardMetadataAsync();
            _isAdminSession = await _supabaseService.IsCurrentUserAdminAsync();

            // Загружаем фигуры из Supabase
            await LoadShapesFromSupabase();

            // Определяем роль пользователя
            var userRole = _isAdminSession ? "owner" : await _supabaseService.GetUserRoleForBoardAsync(_boardId);

            if (userRole == "viewer" || userRole == "editor")
            {
                await LoadBoardMembers();
                UsersListView.IsEnabled = false;
                SetViewerMode(userRole == "viewer");
            }
            else if (userRole == "owner")
            {
                await LoadBoardMembers();
                UsersListView.IsEnabled = true;
                SetViewerMode(false);
            }
            else
            {
                UsersListView.Visibility = Visibility.Collapsed;
                SetViewerMode(false);
            }

            // Центрируем камеру
            CenterViewport();

            // Устанавливаем начальный инструмент
            SetTool(ToolMode.Hand);

            // Подписываемся на изменения фигур из Firebase (только для получения обновлений)
            SubscribeToShapes();
            SubscribeToBoardMembers();
            SubscribeToCursors();
            SubscribeToChatMessages();
            await SetCurrentUserPresenceAsync(true);
            await InitCursorIdentityAsync();
            ChatMessagesItemsControl.ItemsSource = _chatMessages;
            _accessMonitorTimer.Start();
            _presenceHeartbeatTimer.Start();
            _presenceUiRefreshTimer.Start();
        }

        private async Task LoadBoardMetadataAsync()
        {
            _boardInfo = await _supabaseService.GetBoardByIdAsync(_boardId);

            BoardTitleText.Text = string.IsNullOrWhiteSpace(_boardInfo?.Title)
                ? "Моя доска"
                : _boardInfo.Title;

            MarkSaved();
        }

        private void MarkSaved()
        {
            if (SaveStatusText != null)
            {
                SaveStatusText.Text = $"Сохранено {DateTime.Now:HH:mm}";
            }
        }

        private void CaptureBoardStateForUndo()
        {
            if (_isRestoringHistory)
            {
                return;
            }

            _undoHistory.Push(CloneShapes(_shapesOnBoard));
            _redoHistory.Clear();
        }

        private static List<BoardShape> CloneShapes(IEnumerable<BoardShape> shapes)
        {
            return shapes.Select(CloneShape).ToList();
        }

        private static BoardShape CloneShape(BoardShape shape)
        {
            return new BoardShape
            {
                Id = shape.Id,
                BoardId = shape.BoardId,
                Type = shape.Type,
                X = shape.X,
                Y = shape.Y,
                Width = shape.Width,
                Height = shape.Height,
                Color = shape.Color,
                Text = shape.Text,
                Points = shape.Points,
                DeserializedPoints = shape.DeserializedPoints != null
                    ? shape.DeserializedPoints.Select(point => new Point(point.X, point.Y)).ToList()
                    : new List<Point>()
            };
        }

        private void RenderCurrentBoardState()
        {
            RemoveResizeFrame();
            RemovePreviewShape();

            _currentStroke = null;
            _isDrawing = false;
            _isDraggingElement = false;
            _dragElement = null;

            BoardCanvas.Children.Clear();

            foreach (var shape in _shapesOnBoard)
            {
                AddShapeToCanvas(CloneShape(shape), false);
            }

            if (_tool == ToolMode.Rect || _tool == ToolMode.Ellipse)
            {
                EnsurePreviewShape();
            }
        }

        private async Task RestoreBoardStateAsync(List<BoardShape> snapshot)
        {
            _isRestoringHistory = true;
            try
            {
                _shapesOnBoard = CloneShapes(snapshot);
                RenderCurrentBoardState();
                await _supabaseService.ReplaceBoardShapesAsync(_boardId, _shapesOnBoard);

                foreach (var shape in _shapesOnBoard)
                {
                    PushShapeToFirebase(shape);
                }

                MarkSaved();
            }
            finally
            {
                _isRestoringHistory = false;
            }
        }

        private void CenterViewport()
        {
            var viewportCenter = new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2);
            var canvasCenter = new Point(BoardCanvas.Width / 2, BoardCanvas.Height / 2);

            BoardTranslate.X = viewportCenter.X - canvasCenter.X;
            BoardTranslate.Y = viewportCenter.Y - canvasCenter.Y;
        }

        private async void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isPageUnloading = true;

            // Отписываемся от событий при выходе
            _shapesSubscription?.Dispose();
            _membersSubscription?.Dispose();
            _cursorsSubscription?.Dispose();
            _chatSubscription?.Dispose();
            _accessMonitorTimer.Stop();
            _presenceHeartbeatTimer.Stop();
            _presenceUiRefreshTimer.Stop();
            await RemoveCurrentUserCursorAsync();

            try
            {
                await SetCurrentUserPresenceAsync(false);
            }
            catch
            {
                // Не критично: при закрытии приложения запрос presence может не успеть завершиться.
            }
        }

        private async void AccessMonitorTimer_Tick(object? sender, EventArgs e)
        {
            await RefreshCurrentUserPermissionsAsync();
        }

        private async void PresenceHeartbeatTimer_Tick(object? sender, EventArgs e)
        {
            if (_isPageUnloading || _isPresenceHeartbeatInFlight)
            {
                return;
            }

            _isPresenceHeartbeatInFlight = true;
            try
            {
                await SetCurrentUserPresenceAsync(true);
            }
            finally
            {
                _isPresenceHeartbeatInFlight = false;
            }
        }

        private async void PresenceUiRefreshTimer_Tick(object? sender, EventArgs e)
        {
            if (_isPageUnloading || _isPresenceUiRefreshInFlight)
            {
                return;
            }

            if (_cachedBoardMembers.Count == 0)
            {
                return;
            }

            _isPresenceUiRefreshInFlight = true;
            try
            {
                UsersListView.ItemsSource = await CreateParticipantCardsAsync(
                    _cachedBoardMembers,
                    _cachedCurrentUserId,
                    _presenceByUserId);
            }
            finally
            {
                _isPresenceUiRefreshInFlight = false;
            }
        }

        // Загрузка фигур из Supabase
        private async System.Threading.Tasks.Task LoadShapesFromSupabase()
        {
            try
            {
                _isLoadingShapes = true;
                var shapes = await _supabaseService.LoadBoardShapesAsync(_boardId);

                foreach (var shape in shapes)
                {
                    AddShapeToCanvas(shape);
                }
            }
            catch (Exception ex)
            {
                AppDialogService.ShowError($"Ошибка загрузки фигур: {ex.Message}", "Доска");
            }
            finally
            {
                _isLoadingShapes = false;
            }
        }

        // Подписка на изменения из Firebase (только для получения обновлений от других пользователей)
        // Подписка на изменения из Firebase (только для получения обновлений от других пользователей)
        private void SubscribeToShapes()
        {
            try
            {
                _shapesSubscription = _firebaseService
                    .GetShapesObservable(_boardId.ToString())
                    .Where(shape => shape != null)
                    .Subscribe(async shape =>
                    {
                        // Игнорируем обновления, если идет загрузка из Supabase
                        if (_isLoadingShapes)
                            return;

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            UpdateOrAddShapeFromFirebase(shape);
                        });
                    });

                // Подписываемся на изменения участников
                SubscribeToBoardMembers();
            }
            catch (Exception ex)
            {
                AppDialogService.ShowError($"Ошибка подписки на Firebase: {ex.Message}", "Доска");
            }
        }

        // Обновление или добавление фигуры из Firebase (от других пользователей)
        private void UpdateOrAddShapeFromFirebase(BoardShape shape)
        {
            var existingShapeIndex = _shapesOnBoard.FindIndex(s => s.Id == shape.Id);

            if (existingShapeIndex >= 0)
            {
                // Если фигура уже существует, обновляем её
                var existingShape = _shapesOnBoard[existingShapeIndex];
                var uiElement = FindUIElementByUid(shape.Id.ToString());

                if (uiElement != null)
                {
                    Console.WriteLine($"Получено обновление для фигуры {shape.Id}: цвет {shape.Color}");

                    // Сначала обновляем данные фигуры
                    existingShape.Color = shape.Color;
                    existingShape.X = shape.X;
                    existingShape.Y = shape.Y;
                    existingShape.Width = shape.Width;
                    existingShape.Height = shape.Height;
                    existingShape.Text = shape.Text;

                    // Обновляем UI элемент (включая цвет)
                    UpdateUIElementFromShape(uiElement, shape);

                    if (shape.Type == "line" && !string.IsNullOrEmpty(shape.Points))
                    {
                        existingShape.Points = shape.Points;
                        existingShape.DeserializedPoints = JsonConvert.DeserializeObject<List<Point>>(shape.Points);

                        if (uiElement is Polyline targetPolyline)
                        {
                            targetPolyline.Points.Clear();
                            foreach (var point in existingShape.DeserializedPoints)
                            {
                                targetPolyline.Points.Add(point);
                            }
                        }
                    }
                }
            }
            else
            {
                // Добавляем новую фигуру (созданную другим пользователем)
                Console.WriteLine($"Получена новая фигура из Firebase: {shape.Id}, цвет {shape.Color}");
                _shapesOnBoard.Add(shape);
                AddShapeToCanvas(shape);
            }
        }

        // Обновление UI элемента из данных фигуры
        private void UpdateUIElementFromShape(UIElement element, BoardShape shape)
        {
            var brush = GetBrushFromColor(shape.Color);

            if (element is Shape shapeElement)
            {
                shapeElement.Stroke = brush;
            }
            else if (element is Polyline polyline)
            {
                polyline.Stroke = brush;
            }
            else if (element is TextBox textBox)
            {
                textBox.Foreground = brush;
                textBox.Text = shape.Text;
            }
            else if (element is Image image)
            {
                image.Source = CreateImageSource(shape.Text);
            }

            // Обновляем позицию и размеры
            if (shape.Type == "text")
            {
                Canvas.SetLeft(element, shape.X);
                Canvas.SetTop(element, shape.Y);

                if (element is TextBox tb)
                {
                    tb.Width = shape.Width;
                    tb.Height = shape.Height;
                }
            }
            else if (shape.Type == "line")
            {
                if (element is Polyline targetPolyline && !string.IsNullOrEmpty(shape.Points))
                {
                    var points = JsonConvert.DeserializeObject<List<Point>>(shape.Points);
                    targetPolyline.Points.Clear();
                    foreach (var point in points)
                    {
                        targetPolyline.Points.Add(point);
                    }
                }
            }
            else
            {
                Canvas.SetLeft(element, shape.X - shape.Width / 2);
                Canvas.SetTop(element, shape.Y - shape.Height / 2);

                if (element is FrameworkElement fe)
                {
                    fe.Width = shape.Width;
                    fe.Height = shape.Height;
                }
            }
        }

        // Отправка изменений в Firebase (для реалтайм обновлений)
        private async void PushShapeToFirebase(BoardShape shape)
        {
            try
            {
                await _firebaseService.PushShapeAsync(_boardId.ToString(), shape);
            }
            catch (Exception ex)
            {
                // Логируем ошибку, но не показываем пользователю
                System.Diagnostics.Debug.WriteLine($"Ошибка отправки в Firebase: {ex.Message}");
            }
        }

        private UIElement FindUIElementByUid(string uid)
        {
            foreach (var child in BoardCanvas.Children)
            {
                if (child is UIElement element && element.Uid == uid)
                {
                    return element;
                }
            }
            return null;
        }

        // Подписка на изменения участников доски
        private void SubscribeToBoardMembers()
        {
            try
            {
                _membersSubscription = _firebaseService
                    .GetBoardMembersObservable(_boardId.ToString())
                    .Where(members => members != null)
                    .Subscribe(async members =>
                    {
                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            await UpdateBoardMembersFromFirebase(members);
                        });
                    });

                Console.WriteLine("Подписка на участников успешно создана");
            }
            catch (Exception ex)
            {
                AppDialogService.ShowError($"Ошибка подписки на участников: {ex.Message}", "Участники доски");
            }
        }

        private async Task SetCurrentUserPresenceAsync(bool isOnline)
        {
            try
            {
                if (_isAdminSession)
                {
                    return;
                }

                _myUserId ??= (await _supabaseService.GetMyProfileAsync())?.Id;
                if (_myUserId == null)
                {
                    return;
                }

                _myUserRole ??= await _supabaseService.GetUserRoleForBoardAsync(_boardId);
                if (string.IsNullOrWhiteSpace(_myUserRole))
                {
                    return;
                }

                if (_myJoinedAtUtc == DateTime.MinValue)
                {
                    _myJoinedAtUtc = DateTime.UtcNow;
                }

                var joinedAtUtc = _myJoinedAtUtc;

                await _firebaseService.PushBoardMemberAsync(_boardId.ToString(), new FirebaseBoardMember
                {
                    UserId = _myUserId.ToString(),
                    Role = _myUserRole,
                    JoinedAt = joinedAtUtc,
                    IsOnline = isOnline,
                    LastSeenUtc = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка отправки presence в Firebase: {ex.Message}");
            }
        }

        private async Task InitCursorIdentityAsync()
        {
            if (_isAdminSession)
            {
                return;
            }

            var profile = await _supabaseService.GetMyProfileAsync();
            _myUserId ??= profile?.Id;
            if (_myUserId == null)
            {
                return;
            }

            _cursorDisplayName = !string.IsNullOrWhiteSpace(profile?.Username)
                ? profile.Username
                : (!string.IsNullOrWhiteSpace(profile?.Email) ? profile.Email : $"User {_myUserId.Value.ToString("N")[..6]}");
        }

        private void SubscribeToCursors()
        {
            try
            {
                _cursorsSubscription = _firebaseService
                    .GetBoardCursorsObservable(_boardId.ToString())
                    .Where(states => states != null)
                    .Subscribe(states =>
                    {
                        Application.Current.Dispatcher.Invoke(() => UpdateRemoteCursors(states));
                    });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка подписки на курсоры: {ex.Message}");
            }
        }

        private void UpdateRemoteCursors(List<FirebaseCursorState> states)
        {
            var now = DateTime.UtcNow;
            var currentStates = new Dictionary<Guid, FirebaseCursorState>();

            foreach (var state in states)
            {
                if (!Guid.TryParse(state.UserId, out var userId))
                {
                    continue;
                }

                if (_myUserId.HasValue && userId == _myUserId.Value)
                {
                    continue;
                }

                var isFresh = state.IsVisible && (now - state.UpdatedAtUtc) <= CursorOfflineTimeout;
                if (!isFresh)
                {
                    continue;
                }

                currentStates[userId] = state;
                _cursorByUserId[userId] = state;
                DrawOrMoveRemoteCursor(userId, state);
            }

            foreach (var userId in _cursorVisuals.Keys.ToList())
            {
                if (currentStates.ContainsKey(userId))
                {
                    continue;
                }

                CursorCanvas.Children.Remove(_cursorVisuals[userId]);
                _cursorVisuals.Remove(userId);
                _cursorByUserId.Remove(userId);
            }
        }

        private Brush ResolveCursorAccentBrush(Guid userId)
        {
            if (_cursorAccentByUserId.TryGetValue(userId, out var brush) && brush != null)
            {
                return brush;
            }

            var (_, stroke) = GetParticipantPalette(Math.Abs(userId.GetHashCode()));
            return stroke;
        }

        private static Brush CloneBrushForFill(Brush brush)
        {
            if (brush is SolidColorBrush scb)
            {
                return new SolidColorBrush(scb.Color);
            }

            return brush.Clone();
        }

        private void DrawOrMoveRemoteCursor(Guid userId, FirebaseCursorState state)
        {
            if (!_cursorVisuals.TryGetValue(userId, out var visual))
            {
                var stack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    IsHitTestVisible = false
                };

                var pointer = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(0, 0),
                        new Point(0, 14),
                        new Point(9, 9)
                    },
                    Fill = CloneBrushForFill(ResolveCursorAccentBrush(userId)),
                    Stroke = Brushes.White,
                    StrokeThickness = 1
                };

                var nameTag = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 2, 0, 0),
                    Child = new TextBlock
                    {
                        Foreground = Brushes.White,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Text = state.DisplayName
                    }
                };

                stack.Children.Add(pointer);
                stack.Children.Add(nameTag);
                _cursorVisuals[userId] = stack;
                CursorCanvas.Children.Add(stack);
                visual = stack;
            }
            else if (visual is StackPanel existingStack
                && existingStack.Children.Count > 1
                && existingStack.Children[1] is Border tagBorder
                && tagBorder.Child is TextBlock tagText)
            {
                tagText.Text = state.DisplayName;
                if (existingStack.Children[0] is Polygon pointerPoly)
                {
                    pointerPoly.Fill = CloneBrushForFill(ResolveCursorAccentBrush(userId));
                }
            }

            Canvas.SetLeft(visual, state.X);
            Canvas.SetTop(visual, state.Y);
        }

        private async Task PublishCursorAsync(Point worldPoint, bool isVisible)
        {
            if (_isAdminSession)
            {
                return;
            }

            if (_myUserId == null)
            {
                await InitCursorIdentityAsync();
            }

            if (_myUserId == null)
            {
                return;
            }

            await _firebaseService.UpsertCursorAsync(_boardId.ToString(), new FirebaseCursorState
            {
                UserId = _myUserId.ToString()!,
                DisplayName = _cursorDisplayName,
                X = worldPoint.X,
                Y = worldPoint.Y,
                IsVisible = isVisible,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        private async Task RemoveCurrentUserCursorAsync()
        {
            if (_myUserId == null)
            {
                return;
            }

            await _firebaseService.DeleteCursorAsync(_boardId.ToString(), _myUserId.ToString()!);
        }

        private async void Viewport_MouseLeave(object sender, MouseEventArgs e)
        {
            await RemoveCurrentUserCursorAsync();
        }

        // Обновление списка участников из Firebase
        // Обновление списка участников из Firebase
        private async Task UpdateBoardMembersFromFirebase(List<FirebaseBoardMember> members)
        {
            try
            {
                Console.WriteLine($"Получено обновление участников из Firebase. Количество: {members?.Count ?? 0}");

                await RefreshCurrentUserPermissionsAsync();
                var currentUser = _isAdminSession ? null : await _supabaseService.GetMyProfileAsync();
                _presenceByUserId.Clear();
                if (members != null)
                {
                    foreach (var member in members)
                    {
                        if (Guid.TryParse(member.UserId, out var userId))
                        {
                            _presenceByUserId[userId] = member;
                        }
                    }
                }

                if (members != null && members.Any())
                {
                    // Получаем полные данные из Supabase для отображения
                    var supabaseMembers = await _supabaseService.GetBoardMembersAsync(_boardId);
                    Console.WriteLine($"Загружено участников из Supabase: {supabaseMembers.Count}");

                    // Сопоставляем Firebase данные с Supabase
                    var displayMembers = new List<BoardMember>();

                    foreach (var fbMember in members)
                    {
                        var supabaseMember = supabaseMembers.FirstOrDefault(m => m.UserId.ToString() == fbMember.UserId);
                        if (supabaseMember != null)
                        {
                            displayMembers.Add(supabaseMember);
                        }
                    }

                    Console.WriteLine($"Отображаем участников: {displayMembers.Count}");

                    UsersListView.ItemsSource = null;
                    _cachedBoardMembers = displayMembers;
                    _cachedCurrentUserId = currentUser?.Id;
                    UsersListView.ItemsSource = await CreateParticipantCardsAsync(displayMembers, currentUser?.Id, _presenceByUserId);
                    UsersListView.Visibility = Visibility.Visible;
                    MembersCountText.Text = displayMembers.Count.ToString();

                    // Обновляем доступность кнопок в зависимости от роли
                    var currentUserMember = displayMembers.FirstOrDefault(m => m.UserId == currentUser?.Id);
                    if (currentUserMember != null)
                    {
                        UsersListView.IsEnabled = currentUserMember.Role == "owner";
                        Console.WriteLine($"Текущий пользователь имеет роль: {currentUserMember.Role}");
                    }
                }
                else
                {
                    UsersListView.ItemsSource = null;
                    UsersListView.Visibility = Visibility.Collapsed;
                    MembersCountText.Text = "0";
                    Console.WriteLine("Нет участников для отображения");
                    _cachedBoardMembers.Clear();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка обновления списка участников: {ex.Message}");
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
        }

        private async Task<bool> PushBoardMembersToFirebaseAsync()
        {
            try
            {
                var members = await _supabaseService.GetBoardMembersAsync(_boardId);

                // Преобразуем в формат Firebase
                var firebaseMembers = members.Select(m => new FirebaseBoardMember
                {
                    UserId = m.UserId.ToString(),
                    Role = m.Role,
                    JoinedAt = m.JoinedAt
                }).ToList();

                await _firebaseService.PushBoardMembersAsync(_boardId.ToString(), firebaseMembers);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка отправки участников в Firebase: {ex.Message}");
                return false;
            }
        }

        private async Task LoadBoardMembers()
        {
            try
            {
                var boardMembers = await _supabaseService.GetBoardMembersAsync(_boardId);

                // Получаем текущего пользователя
                var currentUser = _isAdminSession ? null : await _supabaseService.GetMyProfileAsync();

                if (boardMembers != null && boardMembers.Any())
                {
                    _cachedBoardMembers = boardMembers;
                    _cachedCurrentUserId = currentUser?.Id;

                    // Пока Firebase-подписка не успела заполнить _presenceByUserId,
                    // отметим себя как онлайн локально, чтобы карточка сразу отображалась корректно.
                    if (currentUser != null)
                    {
                        var myMember = boardMembers.FirstOrDefault(m => m.UserId == currentUser.Id);
                        if (myMember != null)
                        {
                            _presenceByUserId[currentUser.Id] = new FirebaseBoardMember
                            {
                                UserId = currentUser.Id.ToString(),
                                Role = myMember.Role,
                                JoinedAt = DateTime.UtcNow,
                                IsOnline = true,
                                LastSeenUtc = DateTime.UtcNow
                            };
                        }
                    }

                    UsersListView.ItemsSource = await CreateParticipantCardsAsync(boardMembers, currentUser?.Id, _presenceByUserId);
                    UsersListView.Visibility = Visibility.Visible;
                    MembersCountText.Text = boardMembers.Count.ToString();

                    await PushBoardMembersToFirebaseAsync();
                }
                else
                {
                    UsersListView.ItemsSource = null;
                    UsersListView.Visibility = Visibility.Collapsed;
                    MembersCountText.Text = "0";
                    _cachedBoardMembers.Clear();
                }
            }
            catch (Exception ex)
            {
                AppDialogService.ShowError($"Ошибка при загрузке участников: {ex.Message}", "Участники доски");
            }
        }

        private async void ToggleParticipantRoleMenu_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var boardMember = menuItem?.DataContext as BoardParticipantCard;

            if (boardMember == null)
            {
                AppDialogService.ShowWarning("Участник не выбран.", "Изменение роли");
                return;
            }

            if (boardMember.Role == "owner")
            {
                AppDialogService.ShowWarning("Вы не можете изменить роль владельца.", "Изменение роли");
                return;
            }

            string newRole = boardMember.Role switch
            {
                "viewer" => "editor",
                "editor" => "viewer",
                _ => boardMember.Role
            };

            if (newRole == boardMember.Role)
            {
                return;
            }

            var result = await _supabaseService.UpdateBoardMemberRoleAsync(_boardId, boardMember.UserId, newRole);

            if (result)
            {
                // Синхронно публикуем изменение в Firebase для мгновенного обновления у всех.
                await PushBoardMembersToFirebaseAsync();
                await LoadBoardMembers();

                AppDialogService.ShowSuccess($"Роль пользователя изменена на {newRole}.", "Изменение роли");
            }
            else
            {
                AppDialogService.ShowError("Не удалось изменить роль.", "Изменение роли");
            }
        }

        private void ParticipantActionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.ContextMenu == null)
            {
                return;
            }

            button.ContextMenu.DataContext = button.DataContext;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }

        private void CopyParticipantIdMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.DataContext is not BoardParticipantCard boardMember)
            {
                return;
            }

            Clipboard.SetText(boardMember.UserId.ToString());
            AppDialogService.ShowInfo("ID пользователя скопирован в буфер обмена.", "Участники");
        }

        private async void RemoveParticipantMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.DataContext is not BoardParticipantCard boardMember)
            {
                return;
            }

            if (!AppDialogService.ShowConfirmation(
                    $"Удалить пользователя \"{boardMember.DisplayName}\" с доски?",
                    "Удаление участника",
                    "Удалить",
                    "Отмена"))
            {
                return;
            }

            var result = await _supabaseService.RemoveBoardMemberAsync(_boardId, boardMember.UserId);
            if (result)
            {
                // Сначала обновляем Firebase, затем локальный список.
                await PushBoardMembersToFirebaseAsync();
                await LoadBoardMembers();
                AppDialogService.ShowSuccess("Пользователь удалён с доски.", "Удаление участника");
            }
        }

        private async Task<List<BoardParticipantCard>> CreateParticipantCardsAsync(
            List<BoardMember> members,
            Guid? currentUserId,
            IReadOnlyDictionary<Guid, FirebaseBoardMember>? presenceByUserId = null)
        {
            var cards = new List<BoardParticipantCard>();
            int colorIndex = 0;
            _cursorAccentByUserId.Clear();

            foreach (var member in members)
            {
                var accountUsername = member.AccountUsername;

                // Если в board_members есть снимок username — берём его и не трогаем profiles.
                if (!string.IsNullOrWhiteSpace(accountUsername))
                {
                    var accountDisplayName = accountUsername;
                    var accountInitials = GetInitials(accountDisplayName);

                    var (accountFill, accountStroke) = GetParticipantPalette(colorIndex++);
                    _cursorAccentByUserId[member.UserId] = accountStroke;
                    var accountIsCurrentUser = currentUserId.HasValue && member.UserId == currentUserId.Value;

                    var accountIsOnline = false;
                    if (presenceByUserId != null
                        && presenceByUserId.TryGetValue(member.UserId, out var accountPresence))
                    {
                        if (accountPresence.IsOnline)
                        {
                            var lastSeenUtc = accountPresence.LastSeenUtc;
                            if (lastSeenUtc != DateTime.MinValue)
                            {
                                var age = DateTime.UtcNow - lastSeenUtc;
                                accountIsOnline = age <= PresenceOfflineTimeout;
                            }
                        }
                    }

                    cards.Add(new BoardParticipantCard
                    {
                        UserId = member.UserId,
                        DisplayName = accountDisplayName,
                        Initials = accountInitials,
                        Role = member.Role,
                        RoleLabel = member.Role switch
                        {
                            "owner" => "Ведущий",
                            "editor" => "Редактор",
                            _ => "Наблюдатель"
                        },
                        RoleActionLabel = member.Role == "viewer" ? "Сделать редактором" : "Сделать наблюдателем",
                        RoleActionVisibility = member.Role == "owner" ? Visibility.Collapsed : Visibility.Visible,
                        ActionVisibility = accountIsCurrentUser || member.Role == "owner" ? Visibility.Collapsed : Visibility.Visible,
                        RemoveActionVisibility = accountIsCurrentUser || member.Role == "owner" ? Visibility.Collapsed : Visibility.Visible,
                        CurrentUserHint = accountIsCurrentUser ? "Вы" : string.Empty,
                        CurrentUserHintVisibility = accountIsCurrentUser ? Visibility.Visible : Visibility.Collapsed,
                        PresenceLabel = accountIsOnline ? "Онлайн" : "Не в сети",
                        PresenceDotFill = accountIsOnline
                            ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                            : new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                        PresenceTextFill = accountIsOnline
                            ? new SolidColorBrush(Color.FromRgb(22, 163, 74))
                            : new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                        IsCurrentUser = accountIsCurrentUser,
                        IsOnline = accountIsOnline,
                        AvatarFill = accountFill,
                        AvatarStroke = accountStroke,
                        RoleBadgeBackground = member.Role == "owner"
                            ? new SolidColorBrush(Color.FromRgb(14, 16, 38))
                            : new SolidColorBrush(Color.FromRgb(244, 246, 251)),
                        RoleBadgeForeground = member.Role == "owner"
                            ? Brushes.White
                            : new SolidColorBrush(Color.FromRgb(73, 80, 96))
                    });

                    continue;
                }

                if (!_profileDisplayNameCache.TryGetValue(member.UserId, out var cachedProfile))
                {
                    var profile = await _supabaseService.GetProfileByUserIdAsync(member.UserId);
                    string computedDisplayName = !string.IsNullOrWhiteSpace(profile?.Username)
                        ? profile.Username
                        : member.UserId.ToString()[..8];

                    cachedProfile = (computedDisplayName, GetInitials(computedDisplayName));
                    _profileDisplayNameCache[member.UserId] = cachedProfile;
                }

                string displayName = cachedProfile.DisplayName;
                string initials = cachedProfile.Initials;

                var (fill, stroke) = GetParticipantPalette(colorIndex++);
                _cursorAccentByUserId[member.UserId] = stroke;
                var isCurrentUser = currentUserId.HasValue && member.UserId == currentUserId.Value;
                var isOnline = false;
                if (presenceByUserId != null
                    && presenceByUserId.TryGetValue(member.UserId, out var presence))
                {
                    // Если явно выставили IsOnline=false — считаем офлайн сразу.
                    if (presence.IsOnline)
                    {
                        var lastSeenUtc = presence.LastSeenUtc;
                        if (lastSeenUtc != DateTime.MinValue)
                        {
                            var age = DateTime.UtcNow - lastSeenUtc;
                            isOnline = age <= PresenceOfflineTimeout;
                        }
                    }
                }
                cards.Add(new BoardParticipantCard
                {
                    UserId = member.UserId,
                    DisplayName = displayName,
                    Initials = initials,
                    Role = member.Role,
                    RoleLabel = member.Role switch
                    {
                        "owner" => "Ведущий",
                        "editor" => "Редактор",
                        _ => "Наблюдатель"
                    },
                    RoleActionLabel = member.Role == "viewer" ? "Сделать редактором" : "Сделать наблюдателем",
                    RoleActionVisibility = member.Role == "owner" ? Visibility.Collapsed : Visibility.Visible,
                    ActionVisibility = isCurrentUser || member.Role == "owner" ? Visibility.Collapsed : Visibility.Visible,
                    RemoveActionVisibility = isCurrentUser || member.Role == "owner" ? Visibility.Collapsed : Visibility.Visible,
                    CurrentUserHint = isCurrentUser ? "Вы" : string.Empty,
                    CurrentUserHintVisibility = isCurrentUser ? Visibility.Visible : Visibility.Collapsed,
                    PresenceLabel = isOnline ? "Онлайн" : "Не в сети",
                    PresenceDotFill = isOnline
                        ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                        : new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                    PresenceTextFill = isOnline
                        ? new SolidColorBrush(Color.FromRgb(22, 163, 74))
                        : new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                    IsCurrentUser = isCurrentUser,
                    IsOnline = isOnline,
                    AvatarFill = fill,
                    AvatarStroke = stroke,
                    RoleBadgeBackground = member.Role == "owner"
                        ? new SolidColorBrush(Color.FromRgb(14, 16, 38))
                        : new SolidColorBrush(Color.FromRgb(244, 246, 251)),
                    RoleBadgeForeground = member.Role == "owner"
                        ? Brushes.White
                        : new SolidColorBrush(Color.FromRgb(73, 80, 96))
                });
            }

            cards = cards
                .OrderByDescending(card => card.IsCurrentUser)
                .ThenByDescending(card => card.IsOnline)
                .ThenBy(card => card.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            MembersCountText.Text = cards.Count.ToString();
            return cards;
        }

        private static string GetInitials(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return "?";
            }

            var parts = displayName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(p => char.ToUpperInvariant(p[0]));

            return string.Concat(parts);
        }

        private static (Brush Fill, Brush Stroke) GetParticipantPalette(int index)
        {
            var palettes = new[]
            {
                (Fill: "#EEF4FF", Stroke: "#3B82F6"),
                (Fill: "#ECFDF3", Stroke: "#10B981"),
                (Fill: "#FFF7ED", Stroke: "#F59E0B"),
                (Fill: "#F5F3FF", Stroke: "#8B5CF6")
            };

            var palette = palettes[index % palettes.Length];
            return (new BrushConverter().ConvertFromString(palette.Fill) as Brush ?? Brushes.LightGray,
                new BrushConverter().ConvertFromString(palette.Stroke) as Brush ?? Brushes.DimGray);
        }

        private Brush GetBrushFromColor(string colorString)
        {
            if (string.IsNullOrEmpty(colorString))
                return Brushes.Black;
            try
            {
                return (Brush)new BrushConverter().ConvertFromString(colorString);
            }
            catch
            {
                return Brushes.Black;
            }
        }

        private void DisableEditingTools()
        {
            PenButton.IsEnabled = false;
            RectButton.IsEnabled = false;
            EllipseButton.IsEnabled = false;
            TextButton.IsEnabled = false;
            UndoButton.IsEnabled = false;
            RedoButton.IsEnabled = false;
            ClearBoardButton.IsEnabled = false;
            AddImageButton.IsEnabled = false;
            ColorPanel.IsEnabled = false;
        }

        private void EnableEditingTools()
        {
            PenButton.IsEnabled = true;
            RectButton.IsEnabled = true;
            EllipseButton.IsEnabled = true;
            TextButton.IsEnabled = true;
            UndoButton.IsEnabled = true;
            RedoButton.IsEnabled = true;
            ClearBoardButton.IsEnabled = true;
            AddImageButton.IsEnabled = true;
            ColorPanel.IsEnabled = true;
        }

        private void SetViewerMode(bool isViewer)
        {
            ViewerModeText.Visibility = isViewer ? Visibility.Visible : Visibility.Collapsed;
            if (isViewer)
            {
                DisableEditingTools();
            }
            else
            {
                EnableEditingTools();
            }
        }

        private async Task RefreshCurrentUserPermissionsAsync()
        {
            if (_removalHandled || _isAccessCheckRunning)
            {
                return;
            }

            if (_isAdminSession)
            {
                SetViewerMode(false);
                return;
            }

            _isAccessCheckRunning = true;
            try
            {
                var currentUser = await _supabaseService.GetMyProfileAsync();
                if (currentUser == null)
                {
                    return;
                }

                var isOwner = _boardInfo?.OwnerId == currentUser.Id;
                if (isOwner)
                {
                    SetViewerMode(false);
                    return;
                }

                var role = await _supabaseService.GetUserRoleForBoardAsync(_boardId);
                if (string.IsNullOrWhiteSpace(role))
                {
                    _removalHandled = true;
                    _accessMonitorTimer.Stop();
                    AppDialogService.ShowWarning("Вы были удалены с этой доски.", "Доступ к доске");
                    NavigateBackFromBoard();
                    return;
                }

                SetViewerMode(string.Equals(role, "viewer", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _isAccessCheckRunning = false;
            }
        }

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            string accessCode = string.IsNullOrWhiteSpace(_boardInfo?.AccessCode)
                ? "Код доски пока недоступен."
                : $"Код доступа к доске: {_boardInfo.AccessCode}";

            AppDialogService.ShowInfo(accessCode, "Поделиться доской");
        }

        private void BackToMenu_Click(object sender, RoutedEventArgs e)
        {
            NavigateBackFromBoard();
        }

        private void SaveBoard_Click(object sender, RoutedEventArgs e)
        {
            MarkSaved();
            AppDialogService.ShowSuccess("Все изменения уже сохраняются автоматически.", "Сохранение");
        }

        private void BoardMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (BoardMenuButton.ContextMenu != null)
            {
                BoardMenuButton.ContextMenu.PlacementTarget = BoardMenuButton;
                BoardMenuButton.ContextMenu.IsOpen = true;
            }
        }

        private void PresentationMode_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is not Window window)
            {
                return;
            }

            _isPresentationMode = !_isPresentationMode;

            if (_isPresentationMode)
            {
                _presentationRestoreWindowState = window.WindowState;
                _presentationRestoreResizeMode = window.ResizeMode;
                if (window.WindowState == WindowState.Maximized)
                {
                    var rb = window.RestoreBounds;
                    _presentationRestoreBounds = new Rect(rb.Left, rb.Top, rb.Width, rb.Height);
                }
                else
                {
                    _presentationRestoreBounds = new Rect(window.Left, window.Top, window.Width, window.Height);
                }

                _presentationStoredState = true;

                window.Topmost = true;
                window.WindowStyle = WindowStyle.None;
                window.ResizeMode = ResizeMode.NoResize;
                window.WindowState = WindowState.Normal;

                if (!TryPlaceWindowOnContainingMonitorFullscreen(window))
                {
                    window.Left = SystemParameters.WorkArea.Left;
                    window.Top = SystemParameters.WorkArea.Top;
                    window.Width = SystemParameters.WorkArea.Width;
                    window.Height = SystemParameters.WorkArea.Height;
                }

                AppDialogService.ShowInfo(
                    "Режим презентации: окно на весь текущий монитор. Повторите команду в меню, чтобы выйти.",
                    "Презентация");
            }
            else
            {
                window.Topmost = false;
                window.WindowStyle = WindowStyle.SingleBorderWindow;
                window.ResizeMode = _presentationStoredState
                    ? _presentationRestoreResizeMode
                    : ResizeMode.CanResize;

                if (_presentationStoredState)
                {
                    window.WindowState = WindowState.Normal;
                    window.Left = _presentationRestoreBounds.Left;
                    window.Top = _presentationRestoreBounds.Top;
                    window.Width = _presentationRestoreBounds.Width;
                    window.Height = _presentationRestoreBounds.Height;
                    window.WindowState = _presentationRestoreWindowState;
                }

                _presentationStoredState = false;

                AppDialogService.ShowInfo("Режим презентации выключен.", "Презентация");
            }
        }

        private async void SaveVersion_Click(object sender, RoutedEventArgs e)
        {
            if (_shapesOnBoard.Count == 0)
            {
                AppDialogService.ShowInfo("На доске пока нет объектов для сохранения версии.", "Версия доски");
                return;
            }

            var snapshotDirectory = GetSnapshotDirectory();
            Directory.CreateDirectory(snapshotDirectory);

            var payload = new BoardVersionSnapshot
            {
                SavedAtUtc = DateTime.UtcNow,
                Shapes = CloneShapes(_shapesOnBoard)
            };

            var fileName = $"version-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
            var filePath = IOPath.Combine(snapshotDirectory, fileName);

            await File.WriteAllTextAsync(filePath, JsonConvert.SerializeObject(payload, Formatting.Indented));
            MarkSaved();
            AppDialogService.ShowSuccess("Текущая версия отмечена как сохранённая.", "Версия доски");
        }

        private async void VersionHistory_Click(object sender, RoutedEventArgs e)
        {
            var snapshotDirectory = GetSnapshotDirectory();
            if (!Directory.Exists(snapshotDirectory))
            {
                AppDialogService.ShowInfo("Сохраненных версий пока нет.", "История версий");
                return;
            }

            var snapshots = Directory
                .GetFiles(snapshotDirectory, "version-*.json")
                .OrderByDescending(path => path)
                .Take(10)
                .ToList();

            if (snapshots.Count == 0)
            {
                AppDialogService.ShowInfo("Сохраненных версий пока нет.", "История версий");
                return;
            }

            var versionsPreview = string.Join(
                Environment.NewLine,
                snapshots.Select((path, index) => $"{index + 1}. {IOPath.GetFileNameWithoutExtension(path)}"));

            var shouldRestore = AppDialogService.ShowConfirmation(
                $"Найдено версий: {snapshots.Count}\n\n{versionsPreview}\n\nВосстановить самую свежую версию?",
                "История версий",
                "Восстановить",
                "Закрыть");

            if (!shouldRestore)
            {
                return;
            }

            var latestSnapshot = await File.ReadAllTextAsync(snapshots[0]);
            var payload = JsonConvert.DeserializeObject<BoardVersionSnapshot>(latestSnapshot);
            if (payload?.Shapes == null)
            {
                AppDialogService.ShowError("Не удалось прочитать сохраненную версию.", "История версий");
                return;
            }

            _undoHistory.Push(CloneShapes(_shapesOnBoard));
            await RestoreBoardStateAsync(payload.Shapes);
            AppDialogService.ShowSuccess("Последняя сохраненная версия восстановлена.", "История версий");
        }

        private async void DeleteBoardMenu_Click(object sender, RoutedEventArgs e)
        {
            bool success = await _supabaseService.DeleteBoardAsync(_boardId);
            if (success)
            {
                NavigateBackFromBoard();
            }
        }

        private void NavigateBackFromBoard()
        {
            NavigationService?.Navigate(_returnToAdminPage ? new AdminPage() : new UserHomePage());
        }

        private void Invite_Click(object sender, RoutedEventArgs e)
        {
            Share_Click(sender, e);
        }

        private void ExportPng_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Title = "Экспорт доски в PNG",
                    Filter = "PNG image|*.png",
                    FileName = $"{(_boardInfo?.Title ?? "board")}-{DateTime.Now:yyyyMMdd-HHmm}.png"
                };

                if (saveFileDialog.ShowDialog() != true)
                {
                    return;
                }

                var bounds = new Rect(Viewport.RenderSize);
                var renderBitmap = new RenderTargetBitmap(
                    Math.Max(1, (int)bounds.Width),
                    Math.Max(1, (int)bounds.Height),
                    96,
                    96,
                    PixelFormats.Pbgra32);
                renderBitmap.Render(Viewport);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                using var stream = File.Create(saveFileDialog.FileName);
                encoder.Save(stream);

                AppDialogService.ShowSuccess("Доска экспортирована в PNG.", "Экспорт PNG");
            }
            catch (Exception ex)
            {
                AppDialogService.ShowError($"Не удалось экспортировать PNG: {ex.Message}", "Экспорт PNG");
            }
        }

        private void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var printDialog = new PrintDialog();
                var result = printDialog.ShowDialog();
                if (result != true)
                {
                    return;
                }

                printDialog.PrintVisual(Viewport, $"WhiteSpace board {_boardInfo?.Title}");
                AppDialogService.ShowSuccess("Отправлено в печать. Для PDF выберите принтер 'Microsoft Print to PDF'.", "Экспорт PDF");
            }
            catch (Exception ex)
            {
                AppDialogService.ShowError($"Не удалось отправить на печать: {ex.Message}", "Экспорт PDF");
            }
        }

        private async void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoHistory.Count == 0)
            {
                return;
            }

            _redoHistory.Push(CloneShapes(_shapesOnBoard));
            await RestoreBoardStateAsync(_undoHistory.Pop());
        }

        private async void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (_redoHistory.Count == 0)
            {
                return;
            }

            _undoHistory.Push(CloneShapes(_shapesOnBoard));
            await RestoreBoardStateAsync(_redoHistory.Pop());
        }

        private async void ClearBoard_Click(object sender, RoutedEventArgs e)
        {
            if (!AppDialogService.ShowConfirmation("Очистить всю доску? Это удалит все фигуры и изображения.", "Очистить доску"))
            {
                return;
            }

            if (_shapesOnBoard.Count == 0)
            {
                return;
            }

            CaptureBoardStateForUndo();
            _shapesOnBoard.Clear();
            RenderCurrentBoardState();

            if (await _supabaseService.ClearBoardShapesAsync(_boardId))
            {
                MarkSaved();
            }
        }

        private async void AddImage_Click(object sender, RoutedEventArgs e)
        {
            var userRole = await _supabaseService.GetUserRoleForBoardAsync(_boardId);
            if (userRole == "viewer")
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Выберите изображение",
                Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            await AddImageToBoardAsync(dialog.FileName);
        }

        private void ToggleChat_Click(object sender, RoutedEventArgs e)
        {
            var prefs = AppPreferences.Load();
            var show = ChatWidget.Visibility != Visibility.Visible;
            UiAnimationHelper.ApplyFadeVisibilityToggle(ChatWidget, show, prefs.EnableAnimations);
        }

        private void CloseChat_Click(object sender, RoutedEventArgs e)
        {
            var prefs = AppPreferences.Load();
            UiAnimationHelper.ApplyFadeVisibilityToggle(ChatWidget, false, prefs.EnableAnimations);
        }

        private void ChatSend_Click(object sender, RoutedEventArgs e)
        {
            var prefs = AppPreferences.Load();
            if (ChatWidget.Visibility != Visibility.Visible)
            {
                UiAnimationHelper.ApplyFadeVisibilityToggle(ChatWidget, true, prefs.EnableAnimations);
            }

            _ = SendChatMessageAsync();
        }

        private async Task SendChatMessageAsync()
        {
            var text = ChatInputBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                ChatInputBox.Focus();
                return;
            }

            if (_myUserId == null)
            {
                await InitCursorIdentityAsync();
            }

            if (_myUserId == null)
            {
                return;
            }

            var message = new FirebaseChatMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = _myUserId.ToString()!,
                UserName = _cursorDisplayName,
                Text = text,
                SentAtUtc = DateTime.UtcNow
            };

            await _firebaseService.PushChatMessageAsync(_boardId.ToString(), message);
            ChatInputBox.Text = string.Empty;
            ChatInputBox.Focus();
        }

        private async void ChatInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            await SendChatMessageAsync();
        }

        private void SubscribeToChatMessages()
        {
            try
            {
                _chatSubscription = _firebaseService
                    .GetBoardChatMessagesObservable(_boardId.ToString())
                    .Where(messages => messages != null)
                    .Subscribe(messages =>
                    {
                        Application.Current.Dispatcher.Invoke(() => UpdateChatMessages(messages));
                    });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка подписки на чат: {ex.Message}");
            }
        }

        private void UpdateChatMessages(List<FirebaseChatMessage> messages)
        {
            var myUserIdString = _myUserId?.ToString();

            var normalized = messages
                .Where(message => !string.IsNullOrWhiteSpace(message.Text))
                .OrderBy(message => message.SentAtUtc)
                .TakeLast(150)
                .Select(message =>
                {
                    var isMine = !string.IsNullOrWhiteSpace(myUserIdString)
                        && string.Equals(message.UserId, myUserIdString, StringComparison.OrdinalIgnoreCase);

                    var senderName = string.IsNullOrWhiteSpace(message.UserName) ? "Участник" : message.UserName.Trim();
                    var time = message.SentAtUtc.ToLocalTime().ToString("HH:mm");

                    var vm = new ChatMessageViewModel
                    {
                        MessageId = string.IsNullOrWhiteSpace(message.Id) ? string.Empty : message.Id.Trim(),
                        IsMine = isMine,
                        UserId = message.UserId ?? string.Empty,
                        UserName = message.UserName ?? string.Empty,
                        SentAtUtc = message.SentAtUtc,
                        EditedAtUtc = message.EditedAtUtc,
                        HeaderText = isMine ? $"Вы • {time}" : $"{senderName} • {time}",
                        Text = message.Text,
                        HeaderAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                        BubbleAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                        BubbleBackground = isMine
                            ? FindThemeBrush("WsChatMineBubbleBrush", new SolidColorBrush(Color.FromRgb(139, 92, 246)))
                            : FindThemeBrush("WsChatPeerBubbleBrush", new SolidColorBrush(Color.FromRgb(241, 245, 249))),
                        TextForeground = isMine
                            ? Brushes.White
                            : FindThemeBrush("WsChatPeerTextBrush", new SolidColorBrush(Color.FromRgb(30, 41, 59)))
                    };

                    return vm;
                })
                .ToList();

            _chatMessages.Clear();
            foreach (var item in normalized)
            {
                _chatMessages.Add(item);
            }

            Dispatcher.BeginInvoke(() =>
            {
                var sv = ChatMessagesScrollViewer;
                if (sv != null)
                {
                    sv.ScrollToVerticalOffset(sv.ExtentHeight);
                }
            }, DispatcherPriority.Background);
        }

        private void ChatBubble_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not ChatMessageViewModel vm)
            {
                return;
            }

            if (!vm.IsMine)
            {
                e.Handled = true;
            }
        }

        private async void ChatMessageEdit_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetChatMessageViewModelFromMenu(sender);
            if (vm == null || !vm.IsMine || string.IsNullOrWhiteSpace(vm.MessageId))
            {
                return;
            }

            var edited = AppDialogService.ShowTextInput(
                "Редактировать сообщение",
                "Текст сообщения:",
                "Сохранить",
                "Отмена",
                vm.Text);

            if (edited == null)
            {
                return;
            }

            var trimmed = edited.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                AppDialogService.ShowWarning("Сообщение не может быть пустым.", "Чат");
                return;
            }

            await _firebaseService.UpdateChatMessageAsync(
                _boardId.ToString(),
                new FirebaseChatMessage
                {
                    Id = vm.MessageId,
                    UserId = vm.UserId,
                    UserName = string.IsNullOrWhiteSpace(vm.UserName) ? _cursorDisplayName : vm.UserName,
                    Text = trimmed,
                    SentAtUtc = vm.SentAtUtc,
                    EditedAtUtc = DateTime.UtcNow
                });
        }

        private async void ChatMessageDelete_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetChatMessageViewModelFromMenu(sender);
            if (vm == null || !vm.IsMine || string.IsNullOrWhiteSpace(vm.MessageId))
            {
                return;
            }

            if (!AppDialogService.ShowConfirmation(
                    "Удалить это сообщение?",
                    "Чат",
                    "Удалить",
                    "Отмена"))
            {
                return;
            }

            await _firebaseService.DeleteChatMessageAsync(_boardId.ToString(), vm.MessageId);
        }

        private static ChatMessageViewModel? GetChatMessageViewModelFromMenu(object sender)
        {
            if (sender is not MenuItem menuItem)
            {
                return null;
            }

            var menu = menuItem.Parent as ContextMenu;
            var target = menu?.PlacementTarget as FrameworkElement;
            return target?.DataContext as ChatMessageViewModel;
        }

        private void AddShapeToCanvas(BoardShape shape, bool addToBoardState = true)
        {
            Brush brush = GetBrushFromColor(shape.Color);

            if (shape.Type == "line")
            {
                var polyline = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Uid = shape.Id.ToString()
                };

                var points = JsonConvert.DeserializeObject<List<Point>>(shape.Points);
                foreach (var point in points)
                {
                    polyline.Points.Add(new Point(point.X, point.Y));
                }

                BoardCanvas.Children.Add(polyline);
                if (addToBoardState)
                {
                    _shapesOnBoard.Add(shape);
                }
            }
            else if (shape.Type == "text")
            {
                var textBox = new TextBox
                {
                    Text = shape.Text,
                    MinWidth = 120,
                    FontSize = 16,
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    Foreground = brush,
                    Uid = shape.Id.ToString(),
                    IsReadOnly = false,
                    Focusable = true
                };

                textBox.PreviewMouseDown += TextBox_PreviewMouseDown;
                textBox.PreviewMouseUp += TextBox_PreviewMouseUp;
                textBox.LostFocus += TextBox_LostFocus;
                textBox.TextChanged += TextBox_TextChanged;

                Canvas.SetLeft(textBox, shape.X);
                Canvas.SetTop(textBox, shape.Y);

                BoardCanvas.Children.Add(textBox);
                if (addToBoardState)
                {
                    _shapesOnBoard.Add(shape);
                }
            }
            else if (shape.Type == "image")
            {
                var image = new Image
                {
                    Width = shape.Width > 0 ? shape.Width : DefaultImageW,
                    Height = shape.Height > 0 ? shape.Height : DefaultImageH,
                    Stretch = Stretch.UniformToFill,
                    Uid = shape.Id.ToString(),
                    Source = CreateImageSource(shape.Text)
                };

                Canvas.SetLeft(image, shape.X - image.Width / 2);
                Canvas.SetTop(image, shape.Y - image.Height / 2);

                BoardCanvas.Children.Add(image);
                if (addToBoardState)
                {
                    _shapesOnBoard.Add(shape);
                }
            }
            else
            {
                UIElement element = shape.Type switch
                {
                    "rectangle" => new Rectangle
                    {
                        Width = shape.Width,
                        Height = shape.Height,
                        Stroke = brush,
                        StrokeThickness = 2,
                        Fill = Brushes.Transparent,
                        Uid = shape.Id.ToString()
                    },
                    "ellipse" => new Ellipse
                    {
                        Width = shape.Width,
                        Height = shape.Height,
                        Stroke = brush,
                        StrokeThickness = 2,
                        Fill = Brushes.Transparent,
                        Uid = shape.Id.ToString()
                    },
                    _ => null
                };

                if (element != null)
                {
                    Canvas.SetLeft(element, shape.X - shape.Width / 2);
                    Canvas.SetTop(element, shape.Y - shape.Height / 2);

                    BoardCanvas.Children.Add(element);
                    if (addToBoardState)
                    {
                        _shapesOnBoard.Add(shape);
                    }
                }
            }
        }

        private ImageSource? CreateImageSource(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        // Обработчики событий для TextBox
        private void TextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            if (_tool == ToolMode.Hand && e.LeftButton == MouseButtonState.Pressed)
            {
                var world = ScreenToWorld(e.GetPosition(Viewport));
                _dragStartWorld = world;
                CaptureBoardStateForUndo();

                _wasTextEditingEnabled = !textBox.IsReadOnly;

                textBox.IsReadOnly = true;
                textBox.Cursor = Cursors.SizeAll;

                _isDraggingElement = true;
                _dragElement = textBox;

                double left = Canvas.GetLeft(textBox);
                double top = Canvas.GetTop(textBox);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                _dragOffsetWorld = new Point(world.X - left, world.Y - top);

                Viewport.CaptureMouse();
                e.Handled = true;
            }
        }

        private void TextBox_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            if (_isDraggingElement && _dragElement == textBox)
            {
                _isDraggingElement = false;
                _dragElement = null;

                textBox.IsReadOnly = false;
                textBox.Cursor = Cursors.IBeam;

                Viewport.ReleaseMouseCapture();

                SaveTextBoxPosition(textBox);
                e.Handled = true;
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox != null)
            {
                SaveTextBoxText(textBox);
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Можно добавить автосохранение при необходимости
        }

        // Инструменты
        private void Hand_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Hand);
        private void Pen_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Pen);
        private void Rect_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Rect);
        private void Ellipse_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Ellipse);
        private void Text_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Text);

        private void SetTool(ToolMode tool)
        {
            _tool = tool;
            ColorPanel.Visibility = Visibility.Visible;

            _isDrawing = false;
            _currentStroke = null;
            _isPanning = false;
            _isDraggingElement = false;
            _dragElement = null;

            RemoveResizeFrame();
            RemovePreviewShape();

            if (_tool == ToolMode.Rect || _tool == ToolMode.Ellipse)
            {
                EnsurePreviewShape();
            }

            Viewport.Cursor = _tool switch
            {
                ToolMode.Hand => Cursors.Hand,
                ToolMode.Pen => Cursors.Pen,
                ToolMode.Text => Cursors.IBeam,
                _ => Cursors.Cross
            };

            foreach (var child in BoardCanvas.Children)
            {
                if (child is TextBox textBox)
                {
                    if (_tool == ToolMode.Hand)
                    {
                        textBox.IsReadOnly = true;
                        textBox.Cursor = Cursors.Hand;
                        textBox.Background = Brushes.Transparent;
                    }
                    else if (_tool == ToolMode.Text)
                    {
                        textBox.IsReadOnly = false;
                        textBox.Cursor = Cursors.IBeam;
                        textBox.Background = Brushes.White;
                    }
                    else
                    {
                        textBox.IsReadOnly = true;
                        textBox.Cursor = Cursors.Arrow;
                        textBox.Background = Brushes.Transparent;
                    }
                }
            }
        }

        private Point ScreenToWorld(Point screenPoint)
        {
            var s = BoardScale.ScaleX;
            return new Point(
                (screenPoint.X - BoardTranslate.X) / s,
                (screenPoint.Y - BoardTranslate.Y) / s
            );
        }

        private async void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var userRole = await _supabaseService.GetUserRoleForBoardAsync(_boardId);
            if (userRole == "viewer")
            {
                if (_tool == ToolMode.Hand && e.LeftButton == MouseButtonState.Pressed)
                {
                    StartPan(e.GetPosition(Viewport));
                }
                return;
            }

            var hitTestResult = VisualTreeHelper.HitTest(Viewport, e.GetPosition(Viewport));
            if (hitTestResult != null)
            {
                var hitElement = hitTestResult.VisualHit;
                while (hitElement != null && !(hitElement is Rectangle))
                    hitElement = VisualTreeHelper.GetParent(hitElement);

                if (hitElement is Rectangle rect && rect.Tag is string tag && tag.Length <= 2)
                {
                    return;
                }
            }

            Viewport.Focus();
            var screen = e.GetPosition(Viewport);
            var world = ScreenToWorld(screen);

            if (_isDraggingElement)
                return;

            if (_tool == ToolMode.Hand)
            {
                RemoveResizeFrame();

                var hitTestResult2 = VisualTreeHelper.HitTest(Viewport, screen);
                if (hitTestResult2 != null)
                {
                    var hitElement = hitTestResult2.VisualHit;

                    while (hitElement != null && !(hitElement is UIElement))
                        hitElement = VisualTreeHelper.GetParent(hitElement);

                    var uiElement = hitElement as UIElement;

                    if (uiElement != null && uiElement != BoardCanvas && uiElement != Viewport &&
                        !(uiElement is TextBox) && uiElement != _previewShape)
                    {
                        if (uiElement is Polyline || uiElement is Rectangle || uiElement is Ellipse || uiElement is Image)
                        {
                            CaptureBoardStateForUndo();
                            _isDraggingElement = true;
                            _dragElement = uiElement;

                            double left = Canvas.GetLeft(uiElement);
                            double top = Canvas.GetTop(uiElement);
                            if (double.IsNaN(left)) left = 0;
                            if (double.IsNaN(top)) top = 0;

                            _dragOffsetWorld = new Point(world.X - left, world.Y - top);
                            Viewport.CaptureMouse();

                            ShowResizeFrame(uiElement);
                            return;
                        }
                    }
                }

                StartPan(screen);
                return;
            }

            if (_tool == ToolMode.Pen && e.LeftButton == MouseButtonState.Pressed)
            {
                StartStroke(world);
                return;
            }

            if ((_tool == ToolMode.Rect || _tool == ToolMode.Ellipse) && e.LeftButton == MouseButtonState.Pressed)
            {
                PlaceShapeAt(world);
                return;
            }

            if (_tool == ToolMode.Text && e.LeftButton == MouseButtonState.Pressed)
            {
                var hitTestResult3 = VisualTreeHelper.HitTest(Viewport, screen);
                if (hitTestResult3 != null && hitTestResult3.VisualHit is TextBox)
                {
                    var textBox = FindParentTextBox(hitTestResult3.VisualHit);
                    if (textBox != null)
                    {
                        textBox.Focus();
                        textBox.SelectAll();
                    }
                }
                else
                {
                    PlaceTextAt(world);
                }
                return;
            }
        }

        private void BoardCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_tool != ToolMode.Hand)
            {
                e.Handled = true;
                return;
            }

            var screen = e.GetPosition(BoardCanvas);
            var world = ScreenToWorld(screen);

            var hitTestResult = VisualTreeHelper.HitTest(BoardCanvas, screen);
            if (hitTestResult != null)
            {
                var hitElement = hitTestResult.VisualHit;

                while (hitElement != null && !(hitElement is UIElement))
                    hitElement = VisualTreeHelper.GetParent(hitElement);

                var uiElement = hitElement as UIElement;

                if (uiElement != null && uiElement != BoardCanvas)
                {
                    if (_tool == ToolMode.Hand)
                    {
                        if (uiElement is Shape || uiElement is TextBox || uiElement is Polyline || uiElement is Image)
                        {
                            ShowResizeFrame(uiElement);
                            e.Handled = true;
                        }
                    }
                }
            }
        }

        private TextBox FindParentTextBox(DependencyObject child)
        {
            while (child != null && !(child is TextBox))
                child = VisualTreeHelper.GetParent(child);

            return child as TextBox;
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            var screen = e.GetPosition(Viewport);
            var world = ScreenToWorld(screen);

            var now = DateTime.UtcNow;
            if ((now - _lastCursorPublishUtc).TotalMilliseconds >= CursorPublishThrottleMs)
            {
                _lastCursorPublishUtc = now;
                _ = PublishCursorAsync(world, true);
            }

            if (_isResizing && _resizeTarget != null)
            {
                ResizeElement(world);
                return;
            }

            UpdatePreview(world);

            if (_isDrawing && _currentStroke != null && e.LeftButton == MouseButtonState.Pressed)
            {
                _currentStroke.Points.Add(world);
                var shape = _shapesOnBoard.Find(s => s.Id.ToString() == _currentStroke.Uid);
                if (shape != null)
                {
                    shape.DeserializedPoints.Add(world);
                }
            }

            if (_isDraggingElement && _dragElement != null)
            {
                MoveElementTo(world);
            }

            if (_isPanning)
            {
                BoardTranslate.X = _panStartX + (screen.X - _panStartScreen.X);
                BoardTranslate.Y = _panStartY + (screen.Y - _panStartScreen.Y);
            }
        }

        private async void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isResizing)
            {
                await SaveResizedShapeAsync(); // Изменено на асинхронный метод
                _isResizing = false;
                _resizeDirection = null;
                Viewport.ReleaseMouseCapture();
                return;
            }

            if (_isDraggingElement && !(_dragElement is TextBox))
            {
                if (_dragElement != null)
                {
                    await SaveElementPositionAsync(_dragElement); // Изменено на асинхронный метод
                }
                _isDraggingElement = false;
                _dragElement = null;
            }

            if (_isDrawing)
            {
                if (_currentStroke != null)
                {
                    var shape = _shapesOnBoard.Find(s => s.Id.ToString() == _currentStroke.Uid);
                    if (shape != null)
                    {
                        shape.Points = JsonConvert.SerializeObject(_currentStroke.Points);
                        await _supabaseService.SaveShapeAsync(shape);
                        MarkSaved();

                        // Отправляем в Firebase для реалтайм обновлений
                        PushShapeToFirebase(shape); // Этот метод может остаться void, так как не требует await
                    }
                }
                _isDrawing = false;
                _currentStroke = null;
            }

            _isPanning = false;
            Viewport.ReleaseMouseCapture();
        }

        private void MoveElementTo(Point world)
        {
            if (_dragElement == null) return;

            double offsetX = world.X - _dragOffsetWorld.X;
            double offsetY = world.Y - _dragOffsetWorld.Y;

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _dragElement.Uid);
            if (shape != null)
            {
                if (_dragElement is Shape visualShape)
                {
                    shape.X = offsetX + shape.Width / 2;
                    shape.Y = offsetY + shape.Height / 2;
                }
                else if (_dragElement is TextBox textBox)
                {
                    shape.X = offsetX;
                    shape.Y = offsetY;
                }
                else if (_dragElement is Polyline polyline)
                {
                    shape.X = offsetX;
                    shape.Y = offsetY;

                    var newPoints = new List<Point>();
                    foreach (var point in polyline.Points)
                    {
                        newPoints.Add(new Point(point.X + offsetX, point.Y + offsetY));
                    }
                    shape.DeserializedPoints = newPoints;
                    shape.Points = JsonConvert.SerializeObject(newPoints);
                }
                else if (_dragElement is Image image)
                {
                    shape.X = offsetX + image.ActualWidth / 2;
                    shape.Y = offsetY + image.ActualHeight / 2;
                }
            }

            Canvas.SetLeft(_dragElement, offsetX);
            Canvas.SetTop(_dragElement, offsetY);

            if (_resizeTarget == _dragElement && _resizeBorder != null)
            {
                Canvas.SetLeft(_resizeBorder, offsetX);
                Canvas.SetTop(_resizeBorder, offsetY);

                double width, height;

                if (_dragElement is Polyline polyline)
                {
                    if (polyline.Points.Count > 0)
                    {
                        double minX = polyline.Points.Min(p => p.X);
                        double maxX = polyline.Points.Max(p => p.X);
                        double minY = polyline.Points.Min(p => p.Y);
                        double maxY = polyline.Points.Max(p => p.Y);

                        width = maxX - minX;
                        height = maxY - minY;
                    }
                    else
                    {
                        width = 0;
                        height = 0;
                    }
                }
                else
                {
                    width = ((FrameworkElement)_dragElement).ActualWidth;
                    height = ((FrameworkElement)_dragElement).ActualHeight;
                }

                UpdateResizeHandles(offsetX, offsetY, width, height);
            }
        }

        private async Task SaveElementPositionAsync(UIElement element)
        {
            var shape = _shapesOnBoard.Find(s => s.Id.ToString() == element.Uid);
            if (shape != null)
            {
                if (element is TextBox textBox)
                {
                    shape.X = Canvas.GetLeft(textBox);
                    shape.Y = Canvas.GetTop(textBox);
                }
                else if (element is Polyline polyline)
                {
                    shape.X = Canvas.GetLeft(element);
                    shape.Y = Canvas.GetTop(element);

                    // Обновляем точки линии
                    var newPoints = new List<Point>();
                    foreach (var point in polyline.Points)
                    {
                        newPoints.Add(new Point(point.X, point.Y));
                    }
                    shape.DeserializedPoints = newPoints;
                    shape.Points = JsonConvert.SerializeObject(newPoints);
                }
                else if (element is Shape visualShape)
                {
                    shape.X = Canvas.GetLeft(element) + shape.Width / 2;
                    shape.Y = Canvas.GetTop(element) + shape.Height / 2;
                }
                else if (element is Image image)
                {
                    shape.X = Canvas.GetLeft(element) + image.ActualWidth / 2;
                    shape.Y = Canvas.GetTop(element) + image.ActualHeight / 2;
                }

                // Сохраняем в Supabase
                await _supabaseService.SaveShapeAsync(shape);
                MarkSaved();

                // Отправляем в Firebase для реалтайм обновлений
                PushShapeToFirebase(shape);
            }
        }

        private async void SaveTextBoxPosition(TextBox textBox)
        {
            var shape = _shapesOnBoard.Find(s => s.Id.ToString() == textBox.Uid);
            if (shape != null)
            {
                shape.X = Canvas.GetLeft(textBox);
                shape.Y = Canvas.GetTop(textBox);
                await _supabaseService.SaveShapeAsync(shape);
                MarkSaved();

                // Отправляем в Firebase для реалтайм обновлений
                PushShapeToFirebase(shape);
            }
        }

        private async void SaveTextBoxText(TextBox textBox)
        {
            var shape = _shapesOnBoard.Find(s => s.Id.ToString() == textBox.Uid);
            if (shape != null)
            {
                if (shape.Text != textBox.Text)
                {
                    CaptureBoardStateForUndo();
                }

                shape.Text = textBox.Text;
                await _supabaseService.SaveShapeAsync(shape);
                MarkSaved();

                // Отправляем в Firebase для реалтайм обновлений
                PushShapeToFirebase(shape);
            }
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var screen = e.GetPosition(Viewport);
            var before = ScreenToWorld(screen);

            double scale = BoardScale.ScaleX;
            double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;

            scale *= factor;
            if (scale < 0.1) scale = 0.1;
            if (scale > 6) scale = 6;

            BoardScale.ScaleX = scale;
            BoardScale.ScaleY = scale;

            var after = ScreenToWorld(screen);

            BoardTranslate.X += (after.X - before.X) * scale;
            BoardTranslate.Y += (after.Y - before.Y) * scale;
        }

        private void StartPan(Point screen)
        {
            _isPanning = true;
            _panStartScreen = screen;
            _panStartX = BoardTranslate.X;
            _panStartY = BoardTranslate.Y;
            Viewport.CaptureMouse();
        }

        private async void StartStroke(Point startWorld)
        {
            CaptureBoardStateForUndo();
            _isDrawing = true;

            _currentStroke = new Polyline
            {
                Stroke = _currentBrush,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            _currentStroke.Points.Add(startWorld);
            BoardCanvas.Children.Add(_currentStroke);

            int uniqueId = await _supabaseService.GenerateUniqueIdAsync(_boardId);
            _currentStroke.Uid = uniqueId.ToString();

            var shape = new BoardShape
            {
                BoardId = _boardId,
                Type = "line",
                X = startWorld.X,
                Y = startWorld.Y,
                Color = _currentColorString,
                Id = uniqueId
            };

            shape.DeserializedPoints.Add(startWorld);

            _shapesOnBoard.Add(shape);
            Viewport.CaptureMouse();
        }

        private void EnsurePreviewShape()
        {
            if (_previewShape != null) return;

            _previewShape = _tool switch
            {
                ToolMode.Ellipse => new Ellipse(),
                ToolMode.Rect => new Rectangle(),
                _ => null
            };

            if (_previewShape == null) return;

            _previewShape.Stroke = Brushes.Black;
            _previewShape.StrokeThickness = 2;
            _previewShape.Opacity = 0.35;
            _previewShape.StrokeDashArray = new DoubleCollection { 4, 3 };
            _previewShape.Fill = Brushes.Transparent;
            _previewShape.IsHitTestVisible = false;

            BoardCanvas.Children.Add(_previewShape);
        }

        private void RemovePreviewShape()
        {
            if (_previewShape == null) return;
            BoardCanvas.Children.Remove(_previewShape);
            _previewShape = null;
        }

        private void UpdatePreview(Point world)
        {
            if (_previewShape == null) return;

            if (_tool == ToolMode.Rect)
            {
                _previewShape.Width = DefaultRectW;
                _previewShape.Height = DefaultRectH;
                Canvas.SetLeft(_previewShape, world.X - DefaultRectW / 2);
                Canvas.SetTop(_previewShape, world.Y - DefaultRectH / 2);
            }
            else if (_tool == ToolMode.Ellipse)
            {
                _previewShape.Width = DefaultEllipse;
                _previewShape.Height = DefaultEllipse;
                Canvas.SetLeft(_previewShape, world.X - DefaultEllipse / 2);
                Canvas.SetTop(_previewShape, world.Y - DefaultEllipse / 2);
            }
        }

        private async void PlaceShapeAt(Point world)
        {
            if (_isCreatingShape) return;

            _isCreatingShape = true;
            try
            {
                CaptureBoardStateForUndo();
                int uniqueId = await _supabaseService.GenerateUniqueIdAsync(_boardId);

                BoardShape shape = _tool switch
                {
                    ToolMode.Rect => new BoardShape
                    {
                        BoardId = _boardId,
                        Type = "rectangle",
                        X = world.X,
                        Y = world.Y,
                        Width = DefaultRectW,
                        Height = DefaultRectH,
                        Color = _currentColorString,
                        Text = "",
                        Id = uniqueId
                    },
                    ToolMode.Ellipse => new BoardShape
                    {
                        BoardId = _boardId,
                        Type = "ellipse",
                        X = world.X,
                        Y = world.Y,
                        Width = DefaultEllipse,
                        Height = DefaultEllipse,
                        Color = _currentColorString,
                        Text = "",
                        Id = uniqueId
                    },
                    _ => null
                };

                if (shape != null)
                {
                    _shapesOnBoard.Add(shape);

                    UIElement element = shape.Type switch
                    {
                        "rectangle" => new Rectangle
                        {
                            Width = shape.Width,
                            Height = shape.Height,
                            Stroke = _currentBrush,
                            StrokeThickness = 2,
                            Fill = Brushes.Transparent,
                            Uid = shape.Id.ToString()
                        },
                        "ellipse" => new Ellipse
                        {
                            Width = shape.Width,
                            Height = shape.Height,
                            Stroke = _currentBrush,
                            StrokeThickness = 2,
                            Fill = Brushes.Transparent,
                            Uid = shape.Id.ToString()
                        },
                        _ => null
                    };

                    if (element != null)
                    {
                        Canvas.SetLeft(element, shape.X - shape.Width / 2);
                        Canvas.SetTop(element, shape.Y - shape.Height / 2);

                        BoardCanvas.Children.Add(element);

                        // Сохраняем в Supabase
                        await _supabaseService.SaveShapeAsync(shape);
                        MarkSaved();

                        // Отправляем в Firebase для реалтайм обновлений
                        PushShapeToFirebase(shape);

        }
                }
            }
            finally
            {
                _isCreatingShape = false;
            }
        }

        private async void PlaceTextAt(Point world)
        {
            if (_isCreatingShape) return;

            _isCreatingShape = true;
            CaptureBoardStateForUndo();

            var tb = new TextBox
            {
                Text = "Текст",
                MinWidth = 120,
                FontSize = 16,
                Background = Brushes.White,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Foreground = _currentBrush,
                IsReadOnly = false,
                Focusable = true,
                Cursor = Cursors.IBeam
            };

            tb.PreviewMouseDown += TextBox_PreviewMouseDown;
            tb.PreviewMouseUp += TextBox_PreviewMouseUp;
            tb.LostFocus += TextBox_LostFocus;
            tb.TextChanged += TextBox_TextChanged;

            Canvas.SetLeft(tb, world.X);
            Canvas.SetTop(tb, world.Y);
            BoardCanvas.Children.Add(tb);

            var uniqueId = await _supabaseService.GenerateUniqueIdAsync(_boardId);
            tb.Uid = uniqueId.ToString();

            var shape = new BoardShape
            {
                BoardId = _boardId,
                Type = "text",
                X = world.X,
                Y = world.Y,
                Width = 120,
                Height = 30,
                Text = tb.Text,
                Color = _currentColorString,
                Id = uniqueId
            };

            _shapesOnBoard.Add(shape);
            await _supabaseService.SaveShapeAsync(shape);
            MarkSaved();

            // Отправляем в Firebase для реалтайм обновлений
            PushShapeToFirebase(shape);

            tb.Focus();
            tb.SelectAll();

            _isCreatingShape = false;
        }

        // РУЧКИ ИЗМЕНЕНИЯ РАЗМЕРА
        private void ShowResizeFrame(UIElement element)
        {
            RemoveResizeFrame();
            ColorPanel.Visibility = Visibility.Visible;

            _resizeTarget = element;

            double left, top, width, height;

            if (element is Polyline polyline)
            {
                if (polyline.Points.Count > 0)
                {
                    double minX = polyline.Points.Min(p => p.X);
                    double maxX = polyline.Points.Max(p => p.X);
                    double minY = polyline.Points.Min(p => p.Y);
                    double maxY = polyline.Points.Max(p => p.Y);

                    left = minX;
                    top = minY;
                    width = Math.Max(maxX - minX, 1);
                    height = Math.Max(maxY - minY, 1);
                }
                else
                {
                    left = Canvas.GetLeft(element);
                    top = Canvas.GetTop(element);
                    width = 1;
                    height = 1;
                }
            }
            else
            {
                left = Canvas.GetLeft(element);
                top = Canvas.GetTop(element);
                width = ((FrameworkElement)element).ActualWidth;
                height = ((FrameworkElement)element).ActualHeight;

                if (width < 1) width = 1;
                if (height < 1) height = 1;
            }

            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            _resizeBorder = new Rectangle
            {
                Width = width,
                Height = height,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                IsHitTestVisible = false
            };

            Canvas.SetLeft(_resizeBorder, left);
            Canvas.SetTop(_resizeBorder, top);

            BoardCanvas.Children.Add(_resizeBorder);

            CreateResizeHandles(left, top, width, height);
        }

        private void RemoveResizeFrame()
        {
            if (_resizeBorder != null)
            {
                BoardCanvas.Children.Remove(_resizeBorder);
                _resizeBorder = null;
            }

            RemoveAllHandles();
            _resizeTarget = null;
            ColorPanel.Visibility = Visibility.Visible;
        }

        private void RemoveAllHandles()
        {
            foreach (var handle in _resizeHandles.Values.ToList())
            {
                if (handle != null)
                {
                    handle.MouseDown -= ResizeHandle_MouseDown;
                    BoardCanvas.Children.Remove(handle);
                }
            }
            _resizeHandles.Clear();
        }

        private void CreateResizeHandles(double x, double y, double w, double h)
        {
            RemoveAllHandles();

            var handlePositions = new Dictionary<string, (double X, double Y)>
            {
                { "nw", (x, y) },
                { "n", (x + w / 2, y) },
                { "ne", (x + w, y) },
                { "e", (x + w, y + h / 2) },
                { "se", (x + w, y + h) },
                { "s", (x + w / 2, y + h) },
                { "sw", (x, y + h) },
                { "w", (x, y + h / 2) }
            };

            foreach (var kvp in handlePositions)
            {
                var handle = CreateHandle(kvp.Key, kvp.Value.X, kvp.Value.Y);
                _resizeHandles[kvp.Key] = handle;
                BoardCanvas.Children.Add(handle);
            }
        }

        private void UpdateResizeFrame(double x, double y, double w, double h)
        {
            if (_resizeBorder == null) return;

            _resizeBorder.Width = w;
            _resizeBorder.Height = h;
            Canvas.SetLeft(_resizeBorder, x);
            Canvas.SetTop(_resizeBorder, y);

            UpdateResizeHandles(x, y, w, h);
        }

        private void UpdateResizeHandles(double x, double y, double w, double h)
        {
            if (_resizeHandles.Count == 0) return;

            var handlePositions = new Dictionary<string, (double X, double Y)>
            {
                { "nw", (x, y) },
                { "n", (x + w / 2, y) },
                { "ne", (x + w, y) },
                { "e", (x + w, y + h / 2) },
                { "se", (x + w, y + h) },
                { "s", (x + w / 2, y + h) },
                { "sw", (x, y + h) },
                { "w", (x, y + h / 2) }
            };

            foreach (var kvp in handlePositions)
            {
                if (_resizeHandles.TryGetValue(kvp.Key, out var handle))
                {
                    Canvas.SetLeft(handle, kvp.Value.X - 4);
                    Canvas.SetTop(handle, kvp.Value.Y - 4);
                }
            }
        }

        private Rectangle CreateHandle(string direction, double x, double y)
        {
            var handle = new Rectangle
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.White,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 1,
                Cursor = GetResizeCursor(direction),
                Tag = direction,
                IsHitTestVisible = true
            };

            Canvas.SetLeft(handle, x - 4);
            Canvas.SetTop(handle, y - 4);

            handle.MouseDown += ResizeHandle_MouseDown;

            return handle;
        }

        private Cursor GetResizeCursor(string dir)
        {
            return dir switch
            {
                "nw" or "se" => Cursors.SizeNWSE,
                "ne" or "sw" => Cursors.SizeNESW,
                "n" or "s" => Cursors.SizeNS,
                "e" or "w" => Cursors.SizeWE,
                _ => Cursors.Arrow
            };
        }

        private void ResizeHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_resizeTarget == null) return;

            var handle = sender as Rectangle;
            if (handle == null) return;

            CaptureBoardStateForUndo();
            _resizeDirection = handle.Tag.ToString();
            _isResizing = true;

            var screenPos = e.GetPosition(Viewport);
            _resizeStartWorld = ScreenToWorld(screenPos);

            if (_resizeTarget is Polyline polyline)
            {
                if (polyline.Points.Count > 0)
                {
                    double minX = polyline.Points.Min(p => p.X);
                    double maxX = polyline.Points.Max(p => p.X);
                    double minY = polyline.Points.Min(p => p.Y);
                    double maxY = polyline.Points.Max(p => p.Y);

                    _startX = minX;
                    _startY = minY;
                    _startW = Math.Max(maxX - minX, 0.1);
                    _startH = Math.Max(maxY - minY, 0.1);

                    _originalCorners = new Dictionary<string, Point>
                    {
                        { "nw", new Point(minX, minY) },
                        { "ne", new Point(maxX, minY) },
                        { "sw", new Point(minX, maxY) },
                        { "se", new Point(maxX, maxY) }
                    };
                }
            }
            else
            {
                _startX = Canvas.GetLeft(_resizeTarget);
                _startY = Canvas.GetTop(_resizeTarget);
                _startW = ((FrameworkElement)_resizeTarget).ActualWidth;
                _startH = ((FrameworkElement)_resizeTarget).ActualHeight;

                if (double.IsNaN(_startX)) _startX = 0;
                if (double.IsNaN(_startY)) _startY = 0;
            }

            Viewport.CaptureMouse();
            e.Handled = true;
        }

        private void ResizeElement(Point world)
        {
            if (_resizeTarget == null || string.IsNullOrEmpty(_resizeDirection)) return;

            double dx = world.X - _resizeStartWorld.X;
            double dy = world.Y - _resizeStartWorld.Y;

            double newX = _startX;
            double newY = _startY;
            double newW = _startW;
            double newH = _startH;

            if (_resizeDirection.Contains("e"))
            {
                newW = Math.Max(1, _startW + dx);
            }
            if (_resizeDirection.Contains("s"))
            {
                newH = Math.Max(1, _startH + dy);
            }
            if (_resizeDirection.Contains("w"))
            {
                double delta = dx;
                newW = Math.Max(1, _startW - delta);
                newX = _startX + delta;
            }
            if (_resizeDirection.Contains("n"))
            {
                double delta = dy;
                newH = Math.Max(1, _startH - delta);
                newY = _startY + delta;
            }

            if (_resizeTarget is Polyline polyline)
            {
                ResizePolyline(polyline, newX, newY, newW, newH);
            }
            else
            {
                var fe = (FrameworkElement)_resizeTarget;
                fe.Width = newW;
                fe.Height = newH;
                Canvas.SetLeft(fe, newX);
                Canvas.SetTop(fe, newY);
            }

            UpdateResizeFrame(newX, newY, newW, newH);
            PushRealtimeResizeUpdate(_resizeTarget);
        }

        private void PushRealtimeResizeUpdate(UIElement? target)
        {
            if (target == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastResizeRealtimePushUtc).TotalMilliseconds < 80)
            {
                return;
            }

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == target.Uid);
            if (shape == null)
            {
                return;
            }

            if (target is Polyline polyline)
            {
                shape.DeserializedPoints = new List<Point>(polyline.Points);
                shape.Points = JsonConvert.SerializeObject(polyline.Points);

                if (polyline.Points.Count > 0)
                {
                    double minX = polyline.Points.Min(p => p.X);
                    double maxX = polyline.Points.Max(p => p.X);
                    double minY = polyline.Points.Min(p => p.Y);
                    double maxY = polyline.Points.Max(p => p.Y);

                    shape.X = (minX + maxX) / 2;
                    shape.Y = (minY + maxY) / 2;
                    shape.Width = maxX - minX;
                    shape.Height = maxY - minY;
                }
            }
            else if (target is FrameworkElement fe)
            {
                shape.Width = fe.Width > 0 ? fe.Width : fe.ActualWidth;
                shape.Height = fe.Height > 0 ? fe.Height : fe.ActualHeight;
                shape.X = Canvas.GetLeft(target) + shape.Width / 2;
                shape.Y = Canvas.GetTop(target) + shape.Height / 2;
            }

            _lastResizeRealtimePushUtc = now;
            PushShapeToFirebase(shape);
        }

        private void ResizePolyline(Polyline polyline, double newX, double newY, double newW, double newH)
        {
            if (polyline == null || polyline.Points.Count == 0 || _originalCorners == null) return;

            string cornerKey = "";
            if (_resizeDirection.Contains("n") && _resizeDirection.Contains("w")) cornerKey = "nw";
            else if (_resizeDirection.Contains("n") && _resizeDirection.Contains("e")) cornerKey = "ne";
            else if (_resizeDirection.Contains("s") && _resizeDirection.Contains("w")) cornerKey = "sw";
            else if (_resizeDirection.Contains("s") && _resizeDirection.Contains("e")) cornerKey = "se";
            else if (_resizeDirection.Contains("n")) cornerKey = "n";
            else if (_resizeDirection.Contains("s")) cornerKey = "s";
            else if (_resizeDirection.Contains("w")) cornerKey = "w";
            else if (_resizeDirection.Contains("e")) cornerKey = "e";

            if (string.IsNullOrEmpty(cornerKey)) return;

            double currentMinX = polyline.Points.Min(p => p.X);
            double currentMaxX = polyline.Points.Max(p => p.X);
            double currentMinY = polyline.Points.Min(p => p.Y);
            double currentMaxY = polyline.Points.Max(p => p.Y);

            double currentWidth = currentMaxX - currentMinX;
            double currentHeight = currentMaxY - currentMinY;

            if (currentWidth <= 0) currentWidth = 0.1;
            if (currentHeight <= 0) currentHeight = 0.1;

            PointCollection newPoints = new PointCollection();

            foreach (var point in polyline.Points)
            {
                double normalizedX = (point.X - currentMinX) / currentWidth;
                double normalizedY = (point.Y - currentMinY) / currentHeight;

                double newPointX = newX + normalizedX * newW;
                double newPointY = newY + normalizedY * newH;

                newPoints.Add(new Point(newPointX, newPointY));
            }

            polyline.Points = newPoints;
        }

        private async Task SaveResizedShapeAsync()
        {
            if (_resizeTarget == null) return;

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _resizeTarget.Uid);
            if (shape == null) return;

            if (_resizeTarget is Polyline polyline)
            {
                // Для линии сохраняем все точки
                shape.DeserializedPoints = new List<Point>(polyline.Points);
                shape.Points = JsonConvert.SerializeObject(polyline.Points);

                // Вычисляем новые границы
                if (polyline.Points.Count > 0)
                {
                    double minX = polyline.Points.Min(p => p.X);
                    double maxX = polyline.Points.Max(p => p.X);
                    double minY = polyline.Points.Min(p => p.Y);
                    double maxY = polyline.Points.Max(p => p.Y);

                    shape.X = (minX + maxX) / 2;
                    shape.Y = (minY + maxY) / 2;
                    shape.Width = maxX - minX;
                    shape.Height = maxY - minY;
                }
            }
            else
            {
                // Для других фигур
                shape.Width = ((FrameworkElement)_resizeTarget).ActualWidth;
                shape.Height = ((FrameworkElement)_resizeTarget).ActualHeight;
                shape.X = Canvas.GetLeft(_resizeTarget) + shape.Width / 2;
                shape.Y = Canvas.GetTop(_resizeTarget) + shape.Height / 2;
            }

            // Сохраняем в Supabase
            await _supabaseService.SaveShapeAsync(shape);
            MarkSaved();

            // Отправляем в Firebase для реалтайм обновлений
            PushShapeToFirebase(shape);
        }

        private async void Color_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                var selectedBrush = btn.Background;
                var selectedColorString = btn.Background.ToString();

                if (_resizeTarget != null)
                {
                    await ApplyColorToElement(_resizeTarget, selectedBrush, selectedColorString);
                }
                else
                {
                    _currentBrush = selectedBrush;
                    _currentColorString = selectedColorString;
                }
            }
        }

        private async Task ApplyColorToElement(UIElement element, Brush brush, string colorString)
        {
            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == element.Uid);
            if (shape == null)
            {
                return;
            }

            if (string.Equals(shape.Color, colorString, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CaptureBoardStateForUndo();

            if (element is Shape shapeElement)
                shapeElement.Stroke = brush;
            else if (element is Polyline polyline)
                polyline.Stroke = brush;
            else if (element is TextBox tb)
                tb.Foreground = brush;

            shape.Color = colorString;

            // Сохраняем в Supabase
            await _supabaseService.SaveShapeAsync(shape);
            MarkSaved();

            // Отправляем в Firebase для реалтайм обновлений
            PushShapeToFirebase(shape);
        }

        private async Task AddImageToBoardAsync(string imagePath)
        {
            if (_isCreatingShape || string.IsNullOrWhiteSpace(imagePath))
            {
                return;
            }

            _isCreatingShape = true;
            try
            {
                CaptureBoardStateForUndo();

                int uniqueId = await _supabaseService.GenerateUniqueIdAsync(_boardId);
                var viewportCenter = new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2);
                var world = ScreenToWorld(viewportCenter);

                var shape = new BoardShape
                {
                    BoardId = _boardId,
                    Type = "image",
                    X = world.X,
                    Y = world.Y,
                    Width = DefaultImageW,
                    Height = DefaultImageH,
                    Color = null,
                    Text = imagePath,
                    Id = uniqueId
                };

                AddShapeToCanvas(shape);
                await _supabaseService.SaveShapeAsync(shape);
                MarkSaved();
                PushShapeToFirebase(shape);

                var image = FindUIElementByUid(shape.Id.ToString());
                if (image != null)
                {
                    ShowResizeFrame(image);
                }
            }
            finally
            {
                _isCreatingShape = false;
            }
        }

        private void ResetToolColorToDefault()
        {
            _currentBrush = Brushes.Black;
            _currentColorString = Brushes.Black.ToString();
        }

        private string GetSnapshotDirectory()
        {
            return IOPath.Combine(BoardSnapshotsRoot, _boardId.ToString("N"));
        }

        private static Brush FindThemeBrush(string key, Brush fallback)
        {
            try
            {
                if (Application.Current?.TryFindResource(key) is Brush brush)
                {
                    return brush;
                }
            }
            catch
            {
                // ignore
            }

            return fallback;
        }

        /// <summary>
        /// Разворачивает окно на границы того монитора, где оно сейчас находится (не на все мониторы сразу).
        /// </summary>
        private static bool TryPlaceWindowOnContainingMonitorFullscreen(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                var hMonitor = PresentationNative.MonitorFromWindow(hwnd, PresentationNative.MonitorDefaultToNearest);
                var mi = new PresentationNative.MONITORINFO
                {
                    cbSize = (uint)Marshal.SizeOf<PresentationNative.MONITORINFO>()
                };

                if (!PresentationNative.GetMonitorInfo(hMonitor, ref mi))
                {
                    return false;
                }

                var src = HwndSource.FromHwnd(hwnd);
                if (src?.CompositionTarget == null)
                {
                    return false;
                }

                var fromDevice = src.CompositionTarget.TransformFromDevice;
                var topLeft = fromDevice.Transform(new Point(mi.rcMonitor.Left, mi.rcMonitor.Top));
                var bottomRight = fromDevice.Transform(new Point(mi.rcMonitor.Right, mi.rcMonitor.Bottom));
                window.Left = topLeft.X;
                window.Top = topLeft.Y;
                window.Width = Math.Max(1, bottomRight.X - topLeft.X);
                window.Height = Math.Max(1, bottomRight.Y - topLeft.Y);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static class PresentationNative
        {
            public const uint MonitorDefaultToNearest = 2;

            [DllImport("user32.dll")]
            public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

            [StructLayout(LayoutKind.Sequential)]
            public struct MONITORINFO
            {
                public uint cbSize;
                public RECT rcMonitor;
                public RECT rcWork;
                public uint dwFlags;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }
        }
    }

    public sealed class BoardVersionSnapshot
    {
        public DateTime SavedAtUtc { get; set; }
        public List<BoardShape> Shapes { get; set; } = new();
    }

    public sealed class BoardParticipantCard
    {
        public Guid UserId { get; set; }
        public string DisplayName { get; set; } = "";
        public string Initials { get; set; } = "";
        public string Role { get; set; } = "viewer";
        public string RoleLabel { get; set; } = "";
        public string RoleActionLabel { get; set; } = "";
        public Visibility RoleActionVisibility { get; set; } = Visibility.Visible;
        public Visibility ActionVisibility { get; set; } = Visibility.Visible;
        public Visibility RemoveActionVisibility { get; set; } = Visibility.Visible;
        public string CurrentUserHint { get; set; } = string.Empty;
        public Visibility CurrentUserHintVisibility { get; set; } = Visibility.Collapsed;
        public string PresenceLabel { get; set; } = "Не в сети";
        public Brush PresenceDotFill { get; set; } = new SolidColorBrush(Color.FromRgb(156, 163, 175));
        public Brush PresenceTextFill { get; set; } = new SolidColorBrush(Color.FromRgb(107, 114, 128));
        public bool IsCurrentUser { get; set; }
        public bool IsOnline { get; set; }
        public Brush AvatarFill { get; set; } = Brushes.White;
        public Brush AvatarStroke { get; set; } = Brushes.Black;
        public Brush RoleBadgeBackground { get; set; } = Brushes.White;
        public Brush RoleBadgeForeground { get; set; } = Brushes.Black;
    }

    public sealed class ChatMessageViewModel : INotifyPropertyChanged
    {
        private string _text = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string MessageId { get; set; } = string.Empty;

        public bool IsMine { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public DateTime SentAtUtc { get; set; }

        public DateTime? EditedAtUtc { get; set; }

        public Visibility EditedBadgeVisibility =>
            EditedAtUtc.HasValue ? Visibility.Visible : Visibility.Collapsed;

        public string HeaderText { get; set; } = string.Empty;

        public string Text
        {
            get => _text;
            set
            {
                if (string.Equals(_text, value, StringComparison.Ordinal))
                {
                    return;
                }

                _text = value;
                NotifyPropertyChanged();
            }
        }

        public HorizontalAlignment HeaderAlignment { get; set; } = HorizontalAlignment.Left;

        public HorizontalAlignment BubbleAlignment { get; set; } = HorizontalAlignment.Left;

        public Brush BubbleBackground { get; set; } = Brushes.White;

        public Brush TextForeground { get; set; } = Brushes.Black;

        private void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
