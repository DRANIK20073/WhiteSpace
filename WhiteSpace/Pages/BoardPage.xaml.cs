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
using System.Threading;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WhiteSpace;
using WhiteSpace.Models;
using WhiteSpace.Dialogs;
using WhiteSpace.Rendering;
using WhiteSpace.Services;

namespace WhiteSpace.Pages
{
    public partial class BoardPage : Page
    {
        private static readonly HttpClient BoardImageHttpClient = CreateBoardImageHttpClient();

        private static HttpClient CreateBoardImageHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WhiteSpace/1.0 (WPF)");
            return client;
        }

        private List<BoardShape> _shapesOnBoard = new List<BoardShape>();
        private readonly Guid _boardId;
        private Board? _boardInfo;
        private readonly SupabaseService _supabaseService;
        private readonly FirebaseService _firebaseService;
        private bool _isLoadingShapes = false; // Флаг для предотвращения двойной загрузки
        private bool _isRestoringHistory;

        private IDisposable? _shapesSubscription;
        private IDisposable? _shapesSnapshotSubscription;
        private IDisposable? _membersSubscription;
        private IDisposable? _cursorsSubscription;
        private IDisposable? _chatSubscription;
        private enum ToolMode { Hand, Select, Pen, Marker, Eraser, Shape, Text, StickyNote, Comment, Arrow }
        private ToolMode _tool = ToolMode.Select;

        private bool _isPlacingSticky;
        private Point _stickyPlacementStartWorld;
        private UIElement? _previewStickyElement;
        private UIElement? _previewCommentElement;
        private const double PreviewElementOpacity = 0.55;
        private const string BoardSelectionChromeTag = "board-selection-chrome";
        private const string SelectionFrameTag = "selection-frame";
        private static readonly string[] ResizeHandleTags = { "nw", "ne", "se", "sw" };
        private static readonly Color SelectionPortStrokeColor = Color.FromRgb(0x2E, 0x90, 0xFF);
        private TextBox? _focusedBoardTextEdit;
        private const double DefaultStickyW = 240;
        private const double DefaultStickyH = 220;

        /// <summary>Выбранный вариант фигуры из палитры (см. <see cref="ShapePalette"/>).</summary>
        private string _shapeKind = "rect";

        private bool _shapesPaletteBuilt;
        private bool _selectionShapeComboBuilt;

        private bool _isCreatingShape = false;
        private Dictionary<string, Point> _originalCorners;

        // Пан/камера
        private bool _isPanning;
        private Point _panStartScreen;
        private double _panStartX, _panStartY;

        // Рисование (карандаш / маркер)
        private bool _isDrawing;
        private Polyline _currentStroke;
        private bool _isErasing;
        private Polyline? _eraserTrailLine;
        private const double EraserHitRadius = 20;

        // Комментарий на доске
        private Border? _commentComposer;
        private Point _pendingCommentWorld;

        // Выбор цвета: заливка и контур независимы (пресеты и «без выделения»).
        private Brush _currentBrush = Brushes.Black;
        private string _currentStrokeHex = "#111111";
        private string _currentFillHex = "#FFFFFF";

        // Силуэты при размещении (фигура, стикер, комментарий, стрелка)
        private UIElement? _previewShapeElement;
        private const double DefaultRectW = 140;
        private const double DefaultRectH = 90;
        private const double DefaultEllipse = 100;

        /// <summary>Минимальные габариты при ресайзе и размещении (прямоугольник, эллипс, блок-схема).</summary>
        private const double MinRectEllipseWidth = 48;
        private const double MinRectEllipseHeight = 40;

        /// <summary>Минимальные размеры стикера.</summary>
        private const double MinStickyWidth = 120;
        private const double MinStickyHeight = 80;

        private const string ShapeLabelPresenterTag = "shapeLabelPresenter";
        private const string StickyAuthorTag = "stickyAuthor";
        private const double ConnectorStrokeThickness = 3.5;

        /// <summary>21 цвет + кнопка «свой» на каждую палитру (заливка и контур).</summary>
        private static readonly string[] FillPaletteHexes =
        {
            "#5C5C5C", "#BDBDBD", "#F44336", "#FF9800", "#FFEB3B", "#4CAF50", "#009688", "#29B6F6", "#9C27B0", "#E91E63", "#FFFFFF",
            "#EEEEEE", "#F5F5F5", "#FFCDD2", "#FFE0B2", "#FFF9C4", "#C8E6C9", "#B2DFDB", "#B3E5FC", "#E1BEE7", "#F8BBD0"
        };

        private bool _fillPaletteBuilt;
        private string _nextRectFillMode = "stroke";

        /// <summary>Рисование прямоугольника/эллипса перетаскиванием (как в Figma).</summary>
        private bool _isPlacingRectEllipse;
        private Point _rectPlacementStartWorld;

        private bool _suppressSelectionToolbarSync;

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
        private Point _dragLastWorld;

        private bool _isMarqueeSelecting;
        private Point _marqueeStartWorld;
        private Rectangle? _marqueeRect;
        private readonly List<UIElement> _multiSelectedElements = new();
        private sealed class MultiDragSnapshot
        {
            public Point Anchor { get; init; }
            public List<Point>? PolylinePoints { get; init; }
            public required UIElement Element { get; init; }
        }

        private readonly Dictionary<int, MultiDragSnapshot> _multiDragSnapshots = new();
        private Point _multiDragStartWorld;
        private bool _wasTextEditingEnabled;

        // Словарь для хранения ручек изменения размера
        private Dictionary<string, Rectangle> _resizeHandles = new Dictionary<string, Rectangle>();

        private readonly List<UIElement> _selectionChromeElements = new();
        private readonly List<FrameworkElement> _anchorPortElements = new();
        private readonly List<Ellipse> _connectorEndpointElements = new();
        private readonly List<Ellipse> _snapHighlightPortElements = new();
        private bool _isDraggingConnectorEndpoint;
        private string? _connectorEndpointDragWhich;
        private int _connectorEndpointDragShapeId;

        private bool _isDrawingConnector;
        private Point _connectorAnchorWorld;
        private Polyline? _connectorPreviewLine;
        private readonly Stack<List<BoardShape>> _undoHistory = new Stack<List<BoardShape>>();
        private readonly Stack<List<BoardShape>> _redoHistory = new Stack<List<BoardShape>>();
        private const double DefaultImageW = 280;
        private const double DefaultImageH = 180;
        private static readonly string BoardSnapshotsRoot = IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhiteSpace",
            "board-snapshots");
        private IDisposable? _presentationSubscription;
        private bool _presentationLockedCollaborators;
        private int? _connectorPortStartShapeId;
        private string? _connectorPortStartSide;
        private DateTime _lastResizeRealtimePushUtc = DateTime.MinValue;
        private bool _removalHandled;
        private readonly DispatcherTimer _accessMonitorTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        private readonly SemaphoreSlim _permissionRefreshLock = new(1, 1);
        private readonly Dictionary<Guid, FirebaseBoardMember> _presenceByUserId = new();
        private Window? _hostWindow;
        private readonly Dictionary<Guid, FirebaseCursorState> _cursorByUserId = new();
        private readonly Dictionary<Guid, FrameworkElement> _cursorVisuals = new();
        private readonly Dictionary<Guid, Brush> _cursorAccentByUserId = new();
        private readonly Dictionary<Guid, (Brush Fill, Brush Stroke)> _participantAvatarByUserId = new();
        private Point _connectorDragStartWorld;
        private bool _connectorDetachedForDrag;
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
        private bool _processingViewportMouseUp;
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
        private double _textResizeStartFontSize = 16;
        private bool _chatUnreadSeeded;
        private DateTime _chatReadWatermarkUtc = DateTime.MinValue;

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
            Viewport.LostMouseCapture += Viewport_LostMouseCapture;

            PreviewKeyDown += BoardPage_PreviewKeyDown;

            Loaded += Page_Loaded;
            Unloaded += Page_Unloaded;
            _accessMonitorTimer.Tick += AccessMonitorTimer_Tick;
            _presenceHeartbeatTimer.Tick += PresenceHeartbeatTimer_Tick;
            _presenceUiRefreshTimer.Tick += PresenceUiRefreshTimer_Tick;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_returnToAdminPage && await _supabaseService.EnforceBanLogoutIfNeededAsync())
            {
                return;
            }

            var prefs = AppPreferences.Load();
            WhiteSpaceThemeManager.Apply(prefs);
            UiAnimationHelper.ApplyFadeIn(BoardRootGrid, prefs.EnableAnimations);

            if (SelectionToolbarFillPopup != null && SelectionToolbarFillButton != null)
            {
                SelectionToolbarFillPopup.PlacementTarget = SelectionToolbarFillButton;
            }

            if (SelectionToolbarStrokePopup != null && SelectionToolbarStrokeButton != null)
            {
                SelectionToolbarStrokePopup.PlacementTarget = SelectionToolbarStrokeButton;
            }

            if (SelectionToolbarShapePopup != null && SelectionToolbarShapeButton != null)
            {
                SelectionToolbarShapePopup.PlacementTarget = SelectionToolbarShapeButton;
            }

            EnsureFillPaletteBuilt();
            EnsureShapesPaletteBuilt();
            UpdateShapesToolLabel();

            await LoadBoardMetadataAsync();
            _isAdminSession = _returnToAdminPage || await _supabaseService.IsCurrentUserAdminAsync();

            if (!_isAdminSession)
            {
                AccountBanGuard.Start();
            }

            // Загружаем фигуры из Supabase
            await LoadShapesFromSupabase();

            // Определяем роль пользователя
            var userRole = _isAdminSession ? "owner" : await _supabaseService.GetUserRoleForBoardAsync(_boardId);
            _myUserRole = userRole;

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
            SetTool(ToolMode.Select);

            await InitCursorIdentityAsync();

            // Подписываемся на изменения из Firebase после известного профиля (для мгновенной смены роли из списка участников)
            SubscribeToShapes();
            SubscribeToPresentationMode();
            SubscribeToBoardMembers();
            SubscribeToCursors();
            SubscribeToChatMessages();
            await SetCurrentUserPresenceAsync(true);
            ChatMessagesItemsControl.ItemsSource = _chatMessages;
            UsersListView.SelectedItem = null;
            UpdatePresentationMenuForRole();
            _accessMonitorTimer.Start();
            _presenceHeartbeatTimer.Start();
            _presenceUiRefreshTimer.Start();

            BoardChatNotificationHub.ActiveBoardId = _boardId;
            _ = BoardChatNotificationHub.SyncSubscriptionsAsync(_supabaseService);

            _hostWindow = Window.GetWindow(this);
            if (_hostWindow != null)
            {
                _hostWindow.PreviewMouseLeftButtonUp += HostWindow_PreviewMouseLeftButtonUp;
                _hostWindow.PreviewMouseDown += HostWindow_PreviewMouseDown;
                _hostWindow.PreviewKeyDown += HostWindow_PreviewKeyDown;
            }
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
            BoardActivityStorage.Touch(_boardId);
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
            RemoveEraserTrail();
            RemoveResizeFrame();
            RemovePreviewShape();

            _isPlacingRectEllipse = false;
            _isPlacingSticky = false;
            _currentStroke = null;
            _isDrawing = false;
            _isDraggingElement = false;
            _dragElement = null;

            BoardCanvas.Children.Clear();
            RemovePreviewSticky();

            foreach (var shape in _shapesOnBoard)
            {
                AddShapeToCanvas(CloneShape(shape), false);
            }

            if (_tool == ToolMode.Shape)
            {
                EnsurePreviewShape();
            }

            if (_tool == ToolMode.StickyNote)
            {
                EnsurePreviewSticky();
            }
        }

        private async Task RestoreBoardStateAsync(List<BoardShape> snapshot)
        {
            _isRestoringHistory = true;
            try
            {
                var newShapes = CloneShapes(snapshot);

                _shapesOnBoard = newShapes;
                RenderCurrentBoardState();
                RemoveResizeFrame();
                ClearMultiSelection();

                await _supabaseService.ReplaceBoardShapesAsync(_boardId, _shapesOnBoard);
                await _firebaseService.ClearAndReplaceBoardShapesAsync(_boardId.ToString(), _shapesOnBoard);

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
            AccountBanGuard.Stop();

            Viewport.LostMouseCapture -= Viewport_LostMouseCapture;

            if (_hostWindow != null)
            {
                _hostWindow.PreviewMouseLeftButtonUp -= HostWindow_PreviewMouseLeftButtonUp;
                _hostWindow.PreviewMouseDown -= HostWindow_PreviewMouseDown;
                _hostWindow.PreviewKeyDown -= HostWindow_PreviewKeyDown;
                _hostWindow = null;
            }

            if (BoardChatNotificationHub.ActiveBoardId == _boardId)
            {
                BoardChatNotificationHub.ActiveBoardId = null;
            }

            // Отписываемся от событий при выходе
            _shapesSubscription?.Dispose();
            _shapesSubscription = null;
            _shapesSnapshotSubscription?.Dispose();
            _shapesSnapshotSubscription = null;
            _membersSubscription?.Dispose();
            _membersSubscription = null;
            _cursorsSubscription?.Dispose();
            _cursorsSubscription = null;
            _chatSubscription?.Dispose();
            _chatSubscription = null;
            _presentationSubscription?.Dispose();
            _presentationSubscription = null;
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
                RemoveResizeFrame();
                RemovePreviewShape();
                _shapesOnBoard.Clear();
                BoardCanvas.Children.Clear();

                var shapes = await _supabaseService.LoadBoardShapesAsync(_boardId);

                foreach (var shape in shapes)
                {
                    AddShapeToCanvas(shape);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadShapesFromSupabase: {ex}");
            }
            finally
            {
                _isLoadingShapes = false;
            }
        }

        // Подписка на изменения из Firebase (только для получения обновлений от других пользователей)
        private void SubscribeToPresentationMode()
        {
            try
            {
                _presentationSubscription?.Dispose();
                _presentationSubscription = _firebaseService
                    .GetBoardPresentationActiveObservable(_boardId.ToString())
                    .Subscribe(active =>
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            _presentationLockedCollaborators = active;
                            ApplyPresentationCollaborativeUi();
                        });
                    });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Подписка на режим презентации: {ex.Message}");
            }
        }

        private bool IsBoardOwnerRole() =>
            string.Equals(_myUserRole, "owner", StringComparison.OrdinalIgnoreCase);

        /// <summary>Наблюдатель или участник при активной презентации (редактирует только ведущий).</summary>
        private bool IsBoardEditLockedForCurrentUser()
        {
            if (_isAdminSession)
            {
                return false;
            }

            if (string.Equals(_myUserRole, "viewer", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return _presentationLockedCollaborators && !IsBoardOwnerRole();
        }

        private void ApplyPresentationCollaborativeUi()
        {
            if (PresentationModeBanner != null)
            {
                PresentationModeBanner.Visibility = _presentationLockedCollaborators
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            ApplyEditingToolsState();
            UpdatePresentationMenuHeader();
        }

        private void ApplyEditingToolsState()
        {
            if (IsBoardEditLockedForCurrentUser())
            {
                DisableEditingTools();
            }
            else
            {
                EnableEditingTools();
            }
        }

        private void UpdatePresentationMenuForRole()
        {
            var ownerOrAdmin = _isAdminSession || IsBoardOwnerRole();
            if (PresentationMenuItem != null)
            {
                PresentationMenuItem.Visibility = ownerOrAdmin ? Visibility.Visible : Visibility.Collapsed;
            }

            if (SaveVersionMenuItem != null)
            {
                SaveVersionMenuItem.Visibility = ownerOrAdmin ? Visibility.Visible : Visibility.Collapsed;
            }

            if (VersionHistoryMenuItem != null)
            {
                VersionHistoryMenuItem.Visibility = ownerOrAdmin ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdatePresentationMenuHeader();
        }

        private void UpdatePresentationMenuHeader()
        {
            if (PresentationMenuItem == null)
            {
                return;
            }

            var on = _presentationLockedCollaborators;
            PresentationMenuItem.Header = on
                ? "Выключить режим презентации"
                : "Включить режим презентации";
        }

        private void SubscribeToShapes()
        {
            try
            {
                _shapesSubscription?.Dispose();
                _shapesSnapshotSubscription?.Dispose();
                _shapesSubscription = _firebaseService
                    .GetShapesObservable(_boardId.ToString())
                    .Subscribe(async change =>
                    {
                        if (_isLoadingShapes)
                        {
                            return;
                        }

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (change.IsDelete)
                            {
                                if (change.ShapeId > 0)
                                {
                                    RemoveShapeFromBoardLocal(change.ShapeId);
                                }

                                return;
                            }

                            if (change.Shape != null)
                            {
                                UpdateOrAddShapeFromFirebase(change.Shape);
                            }
                        });
                    });

                _shapesSnapshotSubscription = _firebaseService
                    .GetBoardShapesObservable(_boardId.ToString())
                    .Subscribe(async snapshot =>
                    {
                        if (_isLoadingShapes || snapshot == null || !snapshot.IsSuccess)
                        {
                            return;
                        }

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            ApplyRemoteShapesSnapshot(snapshot.Shapes);
                        });
                    });
            }
            catch (Exception ex)
            {
                AppDialogService.ShowError($"Ошибка подписки на Firebase: {ex.Message}", "Доска");
            }
        }

        private void ApplyRemoteShapesSnapshot(List<BoardShape>? shapes)
        {
            var incoming = (shapes ?? new List<BoardShape>())
                .Where(shape => shape != null && shape.Id > 0)
                .ToList();

            PurgeOrphanBoardVisuals(incoming.Select(shape => shape.Id));
            RemoveShapesMissingInSnapshot(incoming);

            foreach (var shape in incoming)
            {
                UpdateOrAddShapeFromFirebase(shape);
            }
        }

        private void PurgeOrphanBoardVisuals(IEnumerable<int> validShapeIds)
        {
            var valid = validShapeIds.ToHashSet();
            foreach (var child in BoardCanvas.Children.OfType<FrameworkElement>()
                         .Where(c => !string.IsNullOrEmpty(c.Uid)).ToList())
            {
                if (int.TryParse(child.Uid, out var id) && !valid.Contains(id))
                {
                    BoardCanvas.Children.Remove(child);
                }
            }

            _shapesOnBoard.RemoveAll(shape => !valid.Contains(shape.Id));
        }

        private void RemoveShapesMissingInSnapshot(List<BoardShape>? shapes)
        {
            var incomingShapes = (shapes ?? new List<BoardShape>())
                .Where(shape => shape != null && shape.Id > 0)
                .ToDictionary(shape => shape.Id, shape => shape);

            var removedIds = _shapesOnBoard
                .Where(shape => shape != null && !incomingShapes.ContainsKey(shape.Id))
                .Select(shape => shape.Id)
                .ToList();

            foreach (var removedId in removedIds)
            {
                RemoveShapeFromBoardLocal(removedId);
            }

            if (_resizeTarget != null && !_shapesOnBoard.Any(shape => shape.Id.ToString() == _resizeTarget.Uid))
            {
                RemoveResizeFrame();
            }

            if (_multiSelectedElements.Count > 0)
            {
                _multiSelectedElements.RemoveAll(el =>
                    el is FrameworkElement fe &&
                    !_shapesOnBoard.Any(s => s.Id.ToString() == fe.Uid));
                if (_multiSelectedElements.Count == 0)
                {
                    RemoveResizeFrame();
                }
            }
        }

        private void RemoveShapeFromBoardLocal(int shapeId)
        {
            var uid = shapeId.ToString();
            foreach (var child in BoardCanvas.Children.OfType<FrameworkElement>().Where(c => c.Uid == uid).ToList())
            {
                BoardCanvas.Children.Remove(child);
            }

            _shapesOnBoard.RemoveAll(shape => shape.Id == shapeId);

            if (_resizeTarget is FrameworkElement fe && fe.Uid == uid)
            {
                RemoveResizeFrame();
            }

            _multiSelectedElements.RemoveAll(el =>
                el is FrameworkElement mfe && mfe.Uid == uid);
            if (_multiSelectedElements.Count == 0 && _resizeTarget == null)
            {
                HideSelectionToolbar();
            }
        }

        // Обновление или добавление фигуры из Firebase (от других пользователей)
        private void UpdateOrAddShapeFromFirebase(BoardShape shape)
        {
            if (shape == null || shape.Id <= 0)
            {
                return;
            }

            var existingShapeIndex = _shapesOnBoard.FindIndex(s => s.Id == shape.Id);

            if (existingShapeIndex >= 0)
            {
                var existingShape = _shapesOnBoard[existingShapeIndex];
                var uiElement = FindUIElementByUid(shape.Id.ToString());
                var previousType = existingShape.Type;
                var previousPaletteId = ShapePalette.GetPaletteId(existingShape);
                var typeChanged = !string.Equals(previousType, shape.Type, StringComparison.Ordinal);

                existingShape.Type = shape.Type;
                existingShape.Color = shape.Color;
                existingShape.X = shape.X;
                existingShape.Y = shape.Y;
                existingShape.Width = shape.Width;
                existingShape.Height = shape.Height;
                existingShape.Text = shape.Text;
                existingShape.Points = shape.Points;

                if (shape.Type is "line" or "marker" or "connector" && !string.IsNullOrEmpty(shape.Points))
                {
                    existingShape.DeserializedPoints = JsonConvert.DeserializeObject<List<Point>>(shape.Points) ?? new List<Point>();
                }
                else
                {
                    existingShape.DeserializedPoints = new List<Point>();
                }

                if (shape.Type == "connector" && ConnectorAttachmentHelper.TryParse(existingShape.Text, out _))
                {
                    ApplyConnectorGeometryToBoardShape(existingShape);
                }

                var currentPaletteId = ShapePalette.GetPaletteId(existingShape);
                var paletteChanged = !string.Equals(previousPaletteId, currentPaletteId, StringComparison.Ordinal);
                var shouldRecreateVisual = typeChanged || paletteChanged;

                if (uiElement != null)
                {
                    Console.WriteLine($"Получено обновление для фигуры {shape.Id}: цвет {shape.Color}");

                    if (shouldRecreateVisual)
                    {
                        BoardCanvas.Children.Remove(uiElement);
                        AddShapeToCanvas(existingShape, false);
                        uiElement = FindUIElementByUid(shape.Id.ToString());
                    }

                    if (uiElement == null)
                    {
                        return;
                    }

                    UpdateUIElementFromShape(uiElement, existingShape);

                    if (shape.Type is "line" or "marker" && uiElement is Polyline targetPolyline)
                    {
                        targetPolyline.Points.Clear();
                        foreach (var point in existingShape.DeserializedPoints)
                        {
                            targetPolyline.Points.Add(point);
                        }

                        ApplyStrokeStyleToPolyline(targetPolyline, existingShape, GetBrushFromColor(existingShape.Color));
                    }
                    else if (shape.Type == "comment" && uiElement is Grid commentGrid)
                    {
                        SyncCommentVisual(commentGrid, existingShape);
                    }
                }
                else
                {
                    AddShapeToCanvas(existingShape, false);
                }

                if (shape.Type != "connector")
                {
                    RefreshConnectorVisualsReferencingShapeLocal(shape.Id);
                }
            }
            else
            {
                if (FindUIElementByUid(shape.Id.ToString()) is { } existingUi)
                {
                    _shapesOnBoard.Add(shape);
                    UpdateUIElementFromShape(existingUi, shape);
                    return;
                }

                Console.WriteLine($"Получена новая фигура из Firebase: {shape.Id}, цвет {shape.Color}");
                _shapesOnBoard.Add(shape);
                AddShapeToCanvas(shape);
            }
        }

        // Обновление UI элемента из данных фигуры
        private void UpdateUIElementFromShape(UIElement element, BoardShape shape)
        {
            var brush = GetBrushFromColor(shape.Color);

            if (element is Grid grid)
            {
                if (shape.Type == "stickyNote")
                {
                    ApplyStickyNoteVisual(grid, shape);
                    SyncStickyNoteAuthorAndText(grid, shape);
                }
                else if (grid.Tag is Shape inner)
                {
                    if (shape.Type is "rectangle" or "ellipse")
                    {
                        ApplyRectEllipseVisual(inner, shape);
                    }

                    SyncShapeLabelPresenter(grid, shape);
                }
            }
            else if (element is Shape sh)
            {
                if (shape.Type is "rectangle" or "ellipse")
                {
                    ApplyRectEllipseVisual(sh, shape);
                }
                else
                {
                    sh.Stroke = brush;
                }
            }
            else if (element is Polyline polyline)
            {
                polyline.Stroke = brush;
            }
            else if (element is Canvas canvas && shape.Type == "connector")
            {
                ConnectorVisualHelper.ApplyStyle(canvas, shape, brush, ConnectorStrokeThickness);
            }
            else if (element is TextBox textBox)
            {
                textBox.Foreground = brush;
                textBox.Text = shape.Text ?? string.Empty;
                textBox.FontSize = ParseTextShapeFontSize(shape.Points, shape.Height, 16);
                ApplyTextBoxChrome(textBox, brush);
            }
            else if (element is Image image)
            {
                image.Stretch = Stretch.Uniform;
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                image.Source = null;
                ScheduleBoardImageLoad(image, shape.Text, null);
            }

            // Обновляем позицию и размеры
            if (shape.Type == "comment" && element is Grid commentGrid)
            {
                Canvas.SetLeft(element, shape.X);
                Canvas.SetTop(element, shape.Y);
                SyncCommentVisual(commentGrid, shape);
            }
            else if (shape.Type == "text")
            {
                Canvas.SetLeft(element, shape.X);
                Canvas.SetTop(element, shape.Y);

                if (element is TextBox tb)
                {
                    tb.Width = shape.Width;
                    tb.Height = shape.Height;
                }
            }
            else if (shape.Type is "line" or "marker" or "connector")
            {
                List<Point>? points;
                if (shape.Type == "connector")
                {
                    points = ConnectorAttachmentHelper.ResolveConnectorPoints(shape, _shapesOnBoard);
                    if (points.Count < 2 && !string.IsNullOrWhiteSpace(shape.Points))
                    {
                        points = JsonConvert.DeserializeObject<List<Point>>(shape.Points);
                    }

                    if (FindUIElementByUid(shape.Id.ToString()) is { } connUi && points is { Count: > 0 })
                    {
                        ConnectorVisualHelper.UpdatePoints(connUi, shape, points, brush, ConnectorStrokeThickness);
                    }
                }
                else if (element is Polyline targetPolyline)
                {
                    ApplyStrokeStyleToPolyline(targetPolyline, shape, brush);
                    points = string.IsNullOrEmpty(shape.Points)
                        ? new List<Point>()
                        : JsonConvert.DeserializeObject<List<Point>>(shape.Points);

                    if (points != null && points.Count > 0)
                    {
                        targetPolyline.Points.Clear();
                        foreach (var point in points)
                        {
                            targetPolyline.Points.Add(point);
                        }
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
                _membersSubscription?.Dispose();
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
                _cursorsSubscription?.Dispose();
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

        private (Brush Fill, Brush Stroke) ResolveParticipantAvatarBrushes(Guid? userId)
        {
            if (userId.HasValue && _participantAvatarByUserId.TryGetValue(userId.Value, out var known))
            {
                return (CloneBrushForFill(known.Fill), CloneBrushForFill(known.Stroke));
            }

            var index = Math.Abs((userId ?? Guid.Empty).GetHashCode());
            var (fill, stroke) = GetParticipantPalette(index);
            return (CloneBrushForFill(fill), CloneBrushForFill(stroke));
        }

        private (Brush Fill, Brush Stroke) ResolveCommentAuthorAvatarBrushes(BoardCommentMetadata meta)
        {
            if (Guid.TryParse(meta.AuthorId, out var authorId))
            {
                return ResolveParticipantAvatarBrushes(authorId);
            }

            var index = Math.Abs(meta.DisplayAuthor().GetHashCode(StringComparison.OrdinalIgnoreCase));
            var (fill, stroke) = GetParticipantPalette(index);
            return (CloneBrushForFill(fill), CloneBrushForFill(stroke));
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

        private async void Viewport_LostMouseCapture(object sender, MouseEventArgs e)
        {
            await ProcessViewportMouseUpAsync(Mouse.GetPosition(Viewport));
        }

        private async Task UpdateBoardMembersFromFirebase(List<FirebaseBoardMember> members)
        {
            try
            {
                Console.WriteLine($"Получено обновление участников из Firebase. Количество: {members?.Count ?? 0}");

                ApplyCurrentUserRoleFromFirebaseMembers(members);
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
                        UsersListView.IsEnabled = _isAdminSession || currentUserMember.Role == "owner";
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

            if (boardMember.Role == "owner" && !_isAdminSession)
            {
                AppDialogService.ShowWarning("Вы не можете изменить роль владельца.", "Изменение роли");
                return;
            }

            string newRole = boardMember.Role switch
            {
                "viewer" => "editor",
                "editor" => "viewer",
                "owner" when _isAdminSession => "editor",
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
            _participantAvatarByUserId.Clear();

            foreach (var member in members)
            {
                var accountUsername = member.AccountUsername;

                // Если в board_members есть снимок username — берём его и не трогаем profiles.
                if (!string.IsNullOrWhiteSpace(accountUsername))
                {
                    var accountDisplayName = accountUsername;
                    var accountInitials = GetInitials(accountDisplayName);

                    var (accountFill, accountStroke) = GetParticipantPalette(colorIndex++);
                    _participantAvatarByUserId[member.UserId] = (accountFill, accountStroke);
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
                        RoleActionVisibility = (_isAdminSession || member.Role != "owner") ? Visibility.Visible : Visibility.Collapsed,
                        ActionVisibility = _isAdminSession ? Visibility.Visible : (accountIsCurrentUser || member.Role == "owner" ? Visibility.Collapsed : Visibility.Visible),
                        RemoveActionVisibility = _isAdminSession ? Visibility.Visible : (accountIsCurrentUser || member.Role == "owner" ? Visibility.Collapsed : Visibility.Visible),
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
                _participantAvatarByUserId[member.UserId] = (fill, stroke);
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
                    RoleActionVisibility = (_isAdminSession || member.Role != "owner") ? Visibility.Visible : Visibility.Collapsed,
                    ActionVisibility = _isAdminSession ? Visibility.Visible : (isCurrentUser || member.Role == "owner" ? Visibility.Collapsed : Visibility.Visible),
                    RemoveActionVisibility = _isAdminSession ? Visibility.Visible : (isCurrentUser || member.Role == "owner" ? Visibility.Collapsed : Visibility.Visible),
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
            if (SelectButton != null) SelectButton.IsEnabled = false;
            if (ArrowToolButton != null) ArrowToolButton.IsEnabled = false;
            if (MarkerButton != null) MarkerButton.IsEnabled = false;
            if (EraserButton != null) EraserButton.IsEnabled = false;
            if (CommentButton != null) CommentButton.IsEnabled = false;
            ShapesToolButton.IsEnabled = false;
            TextButton.IsEnabled = false;
            StickyNoteButton.IsEnabled = false;
            UndoButton.IsEnabled = false;
            RedoButton.IsEnabled = false;
            ClearBoardButton.IsEnabled = false;
            AddImageButton.IsEnabled = false;
            ColorPanel.IsEnabled = false;
        }

        private void EnableEditingTools()
        {
            PenButton.IsEnabled = true;
            if (SelectButton != null) SelectButton.IsEnabled = true;
            if (ArrowToolButton != null) ArrowToolButton.IsEnabled = true;
            if (MarkerButton != null) MarkerButton.IsEnabled = true;
            if (EraserButton != null) EraserButton.IsEnabled = true;
            if (CommentButton != null) CommentButton.IsEnabled = true;
            ShapesToolButton.IsEnabled = true;
            TextButton.IsEnabled = true;
            StickyNoteButton.IsEnabled = true;
            UndoButton.IsEnabled = true;
            RedoButton.IsEnabled = true;
            ClearBoardButton.IsEnabled = true;
            AddImageButton.IsEnabled = true;
            ColorPanel.IsEnabled = true;
        }

        private void SetViewerMode(bool isViewer)
        {
            ViewerModeText.Visibility = isViewer ? Visibility.Visible : Visibility.Collapsed;
            ApplyEditingToolsState();
        }

        private async Task RefreshCurrentUserPermissionsAsync()
        {
            if (_removalHandled)
            {
                return;
            }

            await _permissionRefreshLock.WaitAsync();
            try
            {
                if (_removalHandled)
                {
                    return;
                }

                if (_isAdminSession)
                {
                    SetViewerMode(false);
                    return;
                }

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

                _myUserRole = role;
                SetViewerMode(string.Equals(role, "viewer", StringComparison.OrdinalIgnoreCase));
                UpdatePresentationMenuForRole();
            }
            finally
            {
                _permissionRefreshLock.Release();
            }
        }

        /// <summary>Сразу подтягиваем роль из Firebase (обновляется при смене роли владельцем), до запроса Supabase.</summary>
        private void ApplyCurrentUserRoleFromFirebaseMembers(List<FirebaseBoardMember>? members)
        {
            if (_isAdminSession || members == null || members.Count == 0 || !_myUserId.HasValue)
            {
                return;
            }

            FirebaseBoardMember? mine = null;
            foreach (var m in members)
            {
                if (Guid.TryParse(m.UserId, out var uid) && uid == _myUserId.Value)
                {
                    mine = m;
                    break;
                }
            }

            if (mine == null || string.IsNullOrWhiteSpace(mine.Role))
            {
                return;
            }

            var incoming = mine.Role.Trim();
            if (string.Equals(_myUserRole, incoming, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _myUserRole = incoming;
            SetViewerMode(string.Equals(incoming, "viewer", StringComparison.OrdinalIgnoreCase));
            UpdatePresentationMenuForRole();
        }

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_boardInfo?.AccessCode))
            {
                AppDialogService.ShowInfo("Код доски пока недоступен.", "Поделиться доской");
                return;
            }

            var link = BoardInviteLinkBuilder.BuildInviteLink(_boardInfo.AccessCode);
            var win = new BoardShareWindow(_boardInfo.AccessCode, link)
            {
                Owner = Window.GetWindow(this)
            };
            win.ShowDialog();
        }

        private void BackToMenu_Click(object sender, RoutedEventArgs e)
        {
            NavigateBackFromBoard();
        }

        private void Help_Click(object sender, RoutedEventArgs e) =>
            HelpService.Show(Window.GetWindow(this), "board");

        private void BoardMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (BoardMenuButton.ContextMenu != null)
            {
                BoardMenuButton.ContextMenu.PlacementTarget = BoardMenuButton;
                BoardMenuButton.ContextMenu.IsOpen = true;
            }
        }

        private async void PresentationMode_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAdminSession && !IsBoardOwnerRole())
            {
                AppDialogService.ShowInfo(
                    "Режим презентации может включить только владелец доски.",
                    "Презентация");
                return;
            }

            var nextActive = !_presentationLockedCollaborators;

            try
            {
                await _firebaseService.SetBoardPresentationActiveAsync(_boardId.ToString(), nextActive);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Firebase презентация: {ex.Message}");
            }

            AppDialogService.ShowInfo(
                nextActive
                    ? "Режим презентации включён: участники не смогут редактировать доску, пока вы его не выключите."
                    : "Режим презентации выключен.",
                "Презентация");

            UpdatePresentationMenuHeader();
        }

        private async void SaveVersion_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAdminSession && !IsBoardOwnerRole())
            {
                AppDialogService.ShowWarning("Сохранять версии доски может только владелец.", "Версия доски");
                return;
            }

            if (_shapesOnBoard.Count == 0)
            {
                AppDialogService.ShowInfo("На доске пока нет объектов для сохранения версии.", "Версия доски");
                return;
            }

            var payload = new BoardVersionSnapshot
            {
                Name = $"Версия от {DateTime.UtcNow:dd MMMM yyyy, HH:mm}",
                SavedAtUtc = DateTime.UtcNow,
                Shapes = CloneShapes(_shapesOnBoard)
            };

            try
            {
                var versionKey = $"v-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
                await _firebaseService.PushBoardVersionSnapshotAsync(_boardId.ToString(), versionKey, payload);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Версия в Firebase: {ex.Message}");
            }

            MarkSaved();
            AppDialogService.ShowSuccess("Версия сохранена в облаке для всех участников доски.", "Версия доски");
        }

        private async void VersionHistory_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAdminSession && !IsBoardOwnerRole())
            {
                AppDialogService.ShowWarning("Загружать версии доски может только владелец.", "История версий");
                return;
            }

            var ordered = await LoadCloudVersionItemsAsync();

            if (ordered.Count == 0)
            {
                AppDialogService.ShowInfo("Сохранённых облачных версий пока нет.", "История версий");
                return;
            }

            while (true)
            {
                var dlg = new BoardVersionHistoryDialog(ordered)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dlg.ShowDialog() == true && dlg.SelectedVersion != null)
                {
                    if (!AppDialogService.ShowConfirmation(
                            "Восстановить выбранную версию? Текущее состояние будет сохранено в стеке отмены, лишние объекты удалятся у всех участников.",
                            "История версий",
                            "Восстановить",
                            "Отмена"))
                    {
                        return;
                    }

                    _undoHistory.Push(CloneShapes(_shapesOnBoard));
                    await RestoreBoardStateAsync(dlg.SelectedVersion.Snapshot.Shapes);
                    AppDialogService.ShowSuccess("Выбранная версия восстановлена для всех участников.", "История версий");
                    return;
                }

                if (dlg.DeleteRequested && dlg.SelectedVersion != null)
                {
                    await _firebaseService.DeleteBoardVersionAsync(_boardId.ToString(), dlg.SelectedVersion.Key);
                    ordered.RemoveAll(i => i.Key == dlg.SelectedVersion.Key);
                    if (ordered.Count == 0)
                    {
                        AppDialogService.ShowInfo("Все версии удалены.", "История версий");
                        return;
                    }

                    continue;
                }

                return;
            }
        }

        private async Task<List<BoardVersionHistoryDialog.VersionListItem>> LoadCloudVersionItemsAsync()
        {
            var items = new List<BoardVersionHistoryDialog.VersionListItem>();
            try
            {
                var remote = await _firebaseService.GetBoardVersionSnapshotsAsync(_boardId.ToString());
                foreach (var entry in remote)
                {
                    var snap = entry.Snapshot;
                    if (snap?.Shapes == null)
                    {
                        continue;
                    }

                    items.Add(new BoardVersionHistoryDialog.VersionListItem
                    {
                        Key = entry.Key,
                        Snapshot = snap,
                        DisplayLabel = snap.GetDisplayName()
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Версии Firebase: {ex.Message}");
            }

            return items.OrderByDescending(i => i.Snapshot.SavedAtUtc).ToList();
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
            NavigationService?.Navigate((_isAdminSession || _returnToAdminPage) ? new AdminPage() : new UserHomePage());
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
            if (!await _supabaseService.ClearBoardShapesAsync(_boardId))
            {
                return;
            }

            _shapesOnBoard.Clear();
            RenderCurrentBoardState();
            await _firebaseService.ReplaceBoardShapesAsync(_boardId.ToString(), _shapesOnBoard);
            MarkSaved();
        }

        private async void AddImage_Click(object sender, RoutedEventArgs e)
        {
            if (_isAdminSession)
            {
                var adminDialog = new OpenFileDialog
                {
                    Title = "Выберите изображение",
                    Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp"
                };

                if (adminDialog.ShowDialog() == true)
                {
                    await AddImageToBoardAsync(adminDialog.FileName);
                }

                return;
            }

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
            if (show)
            {
                MarkChatPanelAsRead();
            }
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
                MarkChatPanelAsRead();
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
                _chatSubscription?.Dispose();
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

            UpdateChatUnreadBadgeAfterMessages(messages);
        }

        private static DateTime ChatSentAtUtc(FirebaseChatMessage m) =>
            m.SentAtUtc.Kind == DateTimeKind.Utc
                ? m.SentAtUtc
                : m.SentAtUtc.ToUniversalTime();

        private static DateTime ChatSentAtUtc(ChatMessageViewModel vm) =>
            vm.SentAtUtc.Kind == DateTimeKind.Utc
                ? vm.SentAtUtc
                : vm.SentAtUtc.ToUniversalTime();

        private void UpdateChatFabBadge(int count)
        {
            if (ChatUnreadBadge == null || ChatUnreadBadgeText == null)
            {
                return;
            }

            if (count <= 0)
            {
                ChatUnreadBadge.Visibility = Visibility.Collapsed;
                return;
            }

            ChatUnreadBadge.Visibility = Visibility.Visible;
            ChatUnreadBadgeText.Text = count > 99 ? "99+" : count.ToString();
        }

        private void MarkChatPanelAsRead()
        {
            if (_chatMessages.Count == 0)
            {
                _chatReadWatermarkUtc = DateTime.UtcNow;
            }
            else
            {
                _chatReadWatermarkUtc = _chatMessages.Max(ChatSentAtUtc);
            }

            _chatUnreadSeeded = true;
            UpdateChatFabBadge(0);
        }

        private void UpdateChatUnreadBadgeAfterMessages(List<FirebaseChatMessage> messages)
        {
            var myUserIdString = _myUserId?.ToString();

            if (!_chatUnreadSeeded && messages.Count > 0)
            {
                _chatUnreadSeeded = true;
                _chatReadWatermarkUtc = messages.Max(ChatSentAtUtc);
                UpdateChatFabBadge(0);
                return;
            }

            if (ChatWidget.Visibility == Visibility.Visible)
            {
                if (messages.Count > 0)
                {
                    _chatReadWatermarkUtc = messages.Max(ChatSentAtUtc);
                }

                UpdateChatFabBadge(0);
                return;
            }

            if (!_chatUnreadSeeded)
            {
                UpdateChatFabBadge(0);
                return;
            }

            var unread = messages.Count(m =>
                !string.IsNullOrWhiteSpace(m.Text) &&
                !string.IsNullOrWhiteSpace(m.UserId) &&
                !string.IsNullOrWhiteSpace(myUserIdString) &&
                !string.Equals(m.UserId, myUserIdString, StringComparison.OrdinalIgnoreCase) &&
                ChatSentAtUtc(m) > _chatReadWatermarkUtc);

            UpdateChatFabBadge(unread);
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

        private void AddShapeToCanvas(BoardShape shape, bool addToBoardState = true, ImageSource? prefetchedBoardImage = null, string? boardImageLocalFallbackPath = null)
        {
            if (shape.Id > 0 && FindUIElementByUid(shape.Id.ToString()) is UIElement existingSameId)
            {
                if (addToBoardState && _shapesOnBoard.All(s => s.Id != shape.Id))
                {
                    _shapesOnBoard.Add(shape);
                }

                UpdateUIElementFromShape(existingSameId, shape);
                return;
            }

            Brush brush = GetBrushFromColor(shape.Color);

            if (shape.Type is "line" or "marker")
            {
                if (string.IsNullOrWhiteSpace(shape.Points))
                {
                    return;
                }

                List<Point>? points;
                try
                {
                    points = JsonConvert.DeserializeObject<List<Point>>(shape.Points);
                }
                catch
                {
                    return;
                }

                if (points == null || points.Count == 0)
                {
                    return;
                }

                var polyline = new Polyline
                {
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Uid = shape.Id.ToString()
                };
                ApplyStrokeStyleToPolyline(polyline, shape, brush);

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
            else if (shape.Type == "comment")
            {
                var commentVisual = CreateCommentBoardContainer(shape);
                Canvas.SetLeft(commentVisual, shape.X);
                Canvas.SetTop(commentVisual, shape.Y);
                BoardCanvas.Children.Add(commentVisual);
                if (addToBoardState)
                {
                    _shapesOnBoard.Add(shape);
                }
            }
            else if (shape.Type == "text")
            {
                var fontSize = ParseTextShapeFontSize(shape.Points, shape.Height, 16);
                var textBox = new TextBox
                {
                    Text = shape.Text ?? string.Empty,
                    MinWidth = 48,
                    MinHeight = 28,
                    FontSize = fontSize,
                    Foreground = brush,
                    Uid = shape.Id.ToString(),
                    IsReadOnly = false,
                    Focusable = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    Padding = new Thickness(1, 2, 1, 2)
                };

                ApplyTextBoxChrome(textBox, brush);

                if (shape.Width > 0)
                {
                    textBox.Width = shape.Width;
                }

                if (shape.Height > 0)
                {
                    textBox.Height = shape.Height;
                }

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
                    Stretch = Stretch.Uniform,
                    Uid = shape.Id.ToString(),
                    Source = prefetchedBoardImage
                };

                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

                Canvas.SetLeft(image, shape.X - image.Width / 2);
                Canvas.SetTop(image, shape.Y - image.Height / 2);

                BoardCanvas.Children.Add(image);
                if (addToBoardState)
                {
                    _shapesOnBoard.Add(shape);
                }

                if (prefetchedBoardImage == null)
                {
                    ScheduleBoardImageLoad(image, shape.Text, boardImageLocalFallbackPath);
                }
            }
            else if (shape.Type == "connector")
            {
                if (ConnectorAttachmentHelper.TryParse(shape.Text, out _))
                {
                    ApplyConnectorGeometryToBoardShape(shape);
                }

                var cpoints = ConnectorAttachmentHelper.ResolveConnectorPoints(shape, _shapesOnBoard);
                if (cpoints.Count < 2 && !string.IsNullOrWhiteSpace(shape.Points))
                {
                    try
                    {
                        cpoints = JsonConvert.DeserializeObject<List<Point>>(shape.Points) ?? new List<Point>();
                    }
                    catch
                    {
                        cpoints = new List<Point>();
                    }
                }

                if (cpoints.Count < 2)
                {
                    return;
                }

                var conn = ConnectorVisualHelper.Build(shape, cpoints, brush, ConnectorStrokeThickness);
                BoardCanvas.Children.Add(conn);
                if (addToBoardState)
                {
                    _shapesOnBoard.Add(shape);
                }
            }
            else if (shape.Type == "stickyNote")
            {
                var sticky = CreateStickyNoteBoardContainer(shape);
                Canvas.SetLeft(sticky, shape.X - shape.Width / 2);
                Canvas.SetTop(sticky, shape.Y - shape.Height / 2);
                BoardCanvas.Children.Add(sticky);
                if (addToBoardState)
                {
                    _shapesOnBoard.Add(shape);
                }
            }
            else
            {
                UIElement? element = shape.Type is "rectangle" or "ellipse"
                    ? CreateShapeBoardContainer(shape)
                    : null;

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

        private void ScheduleBoardImageLoad(Image imageControl, string? imageRef, string? localFallbackPath)
        {
            if (imageControl == null)
            {
                return;
            }

            _ = ApplyBoardImageFromRefAsync(imageControl, imageRef, localFallbackPath);
        }

        private async Task ApplyBoardImageFromRefAsync(Image imageControl, string? imageRef, string? localFallbackPath)
        {
            byte[]? bytes = null;
            try
            {
                bytes = await ResolveBoardImageBytesAsync(imageRef, localFallbackPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Board image download: {ex.Message}");
            }

            if (bytes == null || bytes.Length < 8)
            {
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!BoardCanvas.Children.Contains(imageControl))
                {
                    return;
                }

                try
                {
                    imageControl.Source = CreateImageSourceFromBytes(bytes);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Board image decode: {ex.Message}");
                }
            });
        }

        private static async Task<byte[]?> ResolveBoardImageBytesAsync(string? imageRef, string? localFallbackPath)
        {
            if (!string.IsNullOrWhiteSpace(localFallbackPath) && File.Exists(localFallbackPath))
            {
                return await File.ReadAllBytesAsync(localFallbackPath).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(imageRef))
            {
                return null;
            }

            var urlString = imageRef.Trim();
            if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri) &&
                urlString.StartsWith("/", StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(SupabaseService.SupabaseUrl))
            {
                urlString = SupabaseService.SupabaseUrl.TrimEnd('/') + urlString;
                Uri.TryCreate(urlString, UriKind.Absolute, out uri);
            }

            if (uri != null && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                using var response = await BoardImageHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }

            if (File.Exists(imageRef))
            {
                return await File.ReadAllBytesAsync(imageRef).ConfigureAwait(false);
            }

            return null;
        }

        private static ImageSource? CreateImageSourceFromBytes(byte[] data)
        {
            using var ms = new MemoryStream(data, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = ms;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        // Обработчики событий для TextBox
        private void TextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            if (_tool == ToolMode.Select && e.LeftButton == MouseButtonState.Pressed)
            {
                var world = ScreenToWorld(e.GetPosition(Viewport));
                _dragStartWorld = world;
                _dragLastWorld = world;
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
        private void Select_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Select);
        private void ArrowTool_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Arrow);
        private void Pen_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Pen);
        private void Marker_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Marker);
        private void Eraser_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Eraser);
        private void Comment_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Comment);
        private void Text_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Text);

        private void StickyNote_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.StickyNote);

        private void ShapesToolButton_Click(object sender, RoutedEventArgs e)
        {
            EnsureShapesPaletteBuilt();
            if (ShapesPalettePopup != null && ShapesToolButton != null)
            {
                ShapesPalettePopup.PlacementTarget = ShapesToolButton;
                ShapesPalettePopup.IsOpen = true;
            }
        }

        private void ShapePaletteChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string id)
            {
                return;
            }

            _shapeKind = id;
            UpdateShapesToolLabel();
            if (ShapesPalettePopup != null)
            {
                ShapesPalettePopup.IsOpen = false;
            }

            SetTool(ToolMode.Shape);
        }

        private void SetTool(ToolMode tool)
        {
            _tool = tool;
            ColorPanel.Visibility = tool is ToolMode.Eraser or ToolMode.Comment
                ? Visibility.Collapsed
                : Visibility.Visible;

            _isDrawing = false;
            _currentStroke = null;
            _isErasing = false;
            _isPanning = false;
            _isDraggingElement = false;
            _dragElement = null;
            _isPlacingRectEllipse = false;
            _isPlacingSticky = false;
            HideCommentComposer();

            if (tool != ToolMode.Select)
            {
                RemoveResizeFrame();
            }

            SyncMainToolbarToolHighlight(tool);
            SyncMainToolbarColorHighlight();

            RemovePreviewShape();
            RemovePreviewSticky();
            RemovePreviewComment();

            if (_tool == ToolMode.Shape)
            {
                EnsurePreviewShape();
            }

            if (_tool == ToolMode.StickyNote)
            {
                EnsurePreviewSticky();
            }

            if (_tool == ToolMode.Comment)
            {
                EnsurePreviewComment();
            }

            Viewport.Cursor = _tool switch
            {
                ToolMode.Hand => Cursors.Hand,
                ToolMode.Select => Cursors.Arrow,
                ToolMode.Pen => Cursors.Pen,
                ToolMode.Marker => Cursors.Pen,
                ToolMode.Eraser => Cursors.SizeAll,
                ToolMode.Comment => Cursors.Cross,
                ToolMode.Text => Cursors.IBeam,
                ToolMode.Arrow => Cursors.Cross,
                _ => Cursors.Cross
            };

            foreach (var child in BoardCanvas.Children)
            {
                if (child is TextBox textBox)
                {
                    if (_tool is ToolMode.Hand or ToolMode.Select)
                    {
                        textBox.IsReadOnly = true;
                        textBox.Cursor = _tool == ToolMode.Hand ? Cursors.Hand : Cursors.Arrow;
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

            if (_tool is ToolMode.Hand or ToolMode.Select)
            {
                LockStickyNoteEditorsForHandTool();
            }
        }

        /// <summary>Сбрасывает режим правки текста у стикеров при выборе «Руки» (вложенный TextBox не обходится циклом по Children).</summary>
        private void LockStickyNoteEditorsForHandTool()
        {
            if (IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            foreach (var s in _shapesOnBoard)
            {
                if (s.Type != "stickyNote")
                {
                    continue;
                }

                if (FindUIElementByUid(s.Id.ToString()) is not Grid stickyGrid)
                {
                    continue;
                }

                if (TryGetStickyNoteBodyTextBox(stickyGrid) is not TextBox noteTb)
                {
                    continue;
                }

                noteTb.Focusable = false;
                noteTb.IsHitTestVisible = false;
                noteTb.IsReadOnly = true;

                if (ReferenceEquals(Keyboard.FocusedElement, noteTb))
                {
                    Keyboard.ClearFocus();
                }

                if (ReferenceEquals(_focusedBoardTextEdit, noteTb))
                {
                    _focusedBoardTextEdit = null;
                }
            }
        }

        private static TextBox? TryGetStickyNoteBodyTextBox(Grid stickyGrid)
        {
            if (stickyGrid.Children.Count < 2 || stickyGrid.Children[1] is not Grid inner)
            {
                return null;
            }

            foreach (var c in inner.Children)
            {
                if (c is TextBox tb)
                {
                    return tb;
                }
            }

            return null;
        }

        private static bool IsDescendantOf(DependencyObject? node, DependencyObject? ancestor)
        {
            for (var d = node; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, ancestor))
                {
                    return true;
                }
            }

            return false;
        }

        private Point ScreenToWorld(Point screenPoint)
        {
            var s = BoardScale.ScaleX;
            return new Point(
                (screenPoint.X - BoardTranslate.X) / s,
                (screenPoint.Y - BoardTranslate.Y) / s
            );
        }

        /// <summary>Координаты мира → координаты внутри области просмотра (без масштаба содержимого).</summary>
        private Point WorldToViewport(Point world)
        {
            var s = BoardScale.ScaleX;
            return new Point(world.X * s + BoardTranslate.X, world.Y * s + BoardTranslate.Y);
        }

        private void HideSelectionToolbar()
        {
            if (SelectionToolbarPanel != null)
            {
                SelectionToolbarPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateSelectionToolbarPosition()
        {
            if (SelectionToolbarPanel == null || SelectionToolbarPanel.Visibility != Visibility.Visible)
            {
                return;
            }

            if (!TryGetSelectionBounds(out var left, out var top, out var w, out var h))
            {
                return;
            }

            Point worldTopCenter;
            var shape = _resizeTarget is FrameworkElement fe
                ? _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == fe.Uid)
                : null;
            if (shape?.Type == "comment")
            {
                worldTopCenter = new Point(shape.X + 20, shape.Y);
            }
            else
            {
                worldTopCenter = new Point(left + w / 2, top);
            }

            var vp = WorldToViewport(worldTopCenter);

            SelectionToolbarPanel.UpdateLayout();
            double toolbarW = SelectionToolbarPanel.ActualWidth > 1 ? SelectionToolbarPanel.ActualWidth : 160;
            double toolbarH = SelectionToolbarPanel.ActualHeight > 1 ? SelectionToolbarPanel.ActualHeight : 36;

            double marginLeft = vp.X - toolbarW / 2;
            double marginTop = vp.Y - toolbarH - 12;
            marginLeft = Math.Max(8, Math.Min(marginLeft, Math.Max(0, Viewport.ActualWidth - toolbarW - 8)));
            marginTop = Math.Max(8, marginTop);

            SelectionToolbarPanel.Margin = new Thickness(marginLeft, marginTop, 0, 0);
        }

        private void EnsureFillPaletteBuilt()
        {
            if (_fillPaletteBuilt || FillPaletteGridFill == null || FillPaletteGridStroke == null)
            {
                return;
            }

            _fillPaletteBuilt = true;
            FillPaletteGridFill.Children.Clear();
            FillPaletteGridStroke.Children.Clear();

            foreach (var hex in FillPaletteHexes)
            {
                FillPaletteGridFill.Children.Add(CreatePaletteSwatchButton(hex, true));
                FillPaletteGridStroke.Children.Add(CreatePaletteSwatchButton(hex, false));
            }

            FillPaletteGridFill.Children.Add(CreatePaletteCustomButton(true));
            FillPaletteGridStroke.Children.Add(CreatePaletteCustomButton(false));
        }

        private void EnsureShapesPaletteBuilt()
        {
            if (_shapesPaletteBuilt || ShapesPaletteBasicGrid == null || ShapesPaletteFlowGrid == null)
            {
                return;
            }

            _shapesPaletteBuilt = true;
            foreach (var e in ShapePalette.Basic)
            {
                ShapesPaletteBasicGrid.Children.Add(CreateShapePaletteButton(e));
            }

            foreach (var e in ShapePalette.Flowchart)
            {
                ShapesPaletteFlowGrid.Children.Add(CreateShapePaletteButton(e));
            }
        }

        private void EnsureSelectionToolbarShapePaletteBuilt()
        {
            if (_selectionShapeComboBuilt || SelectionToolbarShapeBasicGrid == null ||
                SelectionToolbarShapeFlowGrid == null)
            {
                return;
            }

            _selectionShapeComboBuilt = true;
            foreach (var e in ShapePalette.Basic)
            {
                SelectionToolbarShapeBasicGrid.Children.Add(CreateSelectionToolbarShapePaletteButton(e));
            }

            foreach (var e in ShapePalette.Flowchart)
            {
                SelectionToolbarShapeFlowGrid.Children.Add(CreateSelectionToolbarShapePaletteButton(e));
            }
        }

        private System.Windows.Controls.Button CreateSelectionToolbarShapePaletteButton(ShapePalette.Entry e)
        {
            var strokeBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            var icon = CreatePaletteIcon(e.Id, strokeBrush);
            var btn = new System.Windows.Controls.Button
            {
                Tag = e.Id,
                ToolTip = e.Title,
                Margin = new Thickness(2),
                Padding = new Thickness(3),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Content = icon
            };
            btn.Click += SelectionToolbarShapePaletteChip_Click;
            return btn;
        }

        private async void SelectionToolbarShapePaletteChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button b || b.Tag is not string id)
            {
                return;
            }

            if (SelectionToolbarShapePopup != null)
            {
                SelectionToolbarShapePopup.IsOpen = false;
            }

            await TryChangeSelectedShapeKindAsync(id);
        }

        private void SelectionToolbarShapeButton_Click(object sender, RoutedEventArgs e)
        {
            EnsureSelectionToolbarShapePaletteBuilt();
            if (SelectionToolbarShapePopup != null && SelectionToolbarShapeButton != null)
            {
                SelectionToolbarShapePopup.PlacementTarget = SelectionToolbarShapeButton;
                SelectionToolbarShapePopup.IsOpen = true;
            }
        }

        private void SelectionToolbarStrokeButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectionToolbarStrokePopup != null && SelectionToolbarStrokeButton != null)
            {
                SelectionToolbarStrokePopup.PlacementTarget = SelectionToolbarStrokeButton;
                SelectionToolbarStrokePopup.IsOpen = true;
            }
        }

        private async void FillPaletteCustomColorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!TryPromptColorPicker(isStrokeContour: false, out var hex))
            {
                return;
            }

            await ApplyToolbarPaletteHexAsync(hex, false);
            if (SelectionToolbarFillPopup != null)
            {
                SelectionToolbarFillPopup.IsOpen = false;
            }
        }

        private async void StrokePaletteCustomColorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!TryPromptColorPicker(isStrokeContour: true, out var hex))
            {
                return;
            }

            await ApplyToolbarPaletteHexAsync(hex, true);
            if (SelectionToolbarStrokePopup != null)
            {
                SelectionToolbarStrokePopup.IsOpen = false;
            }
        }

        private bool TryPromptColorPicker(bool isStrokeContour, out string hex)
        {
            hex = "#111111";
            var initialHex = hex;

            if (_resizeTarget != null)
            {
                var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _resizeTarget.Uid);
                if (shape != null && shape.Type is "rectangle" or "ellipse")
                {
                    var app = RectEllipseAppearance.Parse(shape);
                    initialHex = isStrokeContour
                        ? (shape.Color ?? "#111111")
                        : (app.FillHex ?? shape.Color ?? "#111111");
                }
                else if (shape != null)
                {
                    initialHex = shape.Color ?? "#111111";
                }
            }
            else
            {
                initialHex = isStrokeContour ? (_currentStrokeHex ?? "#111111") : (_currentFillHex ?? "#FFFFFF");
            }

            if (!TryParseWpfColor(initialHex, out var initialColor))
            {
                initialColor = Color.FromRgb(0x11, 0x11, 0x11);
            }

            var dlg = new HsvColorPickerWindow(initialColor, isStrokeContour ? "Цвет контура" : "Цвет заливки")
            {
                Owner = Window.GetWindow(this)
            };

            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.SelectedHex))
            {
                return false;
            }

            var raw = dlg.SelectedHex.Trim();
            if (!raw.StartsWith("#", StringComparison.Ordinal))
            {
                raw = "#" + raw;
            }

            hex = NormalizeColorKey(raw);
            return hex.Length >= 4;
        }

        private static bool TryParseWpfColor(string? key, out Color c)
        {
            c = Colors.Black;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            try
            {
                var o = ColorConverter.ConvertFromString(key.Trim());
                if (o is Color cc)
                {
                    c = cc;
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static string ResolvePaletteTitle(string id)
        {
            foreach (var e in ShapePalette.Basic.Concat(ShapePalette.Flowchart))
            {
                if (e.Id == id)
                {
                    return e.Title;
                }
            }

            return "Фигуры";
        }

        private void UpdateShapesToolLabel()
        {
            // Подпись к инструменту «Фигуры» убрана в пользу иконки; выбранный вид задаётся палитрой.
        }

        private Button CreateShapePaletteButton(ShapePalette.Entry e)
        {
            var borderBrush = (Brush)(TryFindResource("WsBorderBrush") ?? new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)));
            var strokeBrush = (Brush)(TryFindResource("WsTextPrimaryBrush") ?? Brushes.Black);

            var icon = CreatePaletteIcon(e.Id, strokeBrush);
            var btn = new Button
            {
                Tag = e.Id,
                ToolTip = e.Title,
                Margin = new Thickness(3),
                Padding = new Thickness(4),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = borderBrush,
                Content = icon
            };
            btn.Click += ShapePaletteChip_Click;
            return btn;
        }

        private static FrameworkElement CreatePaletteIcon(string id, Brush strokeBrush)
        {
            if (id == "circle")
            {
                return new System.Windows.Shapes.Path
                {
                    Width = 32,
                    Height = 32,
                    Stretch = Stretch.Uniform,
                    Data = new EllipseGeometry(new Point(16, 16), 12, 12),
                    Stroke = strokeBrush,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                };
            }

            return new System.Windows.Shapes.Path
            {
                Width = 32,
                Height = 32,
                Stretch = Stretch.Uniform,
                Data = BoardShapeOutlineGeometry.Get(id),
                Stroke = strokeBrush,
                StrokeThickness = 1.6,
                Fill = Brushes.Transparent
            };
        }

        private BoardShape BuildTransientPreviewShape()
        {
            var circleLike = _shapeKind == "circle";
            var w = circleLike ? DefaultEllipse : DefaultRectW;
            var h = circleLike ? DefaultEllipse : DefaultRectH;
            var (dbType, kindForJson) = ShapePalette.ResolveStorage(_shapeKind);
            var bs = new BoardShape
            {
                BoardId = _boardId,
                Type = dbType,
                Width = w,
                Height = h,
                Color = _currentStrokeHex,
                Id = 0,
                X = 0,
                Y = 0
            };
            var app = new RectEllipseAppearance
            {
                Mode = _nextRectFillMode,
                FillHex = _currentFillHex
            };
            if (kindForJson != null)
            {
                app.ShapeKind = kindForJson;
            }

            app.SaveTo(bs);
            return bs;
        }

        private BoardShape BuildTransientPreviewSticky()
        {
            var meta = new StickyNoteAppearance();
            var bs = new BoardShape
            {
                BoardId = _boardId,
                Type = "stickyNote",
                Width = DefaultStickyW,
                Height = DefaultStickyH,
                Color = meta.EffectivePaperHex(),
                Text = "",
                Id = 0,
                X = 0,
                Y = 0
            };
            meta.SaveTo(bs);
            return bs;
        }

        private bool IsPlacementPreview(UIElement? u) =>
            u == _previewShapeElement || u == _previewStickyElement || u == _previewCommentElement;

        private static (Brush body, Brush author) StickyNoteTextBrushes(string? paperHex = null)
        {
            if (WhiteSpaceThemeManager.IsDarkApplied)
            {
                return (Brushes.White, new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)));
            }

            return (
                new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)));
        }

        private static void ApplyStickyNoteTextBoxColors(TextBox tb, string? paperHex = null)
        {
            if (WhiteSpaceThemeManager.IsDarkApplied)
            {
                tb.Foreground = Brushes.White;
                tb.CaretBrush = Brushes.White;
                return;
            }

            var (body, _) = StickyNoteTextBrushes(paperHex);
            tb.Foreground = body;
            tb.CaretBrush = body;
        }

        private void RefreshPreviewShapeIfNeeded()
        {
            if (_tool != ToolMode.Shape)
            {
                return;
            }

            RemovePreviewShape();
            EnsurePreviewShape();
        }

        private Button CreatePaletteSwatchButton(string hex, bool isFillColumn)
        {
            var bc = new BrushConverter();
            Brush fill = Brushes.Gray;
            try
            {
                if (bc.ConvertFromString(hex) is Brush b)
                {
                    fill = b;
                }
            }
            catch
            {
                // ignore
            }

            var ring = new Ellipse
            {
                Width = 28,
                Height = 28,
                Stroke = Brushes.Transparent,
                StrokeThickness = 0
            };
            var inner = new Ellipse
            {
                Width = 22,
                Height = 22,
                Fill = fill
            };
            var host = new Grid { Width = 32, Height = 32, Background = Brushes.Transparent };
            host.Children.Add(ring);
            host.Children.Add(inner);
            var wrap = new Button
            {
                Tag = hex,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                Content = host,
                ToolTip = hex
            };
            wrap.Click += (_, _) =>
            {
                _ = ApplyToolbarPaletteHexAsync(hex, !isFillColumn);
            };
            return wrap;
        }

        private Button CreatePaletteCustomButton(bool isFillColumn)
        {
            var customHost = new Grid { Width = 32, Height = 32 };
            var grad = new Ellipse
            {
                Width = 24,
                Height = 24,
                Stroke = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                StrokeThickness = 1
            };
            grad.Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0xFF, 0x3B, 0x30), 0),
                    new GradientStop(Color.FromRgb(0xFF, 0xD6, 0x0A), 0.25),
                    new GradientStop(Color.FromRgb(0x34, 0xC7, 0x59), 0.5),
                    new GradientStop(Color.FromRgb(0x00, 0x7A, 0xFF), 0.75),
                    new GradientStop(Color.FromRgb(0xAF, 0x52, 0xDE), 1)
                }
            };
            customHost.Children.Add(grad);
            var customBtn = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                Content = customHost,
                ToolTip = isFillColumn ? "Свой цвет заливки" : "Свой цвет контура"
            };
            customBtn.Click += (_, _) =>
            {
                _ = ApplyCustomColorFromDialogAsync(isStrokeContour: !isFillColumn);
            };
            return customBtn;
        }

        private void HighlightPaletteGrid(UniformGrid? grid, string? preferredHex)
        {
            if (grid == null)
            {
                return;
            }

            var key = NormalizeColorKey(preferredHex);
            foreach (var child in grid.Children)
            {
                if (child is not Button wrap || wrap.Content is not Grid g || g.Children.Count < 2)
                {
                    continue;
                }

                if (g.Children[0] is not Ellipse ring || g.Children[1] is not Ellipse inner)
                {
                    continue;
                }

                var hx = wrap.Tag as string;
                if (string.IsNullOrEmpty(hx))
                {
                    ring.Stroke = Brushes.Transparent;
                    ring.StrokeThickness = 0;
                    continue;
                }

                var match = NormalizeColorKey(hx) == key;
                ring.Stroke = match ? new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6)) : Brushes.Transparent;
                ring.StrokeThickness = match ? 3 : 0;
            }
        }

        private void SyncPaletteGridRingHighlights(string? fillHex, string? strokeHex)
        {
            HighlightPaletteGrid(FillPaletteGridFill, fillHex);
            HighlightPaletteGrid(FillPaletteGridStroke, strokeHex);
        }

        private void SyncMainToolbarToolHighlight(ToolMode activeTool)
        {
            void SetActive(Button? btn, bool on)
            {
                if (btn == null)
                {
                    return;
                }

                btn.Tag = on ? "active" : null;
            }

            SetActive(SelectButton, activeTool == ToolMode.Select);
            SetActive(HandButton, activeTool == ToolMode.Hand);
            SetActive(PenButton, activeTool == ToolMode.Pen);
            SetActive(MarkerButton, activeTool == ToolMode.Marker);
            SetActive(EraserButton, activeTool == ToolMode.Eraser);
            SetActive(ShapesToolButton, activeTool == ToolMode.Shape);
            SetActive(TextButton, activeTool == ToolMode.Text);
            SetActive(StickyNoteButton, activeTool == ToolMode.StickyNote);
            SetActive(ArrowToolButton, activeTool == ToolMode.Arrow);
            SetActive(CommentButton, activeTool == ToolMode.Comment);
        }

        private static bool IsDrawingColorTool(ToolMode tool) =>
            tool is ToolMode.Pen or ToolMode.Marker or ToolMode.Arrow or ToolMode.Shape
                or ToolMode.Text or ToolMode.StickyNote;

        private void SyncMainToolbarColorHighlight()
        {
            if (ColorPanel == null)
            {
                return;
            }

            var showColorRing = IsDrawingColorTool(_tool);
            var activeKey = showColorRing ? NormalizeColorKey(_currentStrokeHex) : string.Empty;
            foreach (var child in ColorPanel.Children)
            {
                if (child is not Button btn || ReferenceEquals(btn, MainToolbarColorPickerButton))
                {
                    continue;
                }

                if (!showColorRing)
                {
                    btn.Tag = null;
                    continue;
                }

                var btnKey = NormalizeColorKey(btn.Background?.ToString());
                btn.Tag = !string.IsNullOrEmpty(activeKey)
                            && string.Equals(btnKey, activeKey, StringComparison.OrdinalIgnoreCase)
                    ? "active"
                    : null;
            }
        }

        private void SyncSelectionToolbarChipHighlights(string? fillHex, string? strokeHex)
        {
            void SetChip(Button? btn, string? hex)
            {
                if (btn == null)
                {
                    return;
                }

                btn.Tag = string.IsNullOrWhiteSpace(hex) ? null : "active";
            }

            SetChip(SelectionToolbarFillButton, fillHex);
            SetChip(SelectionToolbarStrokeButton, strokeHex);
        }

        private void SelectionToolbarFillButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectionToolbarFillPopup == null || SelectionToolbarFillButton == null)
            {
                return;
            }

            SelectionToolbarFillPopup.PlacementTarget = SelectionToolbarFillButton;
            SelectionToolbarFillPopup.IsOpen = !SelectionToolbarFillPopup.IsOpen;
            if (SelectionToolbarFillPopup.IsOpen)
            {
                RefreshFillPopupRingHighlight();
            }
        }

        private void RefreshFillPopupRingHighlight()
        {
            if (_resizeTarget == null)
            {
                SyncPaletteGridRingHighlights(_currentFillHex, _currentStrokeHex);
                return;
            }

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _resizeTarget.Uid);
            if (shape == null)
            {
                return;
            }

            if (shape.Type == "stickyNote")
            {
                var sn = StickyNoteAppearance.Parse(shape);
                var paper = sn.EffectivePaperHex();
                SyncPaletteGridRingHighlights(paper, paper);
                return;
            }

            if (shape.Type is not ("rectangle" or "ellipse"))
            {
                var c = shape.Color;
                SyncPaletteGridRingHighlights(c, c);
                return;
            }

            var app = RectEllipseAppearance.Parse(shape);
            var fillHx = app.FillHex ?? shape.Color;
            SyncPaletteGridRingHighlights(fillHx, shape.Color);
        }

        private void SyncFillToolbarSwatchesFromShape(BoardShape? shape)
        {
            if (SelectionToolbarFillSwatch == null || SelectionToolbarStrokeSwatch == null)
            {
                return;
            }

            var bc = new BrushConverter();
            if (shape == null)
            {
                return;
            }

            if (shape.Type is "rectangle" or "ellipse")
            {
                var app = RectEllipseAppearance.Parse(shape);
                var fillKey = string.IsNullOrWhiteSpace(app.FillHex) ? shape.Color : app.FillHex;
                var strokeKey = shape.Color ?? "#111111";
                if (string.IsNullOrWhiteSpace(fillKey))
                {
                    fillKey = "#111111";
                }

                if (bc.ConvertFromString(fillKey) is Brush fb)
                {
                    SelectionToolbarFillSwatch.Fill = fb;
                }

                if (bc.ConvertFromString(string.IsNullOrWhiteSpace(strokeKey) ? "#111111" : strokeKey) is Brush sb)
                {
                    SelectionToolbarStrokeSwatch.Fill = sb;
                }
            }
            else if (shape.Type == "stickyNote")
            {
                var sn = StickyNoteAppearance.Parse(shape);
                var fillKey = sn.EffectivePaperHex();
                var strokeKey = shape.Color ?? "#9AACBC";
                if (bc.ConvertFromString(fillKey) is Brush fb)
                {
                    SelectionToolbarFillSwatch.Fill = fb;
                }

                if (bc.ConvertFromString(strokeKey) is Brush sb)
                {
                    SelectionToolbarStrokeSwatch.Fill = sb;
                }
            }
            else
            {
                var c = shape.Color ?? "#111111";
                if (bc.ConvertFromString(c) is Brush b)
                {
                    SelectionToolbarFillSwatch.Fill = b;
                    SelectionToolbarStrokeSwatch.Fill = b;
                }
            }

            string? fillRing;
            string? strokeRing;
            if (shape.Type is "rectangle" or "ellipse")
            {
                var app = RectEllipseAppearance.Parse(shape);
                fillRing = app.FillHex ?? shape.Color;
                strokeRing = shape.Color;
            }
            else if (shape.Type == "stickyNote")
            {
                fillRing = StickyNoteAppearance.Parse(shape).EffectivePaperHex();
                strokeRing = shape.Color;
            }
            else
            {
                fillRing = shape.Color;
                strokeRing = shape.Color;
            }

            SyncSelectionToolbarChipHighlights(fillRing, strokeRing);
        }

        private void FillModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string mode)
            {
                return;
            }

            _ = ApplyFillModeToSelectionAsync(mode);
        }

        private async Task ApplyCustomColorFromDialogAsync(bool isStrokeContour)
        {
            if (!TryPromptColorPicker(isStrokeContour, out var hex))
            {
                return;
            }

            await ApplyToolbarPaletteHexAsync(hex, isStrokeContour);
        }

        private async Task ApplyFillModeToSelectionAsync(string mode)
        {
            if (_resizeTarget == null)
            {
                _nextRectFillMode = mode;
                UpdateFillModeButtonStyles(mode);
                RefreshPreviewShapeIfNeeded();
                return;
            }

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _resizeTarget.Uid);
            if (shape == null || (shape.Type != "rectangle" && shape.Type != "ellipse"))
            {
                return;
            }

            if (IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            var app = RectEllipseAppearance.Parse(shape);
            if (string.Equals(app.Mode, mode, StringComparison.Ordinal))
            {
                UpdateFillModeButtonStyles(mode);
                return;
            }

            CaptureBoardStateForUndo();
            app.Mode = mode;
            app.SaveTo(shape);

            var innerFill = TryGetInnerShape(_resizeTarget);
            if (innerFill != null)
            {
                ApplyRectEllipseVisual(innerFill, shape);
            }

            UpdateFillModeButtonStyles(mode);
            _nextRectFillMode = mode;

            await _supabaseService.SaveShapeAsync(shape);
            MarkSaved();
            PushShapeToFirebase(shape);
            RefreshConnectorVisualsReferencingShapeLocal(shape.Id);
        }

        /// <param name="isStrokeContour">true — цвет контура (поле Color), false — цвет заливки (FillHex в JSON).</param>
        private async Task ApplyToolbarPaletteHexAsync(string hex, bool isStrokeContour)
        {
            try
            {
                if (new BrushConverter().ConvertFromString(hex) is not Brush brush)
                {
                    return;
                }

                if (_resizeTarget == null)
                {
                    var norm = NormalizeColorKey(hex);
                    if (isStrokeContour)
                    {
                        _currentStrokeHex = string.IsNullOrEmpty(norm) ? hex : norm;
                        _currentBrush = brush;
                    }
                    else
                    {
                        _currentFillHex = string.IsNullOrEmpty(norm) ? hex : norm;
                    }

                    RefreshPreviewShapeIfNeeded();

                    SyncPaletteGridRingHighlights(_currentFillHex, _currentStrokeHex);
                    SyncMainToolbarColorHighlight();
                    if (SelectionToolbarFillPopup != null)
                    {
                        SelectionToolbarFillPopup.IsOpen = false;
                    }

                    if (SelectionToolbarStrokePopup != null)
                    {
                        SelectionToolbarStrokePopup.IsOpen = false;
                    }

                    return;
                }

                var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _resizeTarget.Uid);
                if (shape == null)
                {
                    return;
                }

                var isRe = shape.Type is "rectangle" or "ellipse";
                var isSticky = shape.Type == "stickyNote";

                if (isSticky && !isStrokeContour)
                {
                    if (IsBoardEditLockedForCurrentUser())
                    {
                        return;
                    }

                    var sn = StickyNoteAppearance.Parse(shape);
                    if (string.Equals(NormalizeColorKey(sn.EffectivePaperHex()), NormalizeColorKey(hex), StringComparison.OrdinalIgnoreCase))
                    {
                        SyncFillToolbarSwatchesFromShape(shape);
                        RefreshFillPopupRingHighlight();
                        if (SelectionToolbarFillPopup != null)
                        {
                            SelectionToolbarFillPopup.IsOpen = false;
                        }

                        return;
                    }

                    CaptureBoardStateForUndo();
                    sn.PaperHex = hex;
                    sn.SaveTo(shape);

                    if (TryGetInnerShape(_resizeTarget) is Rectangle rSticky)
                    {
                        rSticky.Fill = brush;
                    }

                    await _supabaseService.SaveShapeAsync(shape);
                    MarkSaved();
                    PushShapeToFirebase(shape);
                }
                else if (isRe && !isStrokeContour)
                {
                    if (IsBoardEditLockedForCurrentUser())
                    {
                        return;
                    }

                    var app = RectEllipseAppearance.Parse(shape);
                    if (string.Equals(NormalizeColorKey(app.FillHex), NormalizeColorKey(hex), StringComparison.OrdinalIgnoreCase))
                    {
                        SyncFillToolbarSwatchesFromShape(shape);
                        RefreshFillPopupRingHighlight();
                        if (SelectionToolbarFillPopup != null)
                        {
                            SelectionToolbarFillPopup.IsOpen = false;
                        }

                        return;
                    }

                    CaptureBoardStateForUndo();
                    app.FillHex = hex;
                    app.SaveTo(shape);

                    var innerFillHex = TryGetInnerShape(_resizeTarget);
                    if (innerFillHex != null)
                    {
                        ApplyRectEllipseVisual(innerFillHex, shape);
                    }

                    await _supabaseService.SaveShapeAsync(shape);
                    MarkSaved();
                    PushShapeToFirebase(shape);
                    RefreshConnectorVisualsReferencingShapeLocal(shape.Id);
                }
                else
                {
                    await ApplyColorToElement(_resizeTarget, brush, hex);
                }

                SyncFillToolbarSwatchesFromShape(shape);
                RefreshFillPopupRingHighlight();
                if (SelectionToolbarFillPopup != null)
                {
                    SelectionToolbarFillPopup.IsOpen = false;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void UpdateFillModeButtonStyles(string mode)
        {
            void StyleBtn(Button? btn, bool on)
            {
                if (btn == null)
                {
                    return;
                }

                btn.Background = on
                    ? new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6))
                    : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                btn.Foreground = Brushes.White;
                btn.BorderThickness = new Thickness(0);
            }

            StyleBtn(FillModeSolidBtn, mode == "solid");
            StyleBtn(FillModeTintBtn, mode == "tint");
            StyleBtn(FillModeStrokeBtn, mode == "stroke");
        }

        private static Shape? TryGetInnerShape(UIElement? target)
        {
            if (target is Shape sh)
            {
                return sh;
            }

            if (target is Grid g && g.Tag is Shape inner)
            {
                return inner;
            }

            return null;
        }

        private bool IsBoardSelectableElement(UIElement? u)
        {
            if (u is not FrameworkElement fe || string.IsNullOrEmpty(fe.Uid) || IsPlacementPreview(u))
            {
                return false;
            }

            if (!_shapesOnBoard.Any(s => s.Id.ToString() == fe.Uid))
            {
                return false;
            }

            return u is Grid or Shape or Image or Polyline or Canvas;
        }

        private bool IsBoardDraggableElement(UIElement? u)
        {
            if (!IsBoardSelectableElement(u))
            {
                return false;
            }

            if (u is not FrameworkElement fe)
            {
                return false;
            }

            var boardShape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == fe.Uid);
            if (boardShape == null)
            {
                return false;
            }

            if (boardShape.Type == "comment")
            {
                return true;
            }

            return u is Grid or Shape or Image or Polyline or Canvas;
        }

        private void DetachConnectorForFreeDrag(BoardShape connector)
        {
            if (!ConnectorAttachmentHelper.TryDeserialize(connector.Text, out var att) || !att.HasAnyAttachment)
            {
                return;
            }

            var pts = ConnectorAttachmentHelper.ResolveConnectorPoints(connector, _shapesOnBoard);
            att.StartShapeId = null;
            att.StartSide = null;
            att.EndShapeId = null;
            att.EndSide = null;
            connector.Text = ConnectorAttachmentHelper.SerializeForStorage(att);
            if (pts.Count >= 2)
            {
                connector.DeserializedPoints = pts;
                connector.Points = JsonConvert.SerializeObject(pts);
            }
        }

        private void ApplyConnectorGeometryToBoardShape(BoardShape connector)
        {
            var pts = ConnectorAttachmentHelper.ResolveConnectorPoints(connector, _shapesOnBoard);
            if (pts.Count < 2)
            {
                return;
            }

            connector.DeserializedPoints = pts;
            connector.Points = JsonConvert.SerializeObject(pts);

            var minX = pts.Min(p => p.X);
            var maxX = pts.Max(p => p.X);
            var minY = pts.Min(p => p.Y);
            var maxY = pts.Max(p => p.Y);

            connector.X = (minX + maxX) / 2;
            connector.Y = (minY + maxY) / 2;
            connector.Width = Math.Max(maxX - minX, 1);
            connector.Height = Math.Max(maxY - minY, 1);
        }

        private async Task RefreshConnectorsReferencingShapeAsync(int shapeId)
        {
            foreach (var connector in _shapesOnBoard
                         .Where(s => s.Type == "connector" && ConnectorAttachmentHelper.ReferencesShape(s, shapeId))
                         .ToList())
            {
                ApplyConnectorGeometryToBoardShape(connector);
                await _supabaseService.SaveShapeAsync(connector);
                MarkSaved();
                PushShapeToFirebase(connector);

                if (FindUIElementByUid(connector.Id.ToString()) is { } ui)
                {
                    var pts = ConnectorAttachmentHelper.ResolveConnectorPoints(connector, _shapesOnBoard);
                    ConnectorVisualHelper.UpdatePoints(
                        ui, connector, pts, GetBrushFromColor(connector.Color), ConnectorStrokeThickness);
                }
            }
        }

        /// <summary>Только UI: при реалтайм-обновлении фигуры пересчитываем привязанные стрелки без записи в БД.</summary>
        private void RefreshConnectorVisualsReferencingShapeLocal(int shapeId)
        {
            foreach (var connector in _shapesOnBoard.Where(s =>
                         s.Type == "connector" && ConnectorAttachmentHelper.ReferencesShape(s, shapeId)))
            {
                ApplyConnectorGeometryToBoardShape(connector);
                if (FindUIElementByUid(connector.Id.ToString()) is not { } ui)
                {
                    continue;
                }

                var pts = ConnectorAttachmentHelper.ResolveConnectorPoints(connector, _shapesOnBoard);
                ConnectorVisualHelper.UpdatePoints(
                    ui, connector, pts, GetBrushFromColor(connector.Color), ConnectorStrokeThickness);
            }
        }

        private bool TryGetConnectorPortAtViewport(Point viewportPos, out int shapeId, out string side)
        {
            shapeId = 0;
            side = "";
            var hit = VisualTreeHelper.HitTest(Viewport, viewportPos);
            DependencyObject? cur = hit?.VisualHit;
            while (cur != null)
            {
                if (cur is Ellipse el && el.Tag is string tag && tag.StartsWith("port:", StringComparison.Ordinal))
                {
                    var parts = tag.Split(':');
                    if (parts.Length >= 3 && int.TryParse(parts[1], out shapeId))
                    {
                        side = parts[2];
                        return true;
                    }
                }

                cur = VisualTreeHelper.GetParent(cur);
            }

            return false;
        }

        /// <summary>Поднимается от визуала попадания до контейнера доски с Uid (клик по заливке/контуру фигуры).</summary>
        private UIElement? ResolveBoardElementFromHit(DependencyObject? leaf)
        {
            for (var cur = leaf; cur != null; cur = VisualTreeHelper.GetParent(cur))
            {
                if (cur is not FrameworkElement fe || string.IsNullOrEmpty(fe.Uid))
                {
                    continue;
                }

                if (!_shapesOnBoard.Any(s => s.Id.ToString() == fe.Uid))
                {
                    continue;
                }

                if (cur is Grid or Image or Polyline or TextBox or Canvas)
                {
                    return fe;
                }
            }

            return null;
        }

        private Shape BuildInnerShapeVisual(BoardShape shape)
        {
            var inner = BoardShapeVisualFactory.Create(shape);
            ApplyRectEllipseVisual(inner, shape);
            inner.HorizontalAlignment = HorizontalAlignment.Stretch;
            inner.VerticalAlignment = VerticalAlignment.Stretch;
            inner.Margin = new Thickness(0);
            inner.IsHitTestVisible = false;
            return inner;
        }

        private Grid CreateShapeBoardContainer(BoardShape shape, bool forPreview = false)
        {
            var inner = BuildInnerShapeVisual(shape);
            var grid = new Grid
            {
                Width = shape.Width,
                Height = shape.Height,
                Uid = shape.Id.ToString(),
                Tag = inner,
                Background = Brushes.Transparent,
                ClipToBounds = false
            };
            grid.Children.Add(inner);

            if (!forPreview)
            {
                SyncShapeLabelPresenter(grid, shape);
                grid.PreviewMouseLeftButtonDown += ShapeBoardContainer_PreviewMouseLeftButtonDown;
            }

            return grid;
        }

        private Grid CreateStickyNoteBoardContainer(BoardShape shape, bool forPreview = false)
        {
            var meta = StickyNoteAppearance.Parse(shape);
            var paperHex = meta.EffectivePaperHex();
            var paper = GetBrushFromColor(paperHex);
            var (bodyBrush, authorBrush) = StickyNoteTextBrushes(paperHex);

            var bg = new Rectangle
            {
                RadiusX = 14,
                RadiusY = 14,
                Fill = paper,
                Stroke = Brushes.Transparent,
                StrokeThickness = 0,
                IsHitTestVisible = false
            };

            var grid = new Grid
            {
                Width = shape.Width,
                Height = shape.Height,
                Uid = shape.Id.ToString(),
                Tag = bg,
                Background = Brushes.Transparent
            };
            grid.Children.Add(bg);

            var inner = new Grid { IsHitTestVisible = false };
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var readOnlyUser = IsBoardEditLockedForCurrentUser();
            var noteTb = new TextBox
            {
                Text = shape.Text ?? "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(12, 12, 12, 8),
                FontSize = 13,
                Foreground = bodyBrush,
                CaretBrush = bodyBrush,
                SelectionBrush = new SolidColorBrush(Color.FromArgb(90, 0x2E, 0x90, 0xFF)),
                IsReadOnly = true,
                Focusable = false,
                IsHitTestVisible = false
            };
            Grid.SetRow(noteTb, 0);

            var authorBlk = new TextBlock
            {
                Text = meta.DisplayAuthor(),
                Margin = new Thickness(12, 0, 12, 10),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = authorBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
                Tag = StickyAuthorTag
            };
            Grid.SetRow(authorBlk, 1);

            inner.Children.Add(noteTb);
            inner.Children.Add(authorBlk);
            grid.Children.Add(inner);
            Panel.SetZIndex(inner, 1);

            if (!forPreview && !readOnlyUser)
            {
                grid.PreviewMouseLeftButtonDown += (_, ev) =>
                {
                    if (ev.ClickCount != 2)
                    {
                        return;
                    }

                    ev.Handled = true;
                    noteTb.Focusable = true;
                    noteTb.IsHitTestVisible = true;
                    noteTb.IsReadOnly = false;
                    ApplyStickyNoteTextBoxColors(noteTb, paperHex);
                    noteTb.GotFocus -= StickyNoteBody_GotFocus;
                    noteTb.GotFocus += StickyNoteBody_GotFocus;
                    noteTb.Focus();
                    if (string.IsNullOrWhiteSpace(noteTb.Text))
                    {
                        noteTb.SelectAll();
                    }
                    else
                    {
                        noteTb.CaretIndex = noteTb.Text.Length;
                    }

                    _focusedBoardTextEdit = noteTb;
                };
            }

            if (!forPreview)
            {
                var sid = shape.Id;
                noteTb.LostFocus += async (_, _) =>
                {
                    if (readOnlyUser)
                    {
                        return;
                    }

                    if (ReferenceEquals(_focusedBoardTextEdit, noteTb))
                    {
                        _focusedBoardTextEdit = null;
                    }

                    noteTb.GotFocus -= StickyNoteBody_GotFocus;
                    noteTb.Focusable = false;
                    noteTb.IsHitTestVisible = false;
                    noteTb.IsReadOnly = true;

                    await SaveStickyNoteBodyAsync(sid, noteTb.Text);
                };
            }

            return grid;
        }

        private async Task SaveStickyNoteBodyAsync(int shapeId, string? text)
        {
            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id == shapeId);
            if (shape == null || shape.Type != "stickyNote")
            {
                return;
            }

            var t = text ?? "";
            if (shape.Text == t)
            {
                return;
            }

            CaptureBoardStateForUndo();
            shape.Text = t;
            await _supabaseService.SaveShapeAsync(shape);
            MarkSaved();
            PushShapeToFirebase(shape);
        }

        private void ApplyStickyNoteVisual(Grid grid, BoardShape shape)
        {
            if (grid.Tag is not Rectangle bg)
            {
                return;
            }

            var meta = StickyNoteAppearance.Parse(shape);
            var paperHex = meta.EffectivePaperHex();
            bg.Fill = GetBrushFromColor(paperHex);
            var (bodyBrush, authorBrush) = StickyNoteTextBrushes(paperHex);
            if (grid.Children.Count >= 2 && grid.Children[1] is Grid innerGrid)
            {
                foreach (var ch in innerGrid.Children)
                {
                    if (ch is TextBox tb)
                    {
                        tb.Foreground = bodyBrush;
                        tb.CaretBrush = bodyBrush;
                    }
                    else if (ch is TextBlock blk && StickyAuthorTag.Equals(blk.Tag as string))
                    {
                        blk.Foreground = authorBrush;
                    }
                }
            }
            bg.Stroke = Brushes.Transparent;
            bg.StrokeThickness = 0;
        }

        private void StickyNoteBody_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb)
            {
                return;
            }

            var grid = FindParentStickyGrid(tb);
            if (grid == null || string.IsNullOrEmpty(grid.Uid))
            {
                return;
            }

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == grid.Uid);
            if (shape == null)
            {
                return;
            }

            ApplyStickyNoteTextBoxColors(tb, StickyNoteAppearance.Parse(shape).EffectivePaperHex());
        }

        private static Grid? FindParentStickyGrid(DependencyObject? child)
        {
            while (child != null)
            {
                if (child is Grid g && g.Tag is Rectangle)
                {
                    return g;
                }

                child = VisualTreeHelper.GetParent(child);
            }

            return null;
        }

        private void SyncStickyNoteAuthorAndText(Grid grid, BoardShape shape)
        {
            if (grid.Children.Count < 2 || grid.Children[1] is not Grid inner)
            {
                return;
            }

            TextBox? noteTb = null;
            TextBlock? authorBlk = null;
            foreach (var ch in inner.Children)
            {
                if (ch is TextBox tx)
                {
                    noteTb = tx;
                }
                else if (ch is TextBlock tb && StickyAuthorTag.Equals(tb.Tag as string))
                {
                    authorBlk = tb;
                }
            }

            var meta = StickyNoteAppearance.Parse(shape);
            var paperHex = meta.EffectivePaperHex();
            var (bodyBrush, authorBrush) = StickyNoteTextBrushes(paperHex);
            if (authorBlk != null)
            {
                authorBlk.Text = meta.DisplayAuthor();
                authorBlk.Foreground = authorBrush;
            }

            if (noteTb != null)
            {
                if (!noteTb.IsKeyboardFocused)
                {
                    noteTb.Text = shape.Text ?? "";
                }

                ApplyStickyNoteTextBoxColors(noteTb, paperHex);
            }
        }

        private async void ShapeBoardContainer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2 || sender is not Grid grid ||
                IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == grid.Uid);
            if (shape == null || shape.Type is not ("rectangle" or "ellipse"))
            {
                return;
            }

            e.Handled = true;
            var stored = RectEllipseAppearance.Parse(shape).Label?.Trim();
            var tb = new TextBox
            {
                Text = stored ?? "",
                AcceptsReturn = false,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxWidth = Math.Min(Math.Max(grid.Width - 20, 96), 176),
                MaxHeight = Math.Min(Math.Max(grid.Height - 20, 28), 72),
                MinWidth = 72,
                MinHeight = 24,
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.DodgerBlue,
                TextAlignment = TextAlignment.Center
            };

            grid.Children.Add(tb);
            tb.GotFocus += (_, _) => _focusedBoardTextEdit = tb;
            tb.Focus();
            tb.SelectAll();

            void Cleanup(bool save)
            {
                tb.LostFocus -= OnLost;
                if (ReferenceEquals(_focusedBoardTextEdit, tb))
                {
                    _focusedBoardTextEdit = null;
                }

                var text = tb.Text?.Trim() ?? "";
                grid.Children.Remove(tb);
                if (!save)
                {
                    return;
                }

                CaptureBoardStateForUndo();
                var app = RectEllipseAppearance.Parse(shape);
                app.Label = string.IsNullOrEmpty(text) ? null : text;
                app.SaveTo(shape);
                SyncShapeLabelPresenter(grid, shape);
                _ = _supabaseService.SaveShapeAsync(shape);
                MarkSaved();
                PushShapeToFirebase(shape);
            }

            void OnLost(object? _, RoutedEventArgs __)
            {
                Cleanup(true);
            }

            tb.LostFocus += OnLost;
            tb.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter)
                {
                    Cleanup(true);
                    ke.Handled = true;
                }
                else if (ke.Key == Key.Escape)
                {
                    Cleanup(false);
                    ke.Handled = true;
                }
            };

            await Dispatcher.Yield(DispatcherPriority.Background);
        }

        private void SyncShapeLabelPresenter(Grid grid, BoardShape shape)
        {
            var label = RectEllipseAppearance.Parse(shape).Label?.Trim();

            TextBlock? existing = null;
            foreach (var c in grid.Children)
            {
                if (c is TextBlock tb && ShapeLabelPresenterTag.Equals(tb.Tag as string))
                {
                    existing = tb;
                    break;
                }
            }

            if (string.IsNullOrEmpty(label))
            {
                if (existing != null)
                {
                    grid.Children.Remove(existing);
                }

                return;
            }

            if (existing == null)
            {
                existing = new TextBlock
                {
                    Tag = ShapeLabelPresenterTag,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(10, 6, 10, 6),
                    MaxWidth = 176,
                    FontSize = 12,
                    IsHitTestVisible = false
                };
                grid.Children.Add(existing);
            }

            existing.Text = label;
            existing.Foreground = ShapeCaptionForegroundBrush(shape);
            existing.Opacity = 1.0;
            existing.MaxWidth = Math.Min(176, Math.Max(grid.Width * 0.88, 72));
        }

        private static Brush ShapeCaptionForegroundBrush(BoardShape shape)
        {
            var app = RectEllipseAppearance.Parse(shape);
            var fillKey = string.IsNullOrWhiteSpace(app.FillHex) ? shape.Color : app.FillHex;
            if (string.IsNullOrWhiteSpace(fillKey))
            {
                return new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            }

            try
            {
                var c = (Color)ColorConverter.ConvertFromString(fillKey);
                // Заметный, но не чёрный текст поверх заливки (как в FigJam — темнее оттенка фона)
                var dark = Color.FromRgb(
                    (byte)Math.Clamp(c.R * 0.35, 0, 255),
                    (byte)Math.Clamp(c.G * 0.35, 0, 255),
                    (byte)Math.Clamp(c.B * 0.35, 0, 255));
                return new SolidColorBrush(dark);
            }
            catch
            {
                return new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            }
        }

        private static Brush BrushWithFractionAlpha(Brush brush, double fraction)
        {
            if (brush is SolidColorBrush sc)
            {
                var c = sc.Color;
                var a = (byte)Math.Clamp((int)(255 * fraction), 1, 255);
                var nb = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
                nb.Freeze();
                return nb;
            }

            return brush;
        }

        /// <summary>Применяет сохранённый режим заливки к прямоугольнику/эллипсу (заливка и контур раздельно).</summary>
        private void ApplyRectEllipseVisual(Shape shapeElement, BoardShape shape)
        {
            var app = RectEllipseAppearance.Parse(shape);
            var strokeBrush = GetBrushFromColor(shape.Color ?? "#111111");
            var fillKey = string.IsNullOrWhiteSpace(app.FillHex) ? shape.Color : app.FillHex;
            var fillBrush = GetBrushFromColor(string.IsNullOrWhiteSpace(fillKey) ? "#111111" : fillKey);

            switch (app.Mode)
            {
                case "solid":
                    shapeElement.Fill = fillBrush;
                    shapeElement.Stroke = strokeBrush;
                    shapeElement.StrokeThickness = 2;
                    break;
                case "tint":
                    shapeElement.Fill = BrushWithFractionAlpha(fillBrush, 0.38);
                    shapeElement.Stroke = strokeBrush;
                    shapeElement.StrokeThickness = 2;
                    break;
                default:
                    shapeElement.Fill = Brushes.Transparent;
                    shapeElement.Stroke = strokeBrush;
                    shapeElement.StrokeThickness = 2;
                    break;
            }

            ApplyStrokeDashStyle(shapeElement, app.StrokeDash);

            if (shapeElement is Rectangle rr)
            {
                var sk = app.ShapeKind;
                if (sk == "roundRect")
                {
                    var rad = Math.Min(shape.Width, shape.Height) * 0.15;
                    rr.RadiusX = rad;
                    rr.RadiusY = rad;
                }
                else
                {
                    rr.RadiusX = 0;
                    rr.RadiusY = 0;
                }
            }
        }

        private static void ApplyStrokeDashStyle(Shape shapeElement, string? dash)
        {
            shapeElement.StrokeDashArray = null;
            shapeElement.StrokeDashCap = PenLineCap.Flat;
            switch (dash?.ToLowerInvariant())
            {
                case "dash":
                    shapeElement.StrokeDashArray = new DoubleCollection(new double[] { 6, 4 });
                    break;
                case "dot":
                    shapeElement.StrokeDashArray = new DoubleCollection(new double[] { 1, 4 });
                    shapeElement.StrokeDashCap = PenLineCap.Round;
                    break;
                case "dashdot":
                    shapeElement.StrokeDashArray = new DoubleCollection(new double[] { 8, 4, 2, 4 });
                    break;
            }
        }

        private void StrokeStyleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string tag)
            {
                return;
            }

            _ = ApplyStrokeStyleToSelectionAsync(tag);
        }

        private void UpdateStrokeStyleButtonStyles(string? dashTag)
        {
            var d = string.IsNullOrWhiteSpace(dashTag) ? "solid" : dashTag.ToLowerInvariant();

            void StyleBtn(Button? btn, bool on)
            {
                if (btn == null)
                {
                    return;
                }

                btn.Background = on
                    ? new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6))
                    : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                btn.Foreground = Brushes.White;
                btn.BorderThickness = new Thickness(0);
            }

            StyleBtn(StrokeStyleSolidBtn, d == "solid");
            StyleBtn(StrokeStyleDashBtn, d == "dash");
            StyleBtn(StrokeStyleDotBtn, d == "dot");
            StyleBtn(StrokeStyleDashDotBtn, d == "dashdot");
        }

        private async Task ApplyStrokeStyleToSelectionAsync(string dashTag)
        {
            if (_resizeTarget == null)
            {
                return;
            }

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _resizeTarget.Uid);
            if (shape == null)
            {
                return;
            }

            if (IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            var normalized = dashTag.ToLowerInvariant();
            var effective = normalized == "solid" ? null : normalized;

            if (shape.Type == "connector")
            {
                if (!ConnectorAttachmentHelper.TryDeserialize(shape.Text, out var connAtt))
                {
                    connAtt = new ConnectorAttachment();
                }

                if (string.Equals(connAtt.StrokeDash ?? "", effective ?? "", StringComparison.Ordinal))
                {
                    UpdateStrokeStyleButtonStyles(effective ?? "solid");
                    return;
                }

                CaptureBoardStateForUndo();
                connAtt.StrokeDash = effective;
                shape.Text = ConnectorAttachmentHelper.SerializeForStorage(connAtt);
                ConnectorVisualHelper.ApplyStyle(_resizeTarget, shape, GetBrushFromColor(shape.Color), ConnectorStrokeThickness);
                UpdateStrokeStyleButtonStyles(effective ?? "solid");
                await _supabaseService.SaveShapeAsync(shape);
                MarkSaved();
                PushShapeToFirebase(shape);
                return;
            }

            if (shape.Type is not ("rectangle" or "ellipse"))
            {
                return;
            }

            var app = RectEllipseAppearance.Parse(shape);
            if (string.Equals(app.StrokeDash ?? "", effective ?? "", StringComparison.Ordinal))
            {
                UpdateStrokeStyleButtonStyles(effective ?? "solid");
                return;
            }

            CaptureBoardStateForUndo();
            app.StrokeDash = effective;
            app.SaveTo(shape);

            var innerDash = TryGetInnerShape(_resizeTarget);
            if (innerDash != null)
            {
                ApplyRectEllipseVisual(innerDash, shape);
            }

            UpdateStrokeStyleButtonStyles(effective ?? "solid");

            await _supabaseService.SaveShapeAsync(shape);
            MarkSaved();
            PushShapeToFirebase(shape);
            if (shape.Type is "rectangle" or "ellipse")
            {
                RefreshConnectorVisualsReferencingShapeLocal(shape.Id);
            }
        }

        private void ConnectorArrowEnd_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string tag)
            {
                _ = ApplyConnectorArrowAsync(end: true, tag);
            }
        }

        private void ConnectorArrowStart_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string tag)
            {
                _ = ApplyConnectorArrowAsync(end: false, tag);
            }
        }

        private async Task ApplyConnectorArrowAsync(bool end, string arrowKind)
        {
            if (_resizeTarget == null)
            {
                return;
            }

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _resizeTarget.Uid);
            if (shape?.Type != "connector" || IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            if (!ConnectorAttachmentHelper.TryDeserialize(shape.Text, out var att))
            {
                att = new ConnectorAttachment();
            }

            var normalized = arrowKind.ToLowerInvariant();
            if (end)
            {
                if (att.EffectiveArrowEnd() == normalized)
                {
                    return;
                }

                att.ArrowEnd = normalized == "block" ? null : normalized;
            }
            else
            {
                if (att.EffectiveArrowStart() == normalized)
                {
                    return;
                }

                att.ArrowStart = normalized == "none" ? null : normalized;
            }

            CaptureBoardStateForUndo();
            shape.Text = ConnectorAttachmentHelper.SerializeForStorage(att);
            ApplyConnectorGeometryToBoardShape(shape);
            var arrowPts = ConnectorAttachmentHelper.ResolveConnectorPoints(shape, _shapesOnBoard);
            ConnectorVisualHelper.UpdatePoints(
                _resizeTarget, shape, arrowPts, GetBrushFromColor(shape.Color), ConnectorStrokeThickness);
            UpdateConnectorArrowButtonStyles(att);
            await _supabaseService.SaveShapeAsync(shape);
            MarkSaved();
            PushShapeToFirebase(shape);
        }

        private void UpdateConnectorArrowButtonStyles(ConnectorAttachment att)
        {
            void StyleWrap(WrapPanel? panel, string active)
            {
                if (panel == null)
                {
                    return;
                }

                foreach (var child in panel.Children.OfType<Button>())
                {
                    var on = child.Tag is string t &&
                             string.Equals(t, active, StringComparison.OrdinalIgnoreCase);
                    child.Background = on
                        ? new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6))
                        : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                    child.Foreground = Brushes.White;
                    child.BorderThickness = new Thickness(0);
                }
            }

            StyleWrap(ConnectorArrowEndRow, att.EffectiveArrowEnd());
            StyleWrap(ConnectorArrowStartRow, att.EffectiveArrowStart());
        }

        private static string NormalizeColorKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                if (new BrushConverter().ConvertFromString(value.Trim()) is SolidColorBrush sb)
                {
                    var c = sb.Color;
                    return $"#{c.R:X2}{c.G:X2}{c.B:X2}".ToUpperInvariant();
                }
            }
            catch
            {
                // ignore
            }

            var s = value.Trim();
            if (s.StartsWith("#", StringComparison.Ordinal) && s.Length >= 7)
            {
                return s[..7].ToUpperInvariant();
            }

            return s.ToUpperInvariant();
        }

        private static readonly SolidColorBrush SelectionToolbarDefaultBg =
            new(Color.FromRgb(0x2C, 0x2C, 0x2E));

        private static readonly SolidColorBrush SelectionToolbarDefaultBorder =
            new(Color.FromRgb(0x55, 0x55, 0x55));

        private void ApplySelectionToolbarChrome(bool deleteOnlySquare)
        {
            if (SelectionToolbarPanel == null)
            {
                return;
            }

            if (SelectionToolbarDeleteButton != null)
            {
                SelectionToolbarDeleteButton.Style =
                    (Style)FindResource("SelectionToolbarDeleteButtonStyle");
                SelectionToolbarDeleteButton.Width = 36;
                SelectionToolbarDeleteButton.Height = 36;
            }

            if (deleteOnlySquare)
            {
                SelectionToolbarPanel.CornerRadius = new CornerRadius(0);
                SelectionToolbarPanel.Padding = new Thickness(0);
                SelectionToolbarPanel.BorderThickness = new Thickness(0);
                SelectionToolbarPanel.BorderBrush = Brushes.Transparent;
                SelectionToolbarPanel.Background = Brushes.Transparent;
                SelectionToolbarPanel.Effect = null;

                if (SelectionToolbarShapeHost != null)
                {
                    SelectionToolbarShapeHost.Visibility = Visibility.Collapsed;
                }

                if (SelectionToolbarStyleHost != null)
                {
                    SelectionToolbarStyleHost.Visibility = Visibility.Collapsed;
                }

                if (SelectionToolbarDeleteButton != null)
                {
                    SelectionToolbarDeleteButton.Margin = new Thickness(0);
                    SelectionToolbarDeleteButton.Tag = "active";
                }
            }
            else
            {
                SelectionToolbarPanel.CornerRadius = new CornerRadius(10);
                SelectionToolbarPanel.Padding = new Thickness(8, 6, 8, 6);
                SelectionToolbarPanel.BorderThickness = new Thickness(1);
                SelectionToolbarPanel.BorderBrush = SelectionToolbarDefaultBorder;
                SelectionToolbarPanel.Background = SelectionToolbarDefaultBg;
                SelectionToolbarPanel.Effect = new DropShadowEffect
                {
                    BlurRadius = 14,
                    ShadowDepth = 2,
                    Opacity = 0.35,
                    Color = Colors.Black
                };

                if (SelectionToolbarStyleHost != null)
                {
                    SelectionToolbarStyleHost.Visibility = Visibility.Visible;
                }

                if (SelectionToolbarDeleteButton != null)
                {
                    SelectionToolbarDeleteButton.Margin = new Thickness(0);
                    SelectionToolbarDeleteButton.Tag = null;
                }
            }
        }

        private void SyncSelectionToolbar(UIElement element)
        {
            if (SelectionToolbarPanel == null || SelectionToolbarDeleteButton == null ||
                SelectionToolbarShapeButton == null || SelectionToolbarFillButton == null)
            {
                return;
            }

            EnsureFillPaletteBuilt();
            EnsureSelectionToolbarShapePaletteBuilt();

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == element.Uid);
            var isMultiSelect = _multiSelectedElements.Count > 1;

            var readOnly = IsBoardEditLockedForCurrentUser();
            SelectionToolbarDeleteButton.IsEnabled = !readOnly;

            var isComment = shape?.Type == "comment";
            var isConnector = shape?.Type == "connector";
            ApplySelectionToolbarChrome(isComment || isMultiSelect);

            if (isMultiSelect)
            {
                SelectionToolbarShapeButton.Visibility = Visibility.Collapsed;
                if (SelectionToolbarFillButton != null)
                {
                    SelectionToolbarFillButton.Visibility = Visibility.Collapsed;
                }

                if (SelectionToolbarStrokeButton != null)
                {
                    SelectionToolbarStrokeButton.Visibility = Visibility.Collapsed;
                }

                if (ConnectorArrowSection != null)
                {
                    ConnectorArrowSection.Visibility = Visibility.Collapsed;
                }

                SelectionToolbarPanel.Visibility = Visibility.Visible;
                Dispatcher.BeginInvoke(new Action(UpdateSelectionToolbarPosition), DispatcherPriority.Loaded);
                return;
            }
            var canEditGeometry = shape != null && (shape.Type == "rectangle" || shape.Type == "ellipse");
            var strokeOnly = shape?.Type is "line" or "marker" or "connector";
            SelectionToolbarShapeButton.IsEnabled = !readOnly && canEditGeometry;
            if (SelectionToolbarStrokeButton != null)
            {
                SelectionToolbarStrokeButton.IsEnabled = !readOnly && shape != null && !isComment;
                SelectionToolbarStrokeButton.Visibility = shape?.Type is "stickyNote" or "comment"
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            if (SelectionToolbarFillButton != null)
            {
                SelectionToolbarFillButton.Visibility = strokeOnly || isComment
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            SelectionToolbarFillButton.IsEnabled = !readOnly && shape != null && !strokeOnly && !isComment;

            if (FillModeButtonRow != null)
            {
                FillModeButtonRow.Visibility = canEditGeometry ? Visibility.Visible : Visibility.Collapsed;
            }

            if (StrokeStyleRow != null)
            {
                StrokeStyleRow.Visibility = canEditGeometry || isConnector
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (ConnectorArrowSection != null)
            {
                ConnectorArrowSection.Visibility = isConnector ? Visibility.Visible : Visibility.Collapsed;
            }

            _suppressSelectionToolbarSync = true;
            try
            {
                if (canEditGeometry)
                {
                    SelectionToolbarShapeButton.Visibility = Visibility.Visible;
                    var app = RectEllipseAppearance.Parse(shape!);
                    _nextRectFillMode = app.Mode;
                    UpdateFillModeButtonStyles(app.Mode);
                    UpdateStrokeStyleButtonStyles(string.IsNullOrWhiteSpace(app.StrokeDash) ? "solid" : app.StrokeDash);
                }
                else
                {
                    SelectionToolbarShapeButton.Visibility = Visibility.Collapsed;
                }

                SyncFillToolbarSwatchesFromShape(shape);

                string? fillRing = _currentFillHex;
                string? strokeRing = _currentStrokeHex;
                if (shape != null && shape.Type is "rectangle" or "ellipse")
                {
                    var ap = RectEllipseAppearance.Parse(shape);
                    fillRing = ap.FillHex ?? shape.Color;
                    strokeRing = shape.Color;
                }
                else if (shape != null && shape.Type == "stickyNote")
                {
                    var sn = StickyNoteAppearance.Parse(shape);
                    fillRing = sn.EffectivePaperHex();
                    strokeRing = shape.Color;
                }
                else if (strokeOnly && shape != null)
                {
                    fillRing = shape.Color;
                    strokeRing = shape.Color;
                }

                if (isConnector && ConnectorAttachmentHelper.TryDeserialize(shape!.Text, out var connMeta))
                {
                    UpdateStrokeStyleButtonStyles(string.IsNullOrWhiteSpace(connMeta.StrokeDash)
                        ? "solid"
                        : connMeta.StrokeDash);
                    UpdateConnectorArrowButtonStyles(connMeta);
                }

                SyncPaletteGridRingHighlights(fillRing, strokeRing);
                SyncSelectionToolbarChipHighlights(fillRing, strokeRing);
            }
            finally
            {
                _suppressSelectionToolbarSync = false;
            }

            SelectionToolbarPanel.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(UpdateSelectionToolbarPosition), DispatcherPriority.Loaded);
        }

        private async Task TryChangeSelectedShapeKindAsync(string paletteId)
        {
            if (_resizeTarget == null)
            {
                return;
            }

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _resizeTarget.Uid);
            if (shape == null || shape.Type is not ("rectangle" or "ellipse"))
            {
                return;
            }

            if (string.Equals(ShapePalette.GetPaletteId(shape), paletteId, StringComparison.Ordinal))
            {
                return;
            }

            CaptureBoardStateForUndo();

            double left = Canvas.GetLeft(_resizeTarget);
            double top = Canvas.GetTop(_resizeTarget);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            double w = Math.Max(((FrameworkElement)_resizeTarget).ActualWidth, 1);
            double h = Math.Max(((FrameworkElement)_resizeTarget).ActualHeight, 1);

            BoardCanvas.Children.Remove(_resizeTarget);

            var (dbType, kindForJson) = ShapePalette.ResolveStorage(paletteId);
            var app = RectEllipseAppearance.Parse(shape);
            app.ShapeKind = dbType == "ellipse" ? null : kindForJson;
            shape.Type = dbType;

            var (nw, nh) = ShapeLayoutHelper.NormalizeOnKindChange(paletteId, w, h);
            var cx = left + w / 2;
            var cy = top + h / 2;
            left = cx - nw / 2;
            top = cy - nh / 2;
            shape.Width = nw;
            shape.Height = nh;
            shape.X = cx;
            shape.Y = cy;
            app.SaveTo(shape);

            var visual = (UIElement)CreateShapeBoardContainer(shape);

            Canvas.SetLeft(visual, left);
            Canvas.SetTop(visual, top);
            BoardCanvas.Children.Add(visual);

            await _supabaseService.SaveShapeAsync(shape);
            MarkSaved();
            PushShapeToFirebase(shape);

            ShowResizeFrame(visual);
            await RefreshConnectorsReferencingShapeAsync(shape.Id);
        }

        private async Task DeleteSelectedShapeAsync()
        {
            if (_resizeTarget == null)
            {
                return;
            }

            if (IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _resizeTarget.Uid);
            if (shape == null)
            {
                return;
            }

            CaptureBoardStateForUndo();

            if (!await _supabaseService.DeleteShapeAsync(shape.Id))
            {
                return;
            }

            await _firebaseService.DeleteShapeAsync(_boardId.ToString(), shape.Id.ToString());
            RemoveShapeFromBoardLocal(shape.Id);
            MarkSaved();
        }

        private async void SelectionToolbarDelete_Click(object sender, RoutedEventArgs e)
        {
            await DeleteCurrentSelectionAsync();
        }

        private bool HasMultiSelection() => _multiSelectedElements.Count > 1;

        private List<BoardShape> CollectMultiSelectedShapes()
        {
            var result = new List<BoardShape>();
            var seenIds = new HashSet<int>();

            foreach (var el in _multiSelectedElements)
            {
                if (el is not FrameworkElement fe || string.IsNullOrEmpty(fe.Uid))
                {
                    continue;
                }

                if (!int.TryParse(fe.Uid, out var shapeId))
                {
                    continue;
                }

                if (!seenIds.Add(shapeId))
                {
                    continue;
                }

                var shape = _shapesOnBoard.FirstOrDefault(s => s.Id == shapeId);
                if (shape != null)
                {
                    result.Add(shape);
                }
            }

            return result;
        }

        private async Task DeleteCurrentSelectionAsync()
        {
            if (HasMultiSelection())
            {
                await DeleteMultiSelectedAsync();
                return;
            }

            await DeleteSelectedShapeAsync();
        }

        private bool IsEditingTextOnBoard()
        {
            if (Keyboard.FocusedElement is not TextBox tb)
            {
                return false;
            }

            if (tb.Name == "ChatInputBox")
            {
                return true;
            }

            if (_resizeTarget is TextBox rt && ReferenceEquals(rt, tb))
            {
                return true;
            }

            if (_focusedBoardTextEdit != null && ReferenceEquals(_focusedBoardTextEdit, tb))
            {
                return true;
            }

            if (_resizeTarget is Grid stickyGrid
                && TryGetStickyNoteBodyTextBox(stickyGrid) is { } snTb
                && ReferenceEquals(snTb, tb)
                && !snTb.IsReadOnly)
            {
                return true;
            }

            return false;
        }

        private async Task TryDeleteSelectionFromKeyboardAsync()
        {
            if (_resizeTarget == null && _multiSelectedElements.Count == 0)
            {
                return;
            }

            if (IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            await DeleteCurrentSelectionAsync();
        }

        private async void BoardPage_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete && e.Key != Key.Back)
            {
                return;
            }

            if (IsEditingTextOnBoard())
            {
                return;
            }

            if (_resizeTarget == null && _multiSelectedElements.Count == 0)
            {
                return;
            }

            await TryDeleteSelectionFromKeyboardAsync();
            e.Handled = true;
        }

        private async void HostWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete && e.Key != Key.Back)
            {
                return;
            }

            if (!IsLoaded || _isPageUnloading)
            {
                return;
            }

            if (IsEditingTextOnBoard())
            {
                return;
            }

            if (_resizeTarget == null && _multiSelectedElements.Count == 0)
            {
                return;
            }

            await TryDeleteSelectionFromKeyboardAsync();
            e.Handled = true;
        }

        private void TryCommitBoardTextFocus(MouseButtonEventArgs e)
        {
            if (_focusedBoardTextEdit == null)
            {
                return;
            }

            var pos = e.GetPosition(Viewport);
            var hit = VisualTreeHelper.HitTest(Viewport, pos)?.VisualHit as DependencyObject;
            for (var d = hit; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, _focusedBoardTextEdit))
                {
                    return;
                }
            }

            var edit = _focusedBoardTextEdit;
            var scope = FocusManager.GetFocusScope(edit);
            if (scope != null)
            {
                FocusManager.SetFocusedElement(scope, null);
            }

            Keyboard.ClearFocus();
            _focusedBoardTextEdit = null;
        }

        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            TryCommitBoardTextFocus(e);
            Viewport.Focus();

            if (e.ChangedButton == MouseButton.Middle)
            {
                StartPan(e.GetPosition(Viewport));
                e.Handled = true;
                return;
            }

            if (IsBoardEditLockedForCurrentUser())
            {
                if (e.LeftButton == MouseButtonState.Pressed)
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

            var screen = e.GetPosition(Viewport);
            var world = ScreenToWorld(screen);

            if (_isDraggingElement)
                return;

            if (_tool == ToolMode.Hand && e.LeftButton == MouseButtonState.Pressed)
            {
                StartPan(screen);
                return;
            }

            if (_tool == ToolMode.Arrow && e.LeftButton == MouseButtonState.Pressed)
            {
                RemoveResizeFrame();
                _connectorPortStartShapeId = null;
                _connectorPortStartSide = null;
                _connectorAnchorWorld = world;
                _isDrawingConnector = true;
                _connectorPreviewLine = new Polyline
                {
                    Stroke = GetBrushFromColor(_currentStrokeHex),
                    StrokeThickness = ConnectorStrokeThickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Triangle,
                    Fill = Brushes.Transparent
                };
                _connectorPreviewLine.Points.Add(world);
                _connectorPreviewLine.Points.Add(world);
                BoardCanvas.Children.Add(_connectorPreviewLine);
                ShowConnectorSnapHighlights(null);
                Viewport.CaptureMouse();
                e.Handled = true;
                return;
            }

            if (_tool == ToolMode.Select && e.LeftButton == MouseButtonState.Pressed)
            {
                if (_multiSelectedElements.Count > 1 && IsWorldPointInMultiSelection(world))
                {
                    BeginMultiSelectionDrag(world);
                    e.Handled = true;
                    return;
                }

                var hitTestResult2 = VisualTreeHelper.HitTest(Viewport, screen);
                var uiElement = hitTestResult2 != null
                    ? ResolveBoardElementFromHit(hitTestResult2.VisualHit as DependencyObject)
                    : null;

                if (uiElement != null && uiElement != BoardCanvas && uiElement != Viewport &&
                    !(uiElement is TextBox) && !IsPlacementPreview(uiElement) && IsBoardSelectableElement(uiElement))
                {
                    if (_multiSelectedElements.Count > 1 && _multiSelectedElements.Contains(uiElement))
                    {
                        BeginMultiSelectionDrag(world);
                        e.Handled = true;
                        return;
                    }

                    ClearMultiSelection();
                    ShowResizeFrame(uiElement);

                    if (IsBoardDraggableElement(uiElement))
                    {
                        var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == uiElement.Uid);
                        CaptureBoardStateForUndo();
                        _isDraggingElement = true;
                        _dragElement = uiElement;
                        _dragLastWorld = world;

                        if (shape?.Type == "connector")
                        {
                            _connectorDragStartWorld = world;
                            _connectorDetachedForDrag = false;
                            Viewport.CaptureMouse();
                        }
                        else
                        {
                            double left = Canvas.GetLeft(uiElement);
                            double top = Canvas.GetTop(uiElement);
                            if (double.IsNaN(left)) left = 0;
                            if (double.IsNaN(top)) top = 0;
                            _dragOffsetWorld = new Point(world.X - left, world.Y - top);
                            Viewport.CaptureMouse();
                        }
                    }

                    return;
                }

                RemoveResizeFrame();
                ClearMultiSelection();
                _isMarqueeSelecting = true;
                _marqueeStartWorld = world;
                _marqueeRect = new Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0x2E, 0x90, 0xFF)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    Fill = new SolidColorBrush(Color.FromArgb(40, 0x2E, 0x90, 0xFF)),
                    IsHitTestVisible = false
                };
                UpdateMarqueeRect(world);
                BoardCanvas.Children.Add(_marqueeRect);
                Viewport.CaptureMouse();
                return;
            }

            if (_tool == ToolMode.Pen && e.LeftButton == MouseButtonState.Pressed)
            {
                StartStroke(world, isMarker: false);
                return;
            }

            if (_tool == ToolMode.Marker && e.LeftButton == MouseButtonState.Pressed)
            {
                StartStroke(world, isMarker: true);
                return;
            }

            if (_tool == ToolMode.Eraser && e.LeftButton == MouseButtonState.Pressed)
            {
                _isErasing = true;
                CaptureBoardStateForUndo();
                StartEraserTrail(world);
                TryEraseAt(world);
                Viewport.CaptureMouse();
                e.Handled = true;
                return;
            }

            if (_tool == ToolMode.Comment && e.LeftButton == MouseButtonState.Pressed)
            {
                ShowCommentComposerAt(world);
                e.Handled = true;
                return;
            }

            if ((_tool == ToolMode.StickyNote) && e.LeftButton == MouseButtonState.Pressed)
            {
                RemoveResizeFrame();
                _isPlacingSticky = true;
                _stickyPlacementStartWorld = world;
                EnsurePreviewSticky();
                Viewport.CaptureMouse();
                e.Handled = true;
                return;
            }

            if ((_tool == ToolMode.Shape) && e.LeftButton == MouseButtonState.Pressed)
            {
                RemoveResizeFrame();
                _isPlacingRectEllipse = true;
                _rectPlacementStartWorld = world;
                EnsurePreviewShape();
                Viewport.CaptureMouse();
                e.Handled = true;
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
            if (_tool != ToolMode.Select)
            {
                e.Handled = true;
                return;
            }

            var screen = e.GetPosition(BoardCanvas);
            var world = ScreenToWorld(e.GetPosition(Viewport));

            if (_multiSelectedElements.Count > 1 && IsWorldPointInMultiSelection(world))
            {
                return;
            }

            var hitTestResult = VisualTreeHelper.HitTest(BoardCanvas, screen);
            if (hitTestResult != null)
            {
                var uiElement = ResolveBoardElementFromHit(hitTestResult.VisualHit as DependencyObject);

                if (uiElement != null && uiElement != BoardCanvas)
                {
                    if (_multiSelectedElements.Count > 1 && _multiSelectedElements.Contains(uiElement))
                    {
                        return;
                    }

                    if (uiElement is TextBox || IsBoardSelectableElement(uiElement))
                    {
                        ClearMultiSelection();
                        ShowResizeFrame(uiElement);
                        e.Handled = true;
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

            var pointerDown = e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed;
            if (!pointerDown &&
                (_isPanning || _isResizing || _isDraggingElement || _isPlacingSticky || _isPlacingRectEllipse ||
                 _isDrawingConnector || _isDrawing || _isErasing || _isDraggingConnectorEndpoint || _isMarqueeSelecting))
            {
                _ = ProcessViewportMouseUpAsync(e.GetPosition(Viewport));
                return;
            }

            if (_isDrawingConnector && _connectorPreviewLine != null &&
                e.LeftButton == MouseButtonState.Pressed)
            {
                var worldMove = ScreenToWorld(screen);
                _connectorPreviewLine.Points.Clear();
                if (_connectorPortStartShapeId.HasValue
                    && !string.IsNullOrEmpty(_connectorPortStartSide))
                {
                    var startShape = _shapesOnBoard.FirstOrDefault(s => s.Id == _connectorPortStartShapeId.Value);
                    if (startShape != null)
                    {
                        var anchor = ConnectorAttachmentHelper.GetAnchorWorldPoint(startShape, _connectorPortStartSide);
                        foreach (var p in ConnectorAttachmentHelper.ComputeOrthogonalRouteToFreePoint(
                                     anchor, worldMove, _connectorPortStartSide))
                        {
                            _connectorPreviewLine.Points.Add(p);
                        }
                    }
                    else
                    {
                        _connectorPreviewLine.Points.Add(_connectorAnchorWorld);
                        _connectorPreviewLine.Points.Add(worldMove);
                    }
                }
                else
                {
                    _connectorPreviewLine.Points.Add(_connectorAnchorWorld);
                    _connectorPreviewLine.Points.Add(worldMove);
                }

                return;
            }

            if (_isResizing && _resizeTarget != null && e.LeftButton == MouseButtonState.Pressed)
            {
                ResizeElement(world);
                return;
            }

            UpdatePreview(world, e.LeftButton == MouseButtonState.Pressed);

            if (_isDrawing && _currentStroke != null && e.LeftButton == MouseButtonState.Pressed)
            {
                _currentStroke.Points.Add(world);
                var shape = _shapesOnBoard.Find(s => s.Id.ToString() == _currentStroke.Uid);
                if (shape != null)
                {
                    shape.DeserializedPoints.Add(world);
                }
            }

            if (_isErasing && e.LeftButton == MouseButtonState.Pressed)
            {
                AppendEraserTrail(world);
                TryEraseAt(world);
            }

            if (_isDraggingConnectorEndpoint && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateConnectorEndpointDrag(world, e.GetPosition(Viewport));
            }

            if (_isDraggingElement && _dragElement != null && e.LeftButton == MouseButtonState.Pressed)
            {
                MoveElementTo(world);
            }

            if (_isPanning && (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed))
            {
                BoardTranslate.X = _panStartX + (screen.X - _panStartScreen.X);
                BoardTranslate.Y = _panStartY + (screen.Y - _panStartScreen.Y);
            }

            if (_isMarqueeSelecting && _marqueeRect != null)
            {
                UpdateMarqueeRect(world);
                return;
            }

            if (SelectionToolbarPanel?.Visibility == Visibility.Visible &&
                (_resizeBorder != null || _resizeTarget != null))
            {
                UpdateSelectionToolbarPosition();
            }
        }

        private void HostWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle || IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            if (_tool != ToolMode.Select && _tool != ToolMode.Hand)
            {
                return;
            }

            StartPan(e.GetPosition(Viewport));
            e.Handled = true;
        }

        private async void HostWindow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!ReferenceEquals(Mouse.Captured, Viewport))
            {
                return;
            }

            var pos = e.GetPosition(Viewport);

            if (_isDraggingElement && _dragElement is TextBox tb)
            {
                _isDraggingElement = false;
                _dragElement = null;
                tb.IsReadOnly = false;
                tb.Cursor = Cursors.IBeam;
                Viewport.ReleaseMouseCapture();
                SaveTextBoxPosition(tb);
                return;
            }

            await ProcessViewportMouseUpAsync(pos);
        }

        private async Task ProcessViewportMouseUpAsync(Point viewportPos)
        {
            if (_processingViewportMouseUp)
            {
                return;
            }

            _processingViewportMouseUp = true;
            try
            {
                await ProcessViewportMouseUpCoreAsync(viewportPos);
            }
            finally
            {
                _processingViewportMouseUp = false;
            }
        }

        private async Task ProcessViewportMouseUpCoreAsync(Point viewportPos)
        {
            if (_isMarqueeSelecting)
            {
                _isMarqueeSelecting = false;
                var endWorld = ScreenToWorld(viewportPos);
                FinalizeMarqueeSelection(endWorld);
                if (_marqueeRect != null)
                {
                    BoardCanvas.Children.Remove(_marqueeRect);
                    _marqueeRect = null;
                }

                Viewport.ReleaseMouseCapture();
                return;
            }

            if (!_isDrawingConnector && !_isPlacingSticky && !_isPlacingRectEllipse && !_isResizing && !_isDraggingElement &&
                !_isDrawing && !_isPanning && !_isErasing && !_isDraggingConnectorEndpoint && !_isMarqueeSelecting)
            {
                Viewport.ReleaseMouseCapture();
                return;
            }

            if (_isDraggingConnectorEndpoint)
            {
                _isDraggingConnectorEndpoint = false;
                _connectorEndpointDragWhich = null;
                Viewport.ReleaseMouseCapture();
                var endWorld = ScreenToWorld(viewportPos);
                await FinalizeConnectorEndpointDragAsync(endWorld, viewportPos);
                return;
            }

            if (_isErasing)
            {
                _isErasing = false;
                RemoveEraserTrail();
                Viewport.ReleaseMouseCapture();
                return;
            }

            if (_isDrawingConnector)
            {
                _isDrawingConnector = false;
                ClearConnectorSnapHighlights();
                Viewport.ReleaseMouseCapture();
                var endWorld = ScreenToWorld(viewportPos);
                await FinalizeConnectorFromDragAsync(endWorld, viewportPos);
                return;
            }

            if (_isPlacingSticky && (_tool == ToolMode.StickyNote))
            {
                _isPlacingSticky = false;
                Viewport.ReleaseMouseCapture();
                var endWorld = ScreenToWorld(viewportPos);
                await FinalizeStickyFromDragAsync(endWorld);
                RemovePreviewSticky();
                if (_tool == ToolMode.StickyNote)
                {
                    EnsurePreviewSticky();
                }

                return;
            }

            if (_isPlacingRectEllipse && (_tool == ToolMode.Shape))
            {
                _isPlacingRectEllipse = false;
                Viewport.ReleaseMouseCapture();
                var endWorld = ScreenToWorld(viewportPos);
                await FinalizeRectEllipseFromDragAsync(endWorld);
                RemovePreviewShape();
                if (_tool == ToolMode.Shape)
                {
                    EnsurePreviewShape();
                }

                return;
            }

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
                if (_multiSelectedElements.Count > 1)
                {
                    await SaveMultiSelectionPositionsAsync();
                }
                else if (_dragElement != null)
                {
                    await SaveElementPositionAsync(_dragElement);
                }

                _isDraggingElement = false;
                _dragElement = null;
                _multiDragSnapshots.Clear();
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

        private async void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && _isPanning)
            {
                _isPanning = false;
                Viewport.ReleaseMouseCapture();
                return;
            }

            await ProcessViewportMouseUpAsync(e.GetPosition(Viewport));
        }

        private void MoveElementTo(Point world)
        {
            if (_multiSelectedElements.Count > 1 && _isDraggingElement)
            {
                MoveMultiSelectionTo(world);
                return;
            }

            if (_dragElement == null) return;

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _dragElement.Uid);

            if (shape?.Type == "connector" && _dragElement is Canvas)
            {
                if (!_connectorDetachedForDrag
                    && ConnectorAttachmentHelper.TryParse(shape.Text, out _)
                    && (Math.Abs(world.X - _connectorDragStartWorld.X) > 3
                        || Math.Abs(world.Y - _connectorDragStartWorld.Y) > 3))
                {
                    DetachConnectorForFreeDrag(shape);
                    _connectorDetachedForDrag = true;
                }

                var delta = new Point(world.X - _dragLastWorld.X, world.Y - _dragLastWorld.Y);
                _dragLastWorld = world;
                if (ConnectorVisualHelper.GetLine(_dragElement) is Polyline line)
                {
                    for (var i = 0; i < line.Points.Count; i++)
                    {
                        var p = line.Points[i];
                        line.Points[i] = new Point(p.X + delta.X, p.Y + delta.Y);
                    }

                    shape.DeserializedPoints = line.Points.Select(p => new Point(p.X, p.Y)).ToList();
                    shape.Points = JsonConvert.SerializeObject(shape.DeserializedPoints);
                    ApplyConnectorGeometryToBoardShape(shape);
                    ConnectorVisualHelper.UpdatePoints(
                        _dragElement, shape, shape.DeserializedPoints,
                        GetBrushFromColor(shape.Color), ConnectorStrokeThickness);
                }

                if (_resizeBorder != null)
                {
                    UpdateSelectionBoundsFromElement(_dragElement);
                }

                UpdateSelectionToolbarPosition();
                return;
            }

            double offsetX = world.X - _dragOffsetWorld.X;
            double offsetY = world.Y - _dragOffsetWorld.Y;

            if (shape != null)
            {
                if (_dragElement is Polyline polyline)
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
                else if (_dragElement is TextBox textBox)
                {
                    shape.X = offsetX;
                    shape.Y = offsetY;
                }
                else if (_dragElement is Image image)
                {
                    shape.X = offsetX + image.ActualWidth / 2;
                    shape.Y = offsetY + image.ActualHeight / 2;
                }
                else if (_dragElement is Grid)
                {
                    if (shape.Type == "comment")
                    {
                        shape.X = offsetX;
                        shape.Y = offsetY;
                    }
                    else
                    {
                        shape.X = offsetX + shape.Width / 2;
                        shape.Y = offsetY + shape.Height / 2;
                    }
                }
                else if (_dragElement is Shape)
                {
                    shape.X = offsetX + shape.Width / 2;
                    shape.Y = offsetY + shape.Height / 2;
                }

                if (shape.Type is "rectangle" or "ellipse" or "stickyNote" or "image")
                {
                    RefreshConnectorVisualsReferencingShapeLocal(shape.Id);
                }
            }

            Canvas.SetLeft(_dragElement, offsetX);
            Canvas.SetTop(_dragElement, offsetY);

            if (_resizeTarget == _dragElement && _resizeBorder != null)
            {
                var dragShape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == (_dragElement as FrameworkElement)?.Uid);
                if (dragShape?.Type == "comment")
                {
                    var pad = CommentSelectionOutlinePad;
                    Canvas.SetLeft(_resizeBorder, offsetX - pad);
                    Canvas.SetTop(_resizeBorder, offsetY - pad);
                    _resizeBorder.Width = CommentPinWidth + pad * 2;
                    _resizeBorder.Height = CommentPinHeight + pad * 2;
                }
                else
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

            if (_resizeTarget == _dragElement)
            {
                UpdateSelectionToolbarPosition();
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
                else if (element is Canvas canvas && shape.Type == "connector")
                {
                    if (ConnectorVisualHelper.GetLine(canvas) is Polyline line)
                    {
                        shape.DeserializedPoints = line.Points.Select(p => new Point(p.X, p.Y)).ToList();
                        shape.Points = JsonConvert.SerializeObject(shape.DeserializedPoints);
                        ApplyConnectorGeometryToBoardShape(shape);
                    }
                }
                else if (element is Polyline polyline)
                {
                    shape.X = Canvas.GetLeft(element);
                    shape.Y = Canvas.GetTop(element);

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
                else if (element is Grid grid)
                {
                    if (shape.Type == "comment")
                    {
                        shape.X = Canvas.GetLeft(grid);
                        shape.Y = Canvas.GetTop(grid);
                    }
                    else
                    {
                        shape.X = Canvas.GetLeft(grid) + shape.Width / 2;
                        shape.Y = Canvas.GetTop(grid) + shape.Height / 2;
                    }
                }

                // Сохраняем в Supabase
                await _supabaseService.SaveShapeAsync(shape);
                MarkSaved();

                // Отправляем в Firebase для реалтайм обновлений
                PushShapeToFirebase(shape);

                if (shape.Type != "connector")
                {
                    await RefreshConnectorsReferencingShapeAsync(shape.Id);
                }
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

                await RefreshConnectorsReferencingShapeAsync(shape.Id);
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
                shape.Width = textBox.ActualWidth > 0 ? textBox.ActualWidth : textBox.Width;
                shape.Height = textBox.ActualHeight > 0 ? textBox.ActualHeight : textBox.Height;
                shape.Points = SerializeTextShapeStyle(textBox.FontSize);
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
            UpdateSelectionToolbarPosition();
        }

        private void StartPan(Point screen)
        {
            _isPanning = true;
            _panStartScreen = screen;
            _panStartX = BoardTranslate.X;
            _panStartY = BoardTranslate.Y;
            Viewport.CaptureMouse();
        }

        private async void StartStroke(Point startWorld, bool isMarker)
        {
            CaptureBoardStateForUndo();
            _isDrawing = true;

            var style = isMarker ? StrokeStyleMetadataHelper.ForMarker() : StrokeStyleMetadataHelper.ForPen();
            var thickness = style.Thickness;
            var strokeBrush = ApplyOpacityToBrush(_currentBrush, style.Opacity);

            _currentStroke = new Polyline
            {
                Stroke = strokeBrush,
                StrokeThickness = thickness,
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
                Type = isMarker ? "marker" : "line",
                X = startWorld.X,
                Y = startWorld.Y,
                Color = _currentStrokeHex,
                Text = StrokeStyleMetadataHelper.Serialize(style),
                Id = uniqueId
            };

            shape.DeserializedPoints.Add(startWorld);

            _shapesOnBoard.Add(shape);
            Viewport.CaptureMouse();
        }

        private static Brush ApplyOpacityToBrush(Brush brush, double opacity)
        {
            if (brush is SolidColorBrush scb)
            {
                var c = scb.Color;
                var a = (byte)Math.Clamp((int)(opacity * 255), 0, 255);
                return new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
            }

            return brush;
        }

        private static void ApplyStrokeStyleToPolyline(Polyline polyline, BoardShape shape, Brush baseBrush)
        {
            if (StrokeStyleMetadataHelper.TryParse(shape.Text, out var style))
            {
                polyline.StrokeThickness = style.Thickness;
                polyline.Stroke = ApplyOpacityToBrush(baseBrush, style.Opacity);
                return;
            }

            polyline.StrokeThickness = shape.Type == "marker" ? 14 : 2;
            polyline.Stroke = shape.Type == "marker"
                ? ApplyOpacityToBrush(baseBrush, 0.42)
                : baseBrush;
        }

        private void EnsurePreviewShape()
        {
            if (_previewShapeElement != null)
            {
                return;
            }

            if (_tool != ToolMode.Shape)
            {
                return;
            }

            var bs = BuildTransientPreviewShape();
            _previewShapeElement = CreateShapeBoardContainer(bs, forPreview: true);
            _previewShapeElement.Opacity = PreviewElementOpacity;
            _previewShapeElement.IsHitTestVisible = false;
            BoardCanvas.Children.Add(_previewShapeElement);
            Panel.SetZIndex(_previewShapeElement, 900);
        }

        private void RemovePreviewShape()
        {
            if (_previewShapeElement == null)
            {
                return;
            }

            BoardCanvas.Children.Remove(_previewShapeElement);
            _previewShapeElement = null;
        }

        private void EnsurePreviewSticky()
        {
            if (_previewStickyElement != null)
            {
                return;
            }

            if (_tool != ToolMode.StickyNote)
            {
                return;
            }

            var bs = BuildTransientPreviewSticky();
            _previewStickyElement = CreateStickyNoteBoardContainer(bs, forPreview: true);
            _previewStickyElement.Opacity = PreviewElementOpacity;
            _previewStickyElement.IsHitTestVisible = false;
            BoardCanvas.Children.Add(_previewStickyElement);
            Panel.SetZIndex(_previewStickyElement, 900);
        }

        private void RemovePreviewSticky()
        {
            if (_previewStickyElement == null)
            {
                return;
            }

            BoardCanvas.Children.Remove(_previewStickyElement);
            _previewStickyElement = null;
        }

        private void EnsurePreviewComment()
        {
            if (_previewCommentElement != null || _tool != ToolMode.Comment || _commentComposer != null)
            {
                return;
            }

            var (avatarFill, avatarStroke) = ResolveParticipantAvatarBrushes(_myUserId);
            var pin = new Border
            {
                Width = CommentPinWidth,
                Height = CommentPinHeight,
                Background = CommentSurfaceBrush(),
                CornerRadius = new CornerRadius(20, 20, 4, 20),
                Padding = new Thickness(4),
                IsHitTestVisible = false,
                Opacity = PreviewElementOpacity,
                Child = new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new CornerRadius(15),
                    Background = avatarFill,
                    BorderBrush = avatarStroke,
                    BorderThickness = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = GetInitials(_cursorDisplayName),
                        Foreground = avatarStroke,
                        FontWeight = FontWeights.Bold,
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
            pin.Effect = new DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 1,
                Opacity = WhiteSpaceThemeManager.IsDarkApplied ? 0.45 : 0.2,
                Color = Colors.Black
            };

            _previewCommentElement = pin;
            BoardCanvas.Children.Add(_previewCommentElement);
            Panel.SetZIndex(_previewCommentElement, 900);
        }

        private void RemovePreviewComment()
        {
            if (_previewCommentElement == null)
            {
                return;
            }

            BoardCanvas.Children.Remove(_previewCommentElement);
            _previewCommentElement = null;
        }

        private static void SetPreviewElementBounds(UIElement element, double left, double top, double width, double height)
        {
            if (element is FrameworkElement fe)
            {
                fe.Width = width;
                fe.Height = height;
            }

            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
        }

        private void UpdatePreview(Point world, bool leftButtonPressed)
        {
            if (_isPlacingSticky && _tool == ToolMode.StickyNote && _previewStickyElement != null)
            {
                if (!leftButtonPressed)
                {
                    return;
                }

                var x = Math.Min(_stickyPlacementStartWorld.X, world.X);
                var y = Math.Min(_stickyPlacementStartWorld.Y, world.Y);
                var w = Math.Max(Math.Abs(world.X - _stickyPlacementStartWorld.X), 1);
                var h = Math.Max(Math.Abs(world.Y - _stickyPlacementStartWorld.Y), 1);
                SetPreviewElementBounds(_previewStickyElement, x, y, w, h);
                return;
            }

            if (_tool == ToolMode.StickyNote && !_isPlacingSticky && _previewStickyElement != null)
            {
                SetPreviewElementBounds(
                    _previewStickyElement,
                    world.X - DefaultStickyW / 2,
                    world.Y - DefaultStickyH / 2,
                    DefaultStickyW,
                    DefaultStickyH);
            }

            if (_tool == ToolMode.Comment && _previewCommentElement != null && _commentComposer == null)
            {
                Canvas.SetLeft(_previewCommentElement, world.X);
                Canvas.SetTop(_previewCommentElement, world.Y);
            }

            if (_previewShapeElement == null)
            {
                return;
            }

            if (_isPlacingRectEllipse && _tool == ToolMode.Shape)
            {
                if (!leftButtonPressed)
                {
                    return;
                }

                var x = Math.Min(_rectPlacementStartWorld.X, world.X);
                var y = Math.Min(_rectPlacementStartWorld.Y, world.Y);
                var w = Math.Max(Math.Abs(world.X - _rectPlacementStartWorld.X), 1);
                var h = Math.Max(Math.Abs(world.Y - _rectPlacementStartWorld.Y), 1);
                SetPreviewElementBounds(_previewShapeElement, x, y, w, h);
                return;
            }

            if (_tool == ToolMode.Shape)
            {
                var circleLike = _shapeKind == "circle";
                var pw = circleLike ? DefaultEllipse : DefaultRectW;
                var ph = circleLike ? DefaultEllipse : DefaultRectH;
                SetPreviewElementBounds(
                    _previewShapeElement,
                    world.X - pw / 2,
                    world.Y - ph / 2,
                    pw,
                    ph);
            }
        }

        /// <summary>Завершение создания прямоугольника/эллипса перетаскиванием (габарит по двум углам).</summary>
        private async Task FinalizeRectEllipseFromDragAsync(Point endWorld)
        {
            if (_isCreatingShape)
            {
                return;
            }

            if (_tool != ToolMode.Shape)
            {
                return;
            }

            double x0 = _rectPlacementStartWorld.X;
            double y0 = _rectPlacementStartWorld.Y;
            double x1 = endWorld.X;
            double y1 = endWorld.Y;
            double minX = Math.Min(x0, x1);
            double minY = Math.Min(y0, y1);
            double w = Math.Max(Math.Abs(x1 - x0), 1);
            double h = Math.Max(Math.Abs(y1 - y0), 1);

            if (w < 8 && h < 8)
            {
                if (_shapeKind == "circle")
                {
                    w = DefaultEllipse;
                    h = DefaultEllipse;
                }
                else
                {
                    w = DefaultRectW;
                    h = DefaultRectH;
                }

                minX = x0 - w / 2;
                minY = y0 - h / 2;
            }

            w = Math.Max(w, MinRectEllipseWidth);
            h = Math.Max(h, MinRectEllipseHeight);

            double cx = minX + w / 2;
            double cy = minY + h / 2;

            _isCreatingShape = true;
            try
            {
                CaptureBoardStateForUndo();
                int uniqueId = await _supabaseService.GenerateUniqueIdAsync(_boardId);

                var (dbType, kindForJson) = ShapePalette.ResolveStorage(_shapeKind);
                var shape = new BoardShape
                {
                    BoardId = _boardId,
                    Type = dbType,
                    X = cx,
                    Y = cy,
                    Width = w,
                    Height = h,
                    Color = _currentStrokeHex,
                    Text = "",
                    Id = uniqueId
                };

                var appearance = new RectEllipseAppearance { Mode = _nextRectFillMode };
                if (kindForJson != null)
                {
                    appearance.ShapeKind = kindForJson;
                }

                appearance.SaveTo(shape);

                _shapesOnBoard.Add(shape);

                UIElement element = CreateShapeBoardContainer(shape);

                if (element != null)
                {
                    Canvas.SetLeft(element, cx - w / 2);
                    Canvas.SetTop(element, cy - h / 2);

                    BoardCanvas.Children.Add(element);

                    await _supabaseService.SaveShapeAsync(shape);
                    MarkSaved();

                    PushShapeToFirebase(shape);

                    BoardCanvas.UpdateLayout();
                    ShowResizeFrame(element);
                    SetTool(ToolMode.Select);
                }
            }
            finally
            {
                _isCreatingShape = false;
            }
        }

        private async Task FinalizeStickyFromDragAsync(Point endWorld)
        {
            if (_isCreatingShape)
            {
                return;
            }

            if (_tool != ToolMode.StickyNote)
            {
                return;
            }

            if (IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            double x0 = _stickyPlacementStartWorld.X;
            double y0 = _stickyPlacementStartWorld.Y;
            double x1 = endWorld.X;
            double y1 = endWorld.Y;
            double minX = Math.Min(x0, x1);
            double minY = Math.Min(y0, y1);
            double w = Math.Max(Math.Abs(x1 - x0), 1);
            double h = Math.Max(Math.Abs(y1 - y0), 1);

            if (w < 8 && h < 8)
            {
                w = DefaultStickyW;
                h = DefaultStickyH;
                minX = x0 - w / 2;
                minY = y0 - h / 2;
            }

            w = Math.Max(w, MinStickyWidth);
            h = Math.Max(h, MinStickyHeight);

            double cx = minX + w / 2;
            double cy = minY + h / 2;

            _isCreatingShape = true;
            try
            {
                CaptureBoardStateForUndo();
                int uniqueId = await _supabaseService.GenerateUniqueIdAsync(_boardId);

                var paperHex = NormalizeColorKey(StickyNoteAppearance.DefaultPaperHex);
                if (string.IsNullOrWhiteSpace(paperHex))
                {
                    paperHex = StickyNoteAppearance.DefaultPaperHex;
                }

                var meta = new StickyNoteAppearance
                {
                    Author = string.IsNullOrWhiteSpace(_cursorDisplayName) ? "УЧАСТНИК" : _cursorDisplayName.Trim(),
                    PaperHex = paperHex
                };

                var shape = new BoardShape
                {
                    BoardId = _boardId,
                    Type = "stickyNote",
                    X = cx,
                    Y = cy,
                    Width = w,
                    Height = h,
                    Color = paperHex,
                    Text = "",
                    Id = uniqueId
                };
                meta.SaveTo(shape);

                _shapesOnBoard.Add(shape);
                var el = CreateStickyNoteBoardContainer(shape);
                Canvas.SetLeft(el, cx - w / 2);
                Canvas.SetTop(el, cy - h / 2);
                BoardCanvas.Children.Add(el);

                await _supabaseService.SaveShapeAsync(shape);
                MarkSaved();
                PushShapeToFirebase(shape);

                BoardCanvas.UpdateLayout();
                ShowResizeFrame(el);
                SetTool(ToolMode.Select);
            }
            finally
            {
                _isCreatingShape = false;
            }
        }

        private async Task FinalizeConnectorFromDragAsync(Point endWorld, Point viewportPos)
        {
            if (_connectorPreviewLine != null)
            {
                BoardCanvas.Children.Remove(_connectorPreviewLine);
                _connectorPreviewLine = null;
            }

            var attachment = new ConnectorAttachment();
            var startWorld = _connectorAnchorWorld;

            if (_connectorPortStartShapeId.HasValue && !string.IsNullOrEmpty(_connectorPortStartSide))
            {
                attachment.StartShapeId = _connectorPortStartShapeId.Value;
                attachment.StartSide = _connectorPortStartSide;
                var startShape = _shapesOnBoard.FirstOrDefault(s => s.Id == attachment.StartShapeId.Value);
                if (startShape != null)
                {
                    startWorld = ConnectorAttachmentHelper.GetAnchorWorldPoint(startShape, attachment.StartSide);
                }
            }

            if (TryGetConnectorPortAtViewport(viewportPos, out var endPortShapeId, out var endPortSide))
            {
                attachment.EndShapeId = endPortShapeId;
                attachment.EndSide = endPortSide;
            }
            else if (ConnectorAttachmentHelper.TryFindNearestPort(
                         endWorld,
                         _shapesOnBoard,
                         attachment.StartShapeId,
                         out var nearShapeId,
                         out var nearSide))
            {
                attachment.EndShapeId = nearShapeId;
                attachment.EndSide = nearSide;
            }

            _connectorPortStartShapeId = null;
            _connectorPortStartSide = null;

            var p0 = startWorld;
            var p1 = endWorld;
            if (attachment.StartShapeId.HasValue)
            {
                var s0 = _shapesOnBoard.FirstOrDefault(x => x.Id == attachment.StartShapeId.Value);
                if (s0 != null)
                {
                    p0 = ConnectorAttachmentHelper.GetAnchorWorldPoint(s0, attachment.StartSide);
                }
            }

            if (attachment.EndShapeId.HasValue)
            {
                var s1 = _shapesOnBoard.FirstOrDefault(x => x.Id == attachment.EndShapeId.Value);
                if (s1 != null)
                {
                    p1 = ConnectorAttachmentHelper.GetAnchorWorldPoint(s1, attachment.EndSide);
                }
            }

            if (Math.Abs(p0.X - p1.X) < 6 && Math.Abs(p0.Y - p1.Y) < 6)
            {
                return;
            }

            if (IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            CaptureBoardStateForUndo();

            _isCreatingShape = true;
            try
            {
                var uniqueId = await _supabaseService.GenerateUniqueIdAsync(_boardId);
                var shape = new BoardShape
                {
                    BoardId = _boardId,
                    Type = "connector",
                    Color = _currentStrokeHex,
                    Points = JsonConvert.SerializeObject(new List<Point> { p0, p1 }),
                    Id = uniqueId,
                    Text = attachment.HasAnyAttachment
                        ? ConnectorAttachmentHelper.SerializeForStorage(attachment)
                        : ""
                };

                ApplyConnectorGeometryToBoardShape(shape);

                _shapesOnBoard.Add(shape);
                AddShapeToCanvas(shape, false);

                await _supabaseService.SaveShapeAsync(shape);
                MarkSaved();
                PushShapeToFirebase(shape);
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

            const double initialFs = 16;
            var tb = new TextBox
            {
                Text = "Текст",
                MinWidth = 48,
                MinHeight = 28,
                Width = 220,
                Height = 88,
                FontSize = initialFs,
                Foreground = _currentBrush,
                IsReadOnly = false,
                Focusable = true,
                Cursor = Cursors.IBeam,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(1, 2, 1, 2)
            };

            ApplyTextBoxChrome(tb, _currentBrush);

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
                Width = tb.Width,
                Height = tb.Height,
                Text = tb.Text,
                Color = _currentStrokeHex,
                Points = SerializeTextShapeStyle(initialFs),
                Id = uniqueId
            };

            _shapesOnBoard.Add(shape);
            await _supabaseService.SaveShapeAsync(shape);
            MarkSaved();

            // Отправляем в Firebase для реалтайм обновлений
            PushShapeToFirebase(shape);

            BoardCanvas.UpdateLayout();
            ShowResizeFrame(tb);
            SetTool(ToolMode.Select);

            _isCreatingShape = false;
        }

        private void RegisterSelectionChrome(UIElement element, int zIndex = 15000)
        {
            if (element is FrameworkElement fe && fe.Tag == null)
            {
                fe.Tag = BoardSelectionChromeTag;
            }

            if (!_selectionChromeElements.Contains(element))
            {
                _selectionChromeElements.Add(element);
            }

            if (!BoardCanvas.Children.Contains(element))
            {
                BoardCanvas.Children.Add(element);
            }

            Panel.SetZIndex(element, zIndex);
        }

        private void DetachSelectionChromeEvents(UIElement element)
        {
            if (element is Ellipse ell)
            {
                var tag = ell.Tag as string;
                if (tag != null && tag.StartsWith("port:", StringComparison.Ordinal))
                {
                    ell.MouseLeftButtonDown -= ConnectorPort_MouseDown;
                }
                else if (tag != null && tag.StartsWith("conn-end:", StringComparison.Ordinal))
                {
                    ell.MouseLeftButtonDown -= ConnectorEndpoint_MouseDown;
                }
            }
            else if (element is Rectangle rect)
            {
                rect.MouseDown -= ResizeHandle_MouseDown;
                rect.MouseLeftButtonDown -= GroupSelectionFrame_MouseLeftButtonDown;
            }
        }

        private static bool IsSelectionPortEllipse(Ellipse ell) =>
            Math.Abs(ell.Width - 12) < 0.5
            && Math.Abs(ell.Height - 12) < 0.5
            && ell.Stroke is SolidColorBrush scb
            && scb.Color == SelectionPortStrokeColor;

        private void ClearAllSelectionChrome()
        {
            foreach (var element in _selectionChromeElements.ToList())
            {
                DetachSelectionChromeEvents(element);
                if (BoardCanvas.Children.Contains(element))
                {
                    BoardCanvas.Children.Remove(element);
                }
            }

            _selectionChromeElements.Clear();
            _anchorPortElements.Clear();
            _resizeHandles.Clear();

            foreach (var endpoint in _connectorEndpointElements.ToList())
            {
                endpoint.MouseLeftButtonDown -= ConnectorEndpoint_MouseDown;
                if (BoardCanvas.Children.Contains(endpoint))
                {
                    BoardCanvas.Children.Remove(endpoint);
                }
            }

            _connectorEndpointElements.Clear();
            ClearConnectorSnapHighlights();
            _resizeBorder = null;
        }

        private void PurgeOrphanSelectionChromeFromCanvas()
        {
            ClearAllSelectionChrome();

            foreach (var child in BoardCanvas.Children.OfType<FrameworkElement>().ToList())
            {
                if (child.Tag is string tag
                    && (tag == BoardSelectionChromeTag
                        || tag == SelectionFrameTag
                        || tag.StartsWith("port:", StringComparison.Ordinal)
                        || tag.StartsWith("conn-end:", StringComparison.Ordinal)
                        || ResizeHandleTags.Contains(tag, StringComparer.Ordinal)))
                {
                    DetachSelectionChromeEvents(child);
                    BoardCanvas.Children.Remove(child);
                    continue;
                }

                if (child is Ellipse ell && IsSelectionPortEllipse(ell))
                {
                    ell.MouseLeftButtonDown -= ConnectorPort_MouseDown;
                    BoardCanvas.Children.Remove(ell);
                }
            }
        }

        // РУЧКИ ИЗМЕНЕНИЯ РАЗМЕРА
        private void ShowResizeFrame(UIElement element)
        {
            RemoveResizeFrame();
            ColorPanel.Visibility = Visibility.Visible;

            _resizeTarget = element;

            var selectedShape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == (element as FrameworkElement)?.Uid);
            var isConnector = selectedShape?.Type == "connector";

            double left, top, width, height;

            if (selectedShape?.Type == "comment")
            {
                left = Canvas.GetLeft(element);
                top = Canvas.GetTop(element);
                if (double.IsNaN(left))
                {
                    left = selectedShape.X;
                }

                if (double.IsNaN(top))
                {
                    top = selectedShape.Y;
                }

                ShowCommentSelectionOutline(left, top, CommentPinWidth, CommentPinHeight);
                SyncSelectionToolbar(element);
                UpdateSelectionToolbarPosition();
                return;
            }

            if (ConnectorVisualHelper.GetLine(element) is Polyline connectorLine && connectorLine.Points.Count > 0)
            {
                double minX = connectorLine.Points.Min(p => p.X);
                double maxX = connectorLine.Points.Max(p => p.X);
                double minY = connectorLine.Points.Min(p => p.Y);
                double maxY = connectorLine.Points.Max(p => p.Y);
                left = minX;
                top = minY;
                width = Math.Max(maxX - minX, 1);
                height = Math.Max(maxY - minY, 1);
            }
            else if (element is Polyline polyline)
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
                var fe = (FrameworkElement)element;
                width = fe.ActualWidth;
                height = fe.ActualHeight;
                if (width < 1 || double.IsNaN(width))
                {
                    var dw = fe.Width;
                    if (!double.IsNaN(dw) && dw > 0)
                    {
                        width = dw;
                    }
                }

                if (height < 1 || double.IsNaN(height))
                {
                    var dh = fe.Height;
                    if (!double.IsNaN(dh) && dh > 0)
                    {
                        height = dh;
                    }
                }

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
                IsHitTestVisible = false,
                Tag = SelectionFrameTag
            };

            Canvas.SetLeft(_resizeBorder, left);
            Canvas.SetTop(_resizeBorder, top);

            RegisterSelectionChrome(_resizeBorder);

            if (!isConnector)
            {
                CreateResizeHandles(left, top, width, height);
            }

            TryAddConnectorAnchorPorts(element, left, top, width, height);
            TryAddConnectorEndpointHandles(element);
            SyncSelectionToolbar(element);
            UpdateSelectionToolbarPosition();
        }

        private void RemoveResizeFrame()
        {
            if (_resizeBorder != null)
            {
                _resizeBorder.MouseLeftButtonDown -= GroupSelectionFrame_MouseLeftButtonDown;
            }

            PurgeOrphanSelectionChromeFromCanvas();
            _resizeTarget = null;
            ColorPanel.Visibility = Visibility.Visible;
            HideSelectionToolbar();
        }

        private void RemoveAnchorPorts()
        {
            foreach (var p in _anchorPortElements.ToList())
            {
                p.MouseLeftButtonDown -= ConnectorPort_MouseDown;
                _selectionChromeElements.Remove(p);
                BoardCanvas.Children.Remove(p);
            }

            _anchorPortElements.Clear();
        }

        private void TryAddConnectorAnchorPorts(UIElement target, double left, double top, double w, double h)
        {
            RemoveAnchorPorts();

            if (_tool != ToolMode.Select || HasMultiSelection())
            {
                return;
            }

            if (string.IsNullOrEmpty(target.Uid))
            {
                return;
            }

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == target.Uid);
            if (shape == null || shape.Type is not ("rectangle" or "ellipse" or "stickyNote"))
            {
                return;
            }

            if (IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            var positions = new (double X, double Y)[]
            {
                (left + w / 2, top),
                (left + w, top + h / 2),
                (left + w / 2, top + h),
                (left, top + h / 2),
            };

            var sides = new[] { "n", "e", "s", "w" };

            for (var i = 0; i < 4; i++)
            {
                var el = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = Brushes.White,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x2E, 0x90, 0xFF)),
                    StrokeThickness = 2,
                    Cursor = Cursors.Cross,
                    Tag = $"port:{target.Uid}:{sides[i]}",
                    IsHitTestVisible = true
                };
                el.MouseLeftButtonDown += ConnectorPort_MouseDown;
                Canvas.SetLeft(el, positions[i].X - 6);
                Canvas.SetTop(el, positions[i].Y - 6);
                RegisterSelectionChrome(el);
                _anchorPortElements.Add(el);
            }
        }

        private void UpdateAnchorPortsPositions(double x, double y, double w, double h)
        {
            if (_anchorPortElements.Count != 4)
            {
                return;
            }

            var positions = new (double X, double Y)[]
            {
                (x + w / 2, y),
                (x + w, y + h / 2),
                (x + w / 2, y + h),
                (x, y + h / 2),
            };

            for (var i = 0; i < 4; i++)
            {
                Canvas.SetLeft(_anchorPortElements[i], positions[i].X - 6);
                Canvas.SetTop(_anchorPortElements[i], positions[i].Y - 6);
            }
        }

        private void ConnectorPort_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Ellipse el || el.Tag is not string tag || !tag.StartsWith("port:", StringComparison.Ordinal))
            {
                return;
            }

            if (IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            e.Handled = true;

            _connectorPortStartShapeId = null;
            _connectorPortStartSide = null;
            var tagParts = tag.Split(':');
            if (tagParts.Length >= 3 && int.TryParse(tagParts[1], out var portShapeId))
            {
                _connectorPortStartShapeId = portShapeId;
                _connectorPortStartSide = tagParts[2];
                var portShape = _shapesOnBoard.FirstOrDefault(s => s.Id == portShapeId);
                _connectorAnchorWorld = portShape != null
                    ? ConnectorAttachmentHelper.GetAnchorWorldPoint(portShape, tagParts[2])
                    : ScreenToWorld(e.GetPosition(Viewport));
            }
            else
            {
                _connectorAnchorWorld = ScreenToWorld(e.GetPosition(Viewport));
            }

            _isDrawingConnector = true;
            _connectorPreviewLine = new Polyline
            {
                Stroke = GetBrushFromColor(_currentStrokeHex),
                StrokeThickness = ConnectorStrokeThickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Triangle,
                StrokeMiterLimit = 1,
                Fill = Brushes.Transparent,
                SnapsToDevicePixels = true
            };
            _connectorPreviewLine.Points.Add(_connectorAnchorWorld);
            _connectorPreviewLine.Points.Add(_connectorAnchorWorld);
            BoardCanvas.Children.Add(_connectorPreviewLine);
            ShowConnectorSnapHighlights(_connectorPortStartShapeId);
            Viewport.CaptureMouse();
        }

        private void ShowConnectorSnapHighlights(int? excludeShapeId)
        {
            ClearConnectorSnapHighlights();

            foreach (var shape in _shapesOnBoard)
            {
                if (shape.Type is not ("rectangle" or "ellipse" or "stickyNote"))
                {
                    continue;
                }

                foreach (var side in new[] { "n", "e", "s", "w" })
                {
                    var world = ConnectorAttachmentHelper.GetAnchorWorldPoint(shape, side);
                    var el = new Ellipse
                    {
                        Width = 18,
                        Height = 18,
                        Fill = new SolidColorBrush(Color.FromArgb(220, 0x8B, 0x5C, 0xF6)),
                        Stroke = Brushes.White,
                        StrokeThickness = 2.5,
                        IsHitTestVisible = false,
                        Opacity = shape.Id == excludeShapeId ? 0.55 : 1
                    };
                    Canvas.SetLeft(el, world.X - 9);
                    Canvas.SetTop(el, world.Y - 9);
                    BoardCanvas.Children.Add(el);
                    Panel.SetZIndex(el, 1000);
                    _snapHighlightPortElements.Add(el);
                }
            }
        }

        private void ClearConnectorSnapHighlights()
        {
            foreach (var el in _snapHighlightPortElements)
            {
                BoardCanvas.Children.Remove(el);
            }

            _snapHighlightPortElements.Clear();
        }

        private void RemoveAllHandles()
        {
            foreach (var handle in _resizeHandles.Values.ToList())
            {
                if (handle == null)
                {
                    continue;
                }

                handle.MouseDown -= ResizeHandle_MouseDown;
                _selectionChromeElements.Remove(handle);
                BoardCanvas.Children.Remove(handle);
            }

            _resizeHandles.Clear();
        }

        private void CreateResizeHandles(double x, double y, double w, double h)
        {
            RemoveAllHandles();

            var handlePositions = new Dictionary<string, (double X, double Y)>
            {
                { "nw", (x, y) },
                { "ne", (x + w, y) },
                { "se", (x + w, y + h) },
                { "sw", (x, y + h) },
            };

            foreach (var kvp in handlePositions)
            {
                var handle = CreateHandle(kvp.Key, kvp.Value.X, kvp.Value.Y);
                _resizeHandles[kvp.Key] = handle;
                RegisterSelectionChrome(handle);
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
            UpdateSelectionToolbarPosition();
        }

        private void UpdateResizeHandles(double x, double y, double w, double h)
        {
            if (_resizeHandles.Count == 0) return;

            var handlePositions = new Dictionary<string, (double X, double Y)>
            {
                { "nw", (x, y) },
                { "ne", (x + w, y) },
                { "se", (x + w, y + h) },
                { "sw", (x, y + h) },
            };

            foreach (var kvp in handlePositions)
            {
                if (_resizeHandles.TryGetValue(kvp.Key, out var handle))
                {
                    Canvas.SetLeft(handle, kvp.Value.X - 4);
                    Canvas.SetTop(handle, kvp.Value.Y - 4);
                }
            }

            UpdateAnchorPortsPositions(x, y, w, h);
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

                if (_resizeTarget is TextBox tbStart)
                {
                    _textResizeStartFontSize = tbStart.FontSize > 0 ? tbStart.FontSize : 16;
                }
            }

            Viewport.CaptureMouse();
            e.Handled = true;
        }

        private void GetMinSizeForResizeTarget(UIElement? target, out double minW, out double minH)
        {
            minW = 16;
            minH = 16;
            if (target is not FrameworkElement fe || string.IsNullOrEmpty(fe.Uid))
            {
                return;
            }

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == fe.Uid);
            if (shape == null)
            {
                return;
            }

            switch (shape.Type)
            {
                case "stickyNote":
                    minW = MinStickyWidth;
                    minH = MinStickyHeight;
                    break;
                case "rectangle":
                case "ellipse":
                    minW = MinRectEllipseWidth;
                    minH = MinRectEllipseHeight;
                    break;
                case "text":
                    minW = 40;
                    minH = 24;
                    break;
                case "image":
                    minW = 48;
                    minH = 40;
                    break;
                default:
                    minW = 24;
                    minH = 24;
                    break;
            }
        }

        private void ResizeElement(Point world)
        {
            if (_resizeTarget == null || string.IsNullOrEmpty(_resizeDirection)) return;

            GetMinSizeForResizeTarget(_resizeTarget, out var minW, out var minH);

            double dx = world.X - _resizeStartWorld.X;
            double dy = world.Y - _resizeStartWorld.Y;

            double newX = _startX;
            double newY = _startY;
            double newW = _startW;
            double newH = _startH;

            if (_resizeDirection.Contains("e"))
            {
                newW = Math.Max(minW, _startW + dx);
            }
            if (_resizeDirection.Contains("s"))
            {
                newH = Math.Max(minH, _startH + dy);
            }
            if (_resizeDirection.Contains("w"))
            {
                double delta = dx;
                newW = Math.Max(minW, _startW - delta);
                newX = _startX + delta;
            }
            if (_resizeDirection.Contains("n"))
            {
                double delta = dy;
                newH = Math.Max(minH, _startH - delta);
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

                if (fe is TextBox tbResize && _startW > 0.01 && _startH > 0.01)
                {
                    var scale = Math.Min(newW / _startW, newH / _startH);
                    tbResize.FontSize = Math.Max(8, Math.Min(240, _textResizeStartFontSize * scale));
                    tbResize.CaretBrush = tbResize.Foreground;
                }

                if (fe is Grid resizeGrid
                    && resizeGrid.Tag is Rectangle roundInner
                    && !string.IsNullOrEmpty(fe.Uid)
                    && _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == fe.Uid) is { } rs)
                {
                    var sk = RectEllipseAppearance.Parse(rs).ShapeKind;
                    if (string.Equals(sk, "roundRect", StringComparison.Ordinal))
                    {
                        var rad = Math.Min(Math.Max(newW, 1), Math.Max(newH, 1)) * 0.15;
                        roundInner.RadiusX = rad;
                        roundInner.RadiusY = rad;
                    }
                }

                if (fe is Grid labelGrid
                    && !string.IsNullOrEmpty(labelGrid.Uid)
                    && _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == labelGrid.Uid) is { } ls
                    && ls.Type is "rectangle" or "ellipse")
                {
                    SyncShapeLabelPresenter(labelGrid, ls);
                }

                if (fe is Grid stickyGrid
                    && !string.IsNullOrEmpty(stickyGrid.Uid)
                    && _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == stickyGrid.Uid) is { } st
                    && st.Type == "stickyNote")
                {
                    SyncStickyNoteAuthorAndText(stickyGrid, st);
                }

                if (!string.IsNullOrEmpty(fe.Uid)
                    && _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == fe.Uid) is { } resizedShape)
                {
                    if (resizedShape.Type == "comment")
                    {
                        resizedShape.Width = newW;
                        resizedShape.Height = newH;
                        resizedShape.X = newX;
                        resizedShape.Y = newY;
                    }
                    else
                    {
                        resizedShape.Width = newW;
                        resizedShape.Height = newH;
                        resizedShape.X = newX + newW / 2;
                        resizedShape.Y = newY + newH / 2;
                    }

                    if (resizedShape.Type is "rectangle" or "ellipse" or "stickyNote")
                    {
                        RefreshConnectorVisualsReferencingShapeLocal(resizedShape.Id);
                    }
                }
            }

            UpdateResizeFrame(newX, newY, newW, newH);
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
            else if (target is TextBox tbRt)
            {
                shape.Width = tbRt.Width > 0 ? tbRt.Width : tbRt.ActualWidth;
                shape.Height = tbRt.Height > 0 ? tbRt.Height : tbRt.ActualHeight;
                shape.X = Canvas.GetLeft(target);
                shape.Y = Canvas.GetTop(target);
                shape.Points = SerializeTextShapeStyle(tbRt.FontSize);
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
            else if (_resizeTarget is TextBox tbSave)
            {
                shape.Width = tbSave.Width > 0 ? tbSave.Width : tbSave.ActualWidth;
                shape.Height = tbSave.Height > 0 ? tbSave.Height : tbSave.ActualHeight;
                shape.X = Canvas.GetLeft(_resizeTarget);
                shape.Y = Canvas.GetTop(_resizeTarget);
                shape.Points = SerializeTextShapeStyle(tbSave.FontSize);
            }
            else
            {
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

            if (shape.Type != "connector")
            {
                await RefreshConnectorsReferencingShapeAsync(shape.Id);
            }
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
                    var hx = NormalizeColorKey(selectedColorString);
                    if (!string.IsNullOrEmpty(hx) && new BrushConverter().ConvertFromString(hx) is Brush b)
                    {
                        _currentBrush = b;
                        _currentStrokeHex = hx;
                    }
                    else
                    {
                        _currentBrush = selectedBrush;
                        _currentStrokeHex = selectedColorString;
                    }

                    SyncPaletteGridRingHighlights(_currentFillHex, _currentStrokeHex);
                    SyncMainToolbarColorHighlight();
                    RefreshPreviewShapeIfNeeded();
                }
            }
        }

        private async void MainToolbarColorPicker_Click(object sender, RoutedEventArgs e)
        {
            if (!TryPromptColorPicker(isStrokeContour: true, out var hex))
            {
                return;
            }

            if (new BrushConverter().ConvertFromString(hex) is not Brush brush)
            {
                return;
            }

            if (_resizeTarget != null)
            {
                await ApplyColorToElement(_resizeTarget, brush, hex);
            }
            else
            {
                var norm = NormalizeColorKey(hex);
                _currentStrokeHex = string.IsNullOrEmpty(norm) ? hex : norm;
                _currentBrush = brush;
                SyncPaletteGridRingHighlights(_currentFillHex, _currentStrokeHex);
                SyncMainToolbarColorHighlight();
            }
        }

        private async Task ApplyColorToElement(UIElement element, Brush brush, string colorString)
        {
            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == element.Uid);
            if (shape == null)
            {
                return;
            }

            if (shape.Type == "stickyNote")
            {
                return;
            }

            if (string.Equals(shape.Color, colorString, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CaptureBoardStateForUndo();

            shape.Color = colorString;

            var inner = TryGetInnerShape(element);
            if (inner != null)
            {
                if (shape.Type is "rectangle" or "ellipse")
                {
                    ApplyRectEllipseVisual(inner, shape);
                }
                else
                {
                    inner.Stroke = brush;
                }
            }
            else if (element is Shape sh)
            {
                if (shape.Type is "rectangle" or "ellipse")
                {
                    ApplyRectEllipseVisual(sh, shape);
                }
                else
                {
                    sh.Stroke = brush;
                }
            }
            else if (element is Polyline polyline)
            {
                polyline.Stroke = brush;
            }
            else if (element is Canvas canvas && shape.Type == "connector")
            {
                if (ConnectorAttachmentHelper.TryParse(shape.Text, out _))
                {
                    ApplyConnectorGeometryToBoardShape(shape);
                    var pts = ConnectorAttachmentHelper.ResolveConnectorPoints(shape, _shapesOnBoard);
                    ConnectorVisualHelper.UpdatePoints(
                        canvas, shape, pts, brush, ConnectorStrokeThickness);
                }
                else
                {
                    ConnectorVisualHelper.ApplyStyle(canvas, shape, brush, ConnectorStrokeThickness);
                }
            }
            else if (element is TextBox tb)
            {
                tb.Foreground = brush;
                tb.CaretBrush = brush;
            }


            // Сохраняем в Supabase
            await _supabaseService.SaveShapeAsync(shape);
            MarkSaved();

            // Отправляем в Firebase для реалтайм обновлений
            PushShapeToFirebase(shape);

            if (shape.Type is "rectangle" or "ellipse" or "stickyNote")
            {
                RefreshConnectorVisualsReferencingShapeLocal(shape.Id);
            }
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

                var publicUrl = await _supabaseService.UploadBoardImageAsync(_boardId, uniqueId, imagePath);
                if (string.IsNullOrWhiteSpace(publicUrl))
                {
                    return;
                }

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
                    Text = publicUrl,
                    Id = uniqueId
                };

                ImageSource? prefetch = null;
                try
                {
                    var bytes = await File.ReadAllBytesAsync(imagePath);
                    prefetch = CreateImageSourceFromBytes(bytes);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Локальный превью кадр: {ex.Message}");
                }

                AddShapeToCanvas(shape, true, prefetch, prefetch == null ? imagePath : null);
                await _supabaseService.SaveShapeAsync(shape);
                MarkSaved();
                PushShapeToFirebase(shape);

                var image = FindUIElementByUid(shape.Id.ToString());
                if (image != null)
                {
                    ShowResizeFrame(image);
                    SetTool(ToolMode.Select);
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
            _currentStrokeHex = "#000000";
            _currentFillHex = "#FFFFFF";
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

        private sealed class TextShapeStyleJson
        {
            [JsonProperty("fs")]
            public double FontSize { get; set; }
        }

        private static string SerializeTextShapeStyle(double fontSize) =>
            JsonConvert.SerializeObject(new TextShapeStyleJson { FontSize = fontSize });

        private static double ParseTextShapeFontSize(string? points, double boxHeight, double fallback)
        {
            if (!string.IsNullOrWhiteSpace(points) && points.TrimStart().StartsWith('{'))
            {
                try
                {
                    var dto = JsonConvert.DeserializeObject<TextShapeStyleJson>(points);
                    if (dto != null && dto.FontSize > 0)
                    {
                        return Math.Clamp(dto.FontSize, 6, 320);
                    }
                }
                catch
                {
                    // не JSON стиля текста
                }
            }

            if (boxHeight > 1)
            {
                return Math.Clamp(boxHeight * 0.62, 10, 200);
            }

            return fallback;
        }

        private static void ApplyTextBoxChrome(TextBox tb, Brush foreground)
        {
            tb.Background = Brushes.Transparent;
            tb.BorderThickness = new Thickness(0);
            tb.BorderBrush = Brushes.Transparent;
            tb.CaretBrush = foreground;
        }

        private const string CommentExpandedTag = "commentExpanded";
        private const string CommentPinTag = "commentPin";
        private const string CommentAuthorTag = "commentAuthor";
        private const string CommentMessageTag = "commentMessage";
        private const string CommentTimeTag = "commentTime";
        private const string CommentAvatarTag = "commentAvatar";

        private const double CommentPinWidth = 40;
        private const double CommentPinHeight = 44;
        private const double CommentSelectionOutlinePad = 4;

        private static Brush ResolveThemeBrush(string key, Color fallback) =>
            Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

        private static Brush CommentSurfaceBrush() =>
            ResolveThemeBrush("WsSurfaceBrush", Color.FromRgb(0x1F, 0x29, 0x37));

        private static Brush CommentBorderBrush() =>
            ResolveThemeBrush("WsBorderBrush", Color.FromRgb(0x37, 0x41, 0x51));

        private static Brush CommentPrimaryTextBrush() =>
            ResolveThemeBrush("WsTextPrimaryBrush", Color.FromRgb(0xF9, 0xFA, 0xFB));

        private static Brush CommentSecondaryTextBrush() =>
            ResolveThemeBrush("WsTextSecondaryBrush", Color.FromRgb(0x9C, 0xA3, 0xAF));

        private static Brush CommentMutedSurfaceBrush() =>
            ResolveThemeBrush("WsSurfaceMutedBrush", Color.FromRgb(0x37, 0x41, 0x51));

        private static Brush SelectionAccentBrush() =>
            ResolveThemeBrush("WsPurpleBrush", Color.FromRgb(0x8B, 0x5C, 0xF6));

        private void ShowCommentSelectionOutline(double left, double top, double width, double height)
        {
            var pad = CommentSelectionOutlinePad;
            _resizeBorder = new Rectangle
            {
                Width = width + pad * 2,
                Height = height + pad * 2,
                RadiusX = 14,
                RadiusY = 14,
                Stroke = SelectionAccentBrush(),
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(_resizeBorder, left - pad);
            Canvas.SetTop(_resizeBorder, top - pad);
            RegisterSelectionChrome(_resizeBorder);
        }

        private static void ApplyCommentTheme(Grid grid)
        {
            var surface = CommentSurfaceBrush();
            var border = CommentBorderBrush();
            var primary = CommentPrimaryTextBrush();
            var secondary = CommentSecondaryTextBrush();

            foreach (var child in FindVisualChildren<Border>(grid))
            {
                var tag = child.Tag as string;
                if (tag == CommentPinTag || tag == CommentExpandedTag)
                {
                    child.Background = surface;
                    if (tag == CommentExpandedTag)
                    {
                        child.BorderBrush = border;
                    }
                }
            }

            foreach (var child in FindVisualChildren<TextBlock>(grid))
            {
                var tag = child.Tag as string;
                if (tag == CommentAuthorTag || tag == CommentMessageTag)
                {
                    child.Foreground = primary;
                }
                else if (tag == CommentTimeTag)
                {
                    child.Foreground = secondary;
                }
            }
        }

        private void ApplyCommentComposerTheme()
        {
            if (_commentComposer == null)
            {
                return;
            }

            _commentComposer.Background = CommentSurfaceBrush();
            _commentComposer.BorderBrush = CommentBorderBrush();

            foreach (var border in FindVisualChildren<Border>(_commentComposer))
            {
                if (border.Tag as string == CommentPinTag)
                {
                    border.Background = CommentSurfaceBrush();
                }
            }

            foreach (var input in FindVisualChildren<TextBox>(_commentComposer))
            {
                input.Foreground = CommentPrimaryTextBrush();
                input.CaretBrush = CommentPrimaryTextBrush();
            }

            foreach (var button in FindVisualChildren<Button>(_commentComposer))
            {
                button.Foreground = CommentPrimaryTextBrush();
                button.Background = CommentMutedSurfaceBrush();
            }
        }

        private Grid CreateCommentBoardContainer(BoardShape shape)
        {
            BoardCommentMetadataHelper.TryParse(shape.Text, out var meta);
            var initials = GetInitials(meta.DisplayAuthor());
            var (avatarFill, avatarStroke) = ResolveCommentAuthorAvatarBrushes(meta);

            var root = new Grid
            {
                Uid = shape.Id.ToString(),
                Width = 320,
                Height = 72,
                Background = Brushes.Transparent
            };

            var pin = new Border
            {
                Tag = CommentPinTag,
                Width = CommentPinWidth,
                Height = CommentPinHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Background = CommentSurfaceBrush(),
                CornerRadius = new CornerRadius(20, 20, 4, 20),
                Padding = new Thickness(4),
                Cursor = Cursors.Hand
            };
            pin.Effect = new DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 1,
                Opacity = WhiteSpaceThemeManager.IsDarkApplied ? 0.45 : 0.2,
                Color = Colors.Black
            };

            var avatar = new Border
            {
                Tag = CommentAvatarTag,
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = avatarFill,
                BorderBrush = avatarStroke,
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = initials,
                    Foreground = avatarStroke,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            pin.Child = avatar;

            var expanded = new Border
            {
                Tag = CommentExpandedTag,
                Visibility = Visibility.Collapsed,
                MinWidth = 220,
                MaxWidth = 280,
                Margin = new Thickness(48, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Background = CommentSurfaceBrush(),
                BorderBrush = CommentBorderBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 10, 14, 10),
                Cursor = Cursors.Arrow
            };
            expanded.Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 2,
                Opacity = WhiteSpaceThemeManager.IsDarkApplied ? 0.5 : 0.22,
                Color = Colors.Black
            };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            header.Children.Add(new TextBlock
            {
                Tag = CommentAuthorTag,
                Text = meta.DisplayAuthor(),
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = CommentPrimaryTextBrush()
            });
            header.Children.Add(new TextBlock
            {
                Tag = CommentTimeTag,
                Text = " " + BoardCommentMetadataHelper.FormatRelativeTime(meta.CreatedAtUtc),
                FontSize = 12,
                Foreground = CommentSecondaryTextBrush(),
                Margin = new Thickness(6, 1, 0, 0)
            });

            var message = new TextBlock
            {
                Tag = CommentMessageTag,
                Text = meta.Message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = CommentPrimaryTextBrush()
            };

            var body = new StackPanel();
            body.Children.Add(header);
            body.Children.Add(message);
            expanded.Child = body;

            root.Children.Add(expanded);
            root.Children.Add(pin);

            root.MouseEnter += (_, _) => expanded.Visibility = Visibility.Visible;
            root.MouseLeave += (_, _) => expanded.Visibility = Visibility.Collapsed;

            ApplyCommentTheme(root);
            return root;
        }

        private void SyncCommentVisual(Grid grid, BoardShape shape)
        {
            BoardCommentMetadataHelper.TryParse(shape.Text, out var meta);
            var (avatarFill, avatarStroke) = ResolveCommentAuthorAvatarBrushes(meta);
            foreach (var child in FindVisualChildren<Border>(grid))
            {
                if (child.Tag as string != CommentAvatarTag)
                {
                    continue;
                }

                child.Background = avatarFill;
                child.BorderBrush = avatarStroke;
                if (child.Child is TextBlock avatarText)
                {
                    avatarText.Text = GetInitials(meta.DisplayAuthor());
                    avatarText.Foreground = avatarStroke;
                }
            }

            foreach (var child in FindVisualChildren<TextBlock>(grid))
            {
                if (child.Tag as string == CommentAuthorTag)
                {
                    child.Text = meta.DisplayAuthor();
                }
                else if (child.Tag as string == CommentTimeTag)
                {
                    child.Text = " " + BoardCommentMetadataHelper.FormatRelativeTime(meta.CreatedAtUtc);
                }
                else if (child.Tag as string == CommentMessageTag)
                {
                    child.Text = meta.Message;
                }
            }

            ApplyCommentTheme(grid);
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                yield break;
            }

            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    yield return match;
                }

                foreach (var nested in FindVisualChildren<T>(child))
                {
                    yield return nested;
                }
            }
        }

        private void ShowCommentComposerAt(Point world)
        {
            if (IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            HideCommentComposer();
            RemovePreviewComment();
            _pendingCommentWorld = world;

            var input = new TextBox
            {
                MinWidth = 200,
                FontSize = 14,
                Padding = new Thickness(12, 8, 40, 8),
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(0),
                Text = ""
            };
            input.KeyDown += async (_, ke) =>
            {
                if (ke.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
                {
                    ke.Handled = true;
                    await FinalizeCommentFromComposerAsync(input.Text);
                }
            };

            var sendBtn = new Button
            {
                Width = 28,
                Height = 28,
                Content = "↑",
                FontWeight = FontWeights.Bold,
                Foreground = CommentPrimaryTextBrush(),
                Background = CommentMutedSurfaceBrush(),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            sendBtn.Click += async (_, _) => await FinalizeCommentFromComposerAsync(input.Text);

            var inner = new Grid();
            inner.Children.Add(input);
            inner.Children.Add(sendBtn);

            var (avatarFill, avatarStroke) = ResolveParticipantAvatarBrushes(_myUserId);
            var avatar = new Border
            {
                Tag = CommentPinTag,
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18, 18, 4, 18),
                Background = CommentSurfaceBrush(),
                Padding = new Thickness(3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(14),
                    Background = avatarFill,
                    BorderBrush = avatarStroke,
                    BorderThickness = new Thickness(2),
                    Child = new TextBlock
                    {
                        Text = GetInitials(_cursorDisplayName),
                        Foreground = avatarStroke,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };

            input.MinWidth = 220;
            input.Background = Brushes.Transparent;
            input.Foreground = CommentPrimaryTextBrush();
            input.CaretBrush = CommentPrimaryTextBrush();

            var row = new Grid { MinWidth = 300 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(avatar, 0);
            Grid.SetColumn(inner, 1);
            row.Children.Add(avatar);
            row.Children.Add(inner);

            _commentComposer = new Border
            {
                Background = CommentSurfaceBrush(),
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(8, 6, 8, 8),
                Child = row,
                MinWidth = 320,
                BorderBrush = CommentBorderBrush(),
                BorderThickness = new Thickness(1)
            };
            _commentComposer.Effect = new DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 2,
                Opacity = WhiteSpaceThemeManager.IsDarkApplied ? 0.5 : 0.22,
                Color = Colors.Black
            };

            ApplyCommentComposerTheme();

            Canvas.SetLeft(_commentComposer, world.X);
            Canvas.SetTop(_commentComposer, world.Y);
            BoardCanvas.Children.Add(_commentComposer);
            input.Focus();
        }

        private void HideCommentComposer()
        {
            if (_commentComposer == null)
            {
                return;
            }

            BoardCanvas.Children.Remove(_commentComposer);
            _commentComposer = null;

            if (_tool == ToolMode.Comment)
            {
                EnsurePreviewComment();
            }
        }

        private async Task FinalizeCommentFromComposerAsync(string text)
        {
            var message = text?.Trim() ?? "";
            if (string.IsNullOrEmpty(message))
            {
                HideCommentComposer();
                return;
            }

            if (IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            HideCommentComposer();
            CaptureBoardStateForUndo();

            var meta = new BoardCommentMetadata
            {
                AuthorId = _myUserId?.ToString(),
                AuthorName = string.IsNullOrWhiteSpace(_cursorDisplayName) ? "Участник" : _cursorDisplayName.Trim(),
                Message = message,
                CreatedAtUtc = DateTime.UtcNow
            };

            var uniqueId = await _supabaseService.GenerateUniqueIdAsync(_boardId);
            var shape = new BoardShape
            {
                BoardId = _boardId,
                Type = "comment",
                X = _pendingCommentWorld.X,
                Y = _pendingCommentWorld.Y,
                Width = 40,
                Height = 44,
                Text = BoardCommentMetadataHelper.Serialize(meta),
                Id = uniqueId
            };

            _shapesOnBoard.Add(shape);
            AddShapeToCanvas(shape, false);
            await _supabaseService.SaveShapeAsync(shape);
            MarkSaved();
            PushShapeToFirebase(shape);
            SetTool(ToolMode.Select);
        }

        private void TryEraseAt(Point world)
        {
            var toRemove = new List<BoardShape>();
            foreach (var shape in _shapesOnBoard)
            {
                if (IsPointOnShape(world, shape, EraserHitRadius))
                {
                    toRemove.Add(shape);
                }
            }

            foreach (var shape in toRemove)
            {
                _ = EraseShapeAsync(shape);
            }
        }

        private bool IsPointOnShape(Point world, BoardShape shape, double radius)
        {
            if (shape.Type is "line" or "marker")
            {
                var points = shape.DeserializedPoints;
                if (points.Count == 0 && !string.IsNullOrWhiteSpace(shape.Points))
                {
                    try
                    {
                        points = JsonConvert.DeserializeObject<List<Point>>(shape.Points) ?? new List<Point>();
                    }
                    catch
                    {
                        return false;
                    }
                }

                return IsPointNearPolyline(world, points, radius);
            }

            if (shape.Type == "connector")
            {
                var points = ConnectorAttachmentHelper.ResolveConnectorPoints(shape, _shapesOnBoard);
                return IsPointNearPolyline(world, points, radius);
            }

            if (shape.Type == "comment")
            {
                return world.X >= shape.X - radius && world.X <= shape.X + 40 + radius
                       && world.Y >= shape.Y - radius && world.Y <= shape.Y + 44 + radius;
            }

            if (shape.Type == "text")
            {
                return world.X >= shape.X - radius && world.X <= shape.X + shape.Width + radius
                       && world.Y >= shape.Y - radius && world.Y <= shape.Y + shape.Height + radius;
            }

            var left = shape.X - shape.Width / 2 - radius;
            var top = shape.Y - shape.Height / 2 - radius;
            var right = shape.X + shape.Width / 2 + radius;
            var bottom = shape.Y + shape.Height / 2 + radius;
            return world.X >= left && world.X <= right && world.Y >= top && world.Y <= bottom;
        }

        private bool TryGetSelectionBounds(out double left, out double top, out double width, out double height)
        {
            left = top = width = height = 0;
            if (_resizeBorder != null)
            {
                left = Canvas.GetLeft(_resizeBorder);
                top = Canvas.GetTop(_resizeBorder);
                width = _resizeBorder.Width;
                height = _resizeBorder.Height;
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;
                return true;
            }

            if (_resizeTarget is FrameworkElement fe)
            {
                if (ConnectorVisualHelper.GetLine(fe) is Polyline line && line.Points.Count > 0)
                {
                    left = line.Points.Min(p => p.X);
                    top = line.Points.Min(p => p.Y);
                    width = Math.Max(line.Points.Max(p => p.X) - left, 1);
                    height = Math.Max(line.Points.Max(p => p.Y) - top, 1);
                    return true;
                }

                left = Canvas.GetLeft(fe);
                top = Canvas.GetTop(fe);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;
                width = fe.ActualWidth > 0 ? fe.ActualWidth : fe.Width;
                height = fe.ActualHeight > 0 ? fe.ActualHeight : fe.Height;
                if (width < 1) width = 1;
                if (height < 1) height = 1;
                return true;
            }

            return false;
        }

        private void UpdateSelectionBoundsFromElement(UIElement element)
        {
            if (_resizeBorder == null || !TryGetElementWorldBounds(element, out var left, out var top, out var w, out var h))
            {
                return;
            }

            UpdateResizeFrame(left, top, w, h);
        }

        private static bool TryGetElementWorldBounds(UIElement element, out double left, out double top, out double w, out double h)
        {
            left = top = 0;
            w = h = 1;
            if (ConnectorVisualHelper.GetLine(element) is Polyline line && line.Points.Count > 0)
            {
                left = line.Points.Min(p => p.X);
                top = line.Points.Min(p => p.Y);
                w = Math.Max(line.Points.Max(p => p.X) - left, 1);
                h = Math.Max(line.Points.Max(p => p.Y) - top, 1);
                return true;
            }

            if (element is not FrameworkElement fe)
            {
                return false;
            }

            left = Canvas.GetLeft(fe);
            top = Canvas.GetTop(fe);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            w = fe.ActualWidth > 1 ? fe.ActualWidth : (fe.Width > 1 ? fe.Width : 1);
            h = fe.ActualHeight > 1 ? fe.ActualHeight : (fe.Height > 1 ? fe.Height : 1);
            return true;
        }

        private void UpdateMarqueeRect(Point world)
        {
            if (_marqueeRect == null)
            {
                return;
            }

            var left = Math.Min(_marqueeStartWorld.X, world.X);
            var top = Math.Min(_marqueeStartWorld.Y, world.Y);
            var w = Math.Abs(world.X - _marqueeStartWorld.X);
            var h = Math.Abs(world.Y - _marqueeStartWorld.Y);
            Canvas.SetLeft(_marqueeRect, left);
            Canvas.SetTop(_marqueeRect, top);
            _marqueeRect.Width = Math.Max(w, 1);
            _marqueeRect.Height = Math.Max(h, 1);
        }

        private void FinalizeMarqueeSelection(Point world)
        {
            var left = Math.Min(_marqueeStartWorld.X, world.X);
            var top = Math.Min(_marqueeStartWorld.Y, world.Y);
            var right = Math.Max(_marqueeStartWorld.X, world.X);
            var bottom = Math.Max(_marqueeStartWorld.Y, world.Y);
            if (right - left < 4 && bottom - top < 4)
            {
                return;
            }

            var hits = new List<UIElement>();
            foreach (var child in BoardCanvas.Children.OfType<FrameworkElement>())
            {
                if (string.IsNullOrEmpty(child.Uid) || !IsBoardSelectableElement(child))
                {
                    continue;
                }

                if (!TryGetElementWorldBounds(child, out var elLeft, out var elTop, out var elW, out var elH))
                {
                    continue;
                }

                var elRight = elLeft + elW;
                var elBottom = elTop + elH;
                if (elRight >= left && elLeft <= right && elBottom >= top && elTop <= bottom)
                {
                    hits.Add(child);
                }
            }

            if (hits.Count == 0)
            {
                return;
            }

            if (hits.Count == 1)
            {
                ShowResizeFrame(hits[0]);
                return;
            }

            ShowGroupSelectionFrame(hits);
        }

        private void ShowGroupSelectionFrame(List<UIElement> elements)
        {
            RemoveResizeFrame();
            _multiSelectedElements.Clear();
            _multiSelectedElements.AddRange(elements);
            _resizeTarget = elements[0];

            var left = double.MaxValue;
            var top = double.MaxValue;
            var right = double.MinValue;
            var bottom = double.MinValue;
            foreach (var el in elements)
            {
                if (!TryGetElementWorldBounds(el, out var l, out var t, out var w, out var h))
                {
                    continue;
                }

                left = Math.Min(left, l);
                top = Math.Min(top, t);
                right = Math.Max(right, l + w);
                bottom = Math.Max(bottom, t + h);
            }

            _resizeBorder = new Rectangle
            {
                Width = Math.Max(right - left, 1),
                Height = Math.Max(bottom - top, 1),
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = true,
                Cursor = Cursors.SizeAll,
                Tag = SelectionFrameTag
            };
            Canvas.SetLeft(_resizeBorder, left);
            Canvas.SetTop(_resizeBorder, top);
            _resizeBorder.MouseLeftButtonDown += GroupSelectionFrame_MouseLeftButtonDown;
            RegisterSelectionChrome(_resizeBorder);
            SyncSelectionToolbar(elements[0]);
            UpdateSelectionToolbarPosition();
        }

        private void GroupSelectionFrame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_multiSelectedElements.Count < 2)
            {
                return;
            }

            var world = ScreenToWorld(e.GetPosition(Viewport));
            BeginMultiSelectionDrag(world);
            e.Handled = true;
        }

        private bool IsWorldPointInMultiSelection(Point world)
        {
            if (_multiSelectedElements.Count < 2)
            {
                return false;
            }

            if (_resizeBorder != null)
            {
                var left = Canvas.GetLeft(_resizeBorder);
                var top = Canvas.GetTop(_resizeBorder);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;
                return world.X >= left && world.X <= left + _resizeBorder.Width
                       && world.Y >= top && world.Y <= top + _resizeBorder.Height;
            }

            var minLeft = double.MaxValue;
            var minTop = double.MaxValue;
            var maxRight = double.MinValue;
            var maxBottom = double.MinValue;
            var any = false;
            foreach (var el in _multiSelectedElements)
            {
                if (!TryGetElementWorldBounds(el, out var l, out var t, out var w, out var h))
                {
                    continue;
                }

                any = true;
                minLeft = Math.Min(minLeft, l);
                minTop = Math.Min(minTop, t);
                maxRight = Math.Max(maxRight, l + w);
                maxBottom = Math.Max(maxBottom, t + h);
            }

            return any
                   && world.X >= minLeft && world.X <= maxRight
                   && world.Y >= minTop && world.Y <= maxBottom;
        }

        private void ClearMultiSelection()
        {
            _multiSelectedElements.Clear();
            _multiDragSnapshots.Clear();
            if (_resizeTarget != null || _resizeBorder != null)
            {
                RemoveResizeFrame();
            }
            else
            {
                PurgeOrphanSelectionChromeFromCanvas();
            }
        }

        private void BeginMultiSelectionDrag(Point world)
        {
            CaptureBoardStateForUndo();
            _isDraggingElement = true;
            _dragElement = _multiSelectedElements[0];
            _multiDragStartWorld = world;
            _multiDragSnapshots.Clear();

            foreach (var el in _multiSelectedElements)
            {
                if (el is not FrameworkElement fe || string.IsNullOrEmpty(fe.Uid))
                {
                    continue;
                }

                if (!int.TryParse(fe.Uid, out var id))
                {
                    continue;
                }

                var shape = _shapesOnBoard.FirstOrDefault(s => s.Id == id);
                if (shape == null)
                {
                    continue;
                }

                List<Point>? polylinePoints = null;
                if (shape.Type is "line" or "marker" or "connector")
                {
                    if (el is Polyline directPl && directPl.Points.Count > 0)
                    {
                        polylinePoints = directPl.Points.Select(p => new Point(p.X, p.Y)).ToList();
                    }
                    else if (ConnectorVisualHelper.GetLine(el) is Polyline pl && pl.Points.Count > 0)
                    {
                        polylinePoints = pl.Points.Select(p => new Point(p.X, p.Y)).ToList();
                    }
                    else
                    {
                        polylinePoints = shape.DeserializedPoints is { Count: > 0 } dp
                            ? dp.Select(p => new Point(p.X, p.Y)).ToList()
                            : JsonConvert.DeserializeObject<List<Point>>(shape.Points);
                    }
                }

                _multiDragSnapshots[id] = new MultiDragSnapshot
                {
                    Anchor = GetShapeWorldPosition(shape),
                    PolylinePoints = polylinePoints,
                    Element = el
                };
            }

            Viewport.CaptureMouse();
        }

        private Point GetShapeWorldPosition(BoardShape shape)
        {
            if (shape.Type is "line" or "marker" or "connector")
            {
                if (!string.IsNullOrWhiteSpace(shape.Points))
                {
                    try
                    {
                        var pts = JsonConvert.DeserializeObject<List<Point>>(shape.Points);
                        if (pts is { Count: > 0 })
                        {
                            return pts[0];
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            if (shape.Type is "text" or "comment")
            {
                return new Point(shape.X, shape.Y);
            }

            return new Point(shape.X - shape.Width / 2, shape.Y - shape.Height / 2);
        }

        private void MoveMultiSelectionTo(Point world)
        {
            var delta = new Point(world.X - _multiDragStartWorld.X, world.Y - _multiDragStartWorld.Y);
            var affectedShapeIds = new HashSet<int>();

            foreach (var (id, snapshot) in _multiDragSnapshots.ToList())
            {
                var shape = _shapesOnBoard.FirstOrDefault(s => s.Id == id);
                if (shape == null)
                {
                    continue;
                }

                ApplyShapeWorldPosition(shape, snapshot, delta);
                affectedShapeIds.Add(id);
            }

            foreach (var id in affectedShapeIds)
            {
                RefreshConnectorVisualsReferencingShapeLocal(id);
            }

            if (_resizeBorder != null)
            {
                UpdateGroupSelectionBounds();
            }

            UpdateSelectionToolbarPosition();
        }

        private void ApplyShapeWorldPosition(BoardShape shape, MultiDragSnapshot snapshot, Point delta)
        {
            var element = snapshot.Element;
            if (shape.Type is "line" or "marker" or "connector" && snapshot.PolylinePoints is { Count: > 0 } initialPts)
            {
                var moved = initialPts.Select(p => new Point(p.X + delta.X, p.Y + delta.Y)).ToList();
                shape.DeserializedPoints = moved;
                shape.Points = JsonConvert.SerializeObject(moved);

                if (element is Polyline directLine)
                {
                    directLine.Points.Clear();
                    foreach (var p in moved)
                    {
                        directLine.Points.Add(p);
                    }
                }
                else if (ConnectorVisualHelper.GetLine(element) is Polyline pl)
                {
                    pl.Points.Clear();
                    foreach (var p in moved)
                    {
                        pl.Points.Add(p);
                    }
                }

                if (shape.Type == "connector")
                {
                    ConnectorVisualHelper.UpdatePoints(
                        element, shape, moved, GetBrushFromColor(shape.Color), ConnectorStrokeThickness);
                }

                return;
            }

            var topLeft = new Point(snapshot.Anchor.X + delta.X, snapshot.Anchor.Y + delta.Y);
            if (shape.Type is "text" or "comment")
            {
                shape.X = topLeft.X;
                shape.Y = topLeft.Y;
                Canvas.SetLeft(element, topLeft.X);
                Canvas.SetTop(element, topLeft.Y);
                return;
            }

            shape.X = topLeft.X + shape.Width / 2;
            shape.Y = topLeft.Y + shape.Height / 2;
            Canvas.SetLeft(element, topLeft.X);
            Canvas.SetTop(element, topLeft.Y);
        }

        private void UpdateGroupSelectionBounds()
        {
            if (_resizeBorder == null || _multiSelectedElements.Count == 0)
            {
                return;
            }

            var left = double.MaxValue;
            var top = double.MaxValue;
            var right = double.MinValue;
            var bottom = double.MinValue;
            foreach (var el in _multiSelectedElements)
            {
                if (!TryGetElementWorldBounds(el, out var l, out var t, out var w, out var h))
                {
                    continue;
                }

                left = Math.Min(left, l);
                top = Math.Min(top, t);
                right = Math.Max(right, l + w);
                bottom = Math.Max(bottom, t + h);
            }

            Canvas.SetLeft(_resizeBorder, left);
            Canvas.SetTop(_resizeBorder, top);
            _resizeBorder.Width = Math.Max(right - left, 1);
            _resizeBorder.Height = Math.Max(bottom - top, 1);
        }

        private async Task SaveMultiSelectionPositionsAsync()
        {
            var tasks = new List<Task>();
            foreach (var el in _multiSelectedElements.ToList())
            {
                if (el is FrameworkElement { Uid: { Length: > 0 } })
                {
                    tasks.Add(SaveElementPositionAsync(el));
                }
            }

            await Task.WhenAll(tasks);

            try
            {
                await _firebaseService.ReplaceBoardShapesAsync(_boardId.ToString(), _shapesOnBoard);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Firebase sync after multi-move: {ex.Message}");
            }

            MarkSaved();
        }

        private async Task DeleteMultiSelectedAsync()
        {
            var shapesToDelete = CollectMultiSelectedShapes();
            if (shapesToDelete.Count == 0)
            {
                return;
            }

            CaptureBoardStateForUndo();

            var deletedIds = new List<int>();
            foreach (var shape in shapesToDelete)
            {
                if (await _supabaseService.DeleteShapeAsync(shape.Id))
                {
                    deletedIds.Add(shape.Id);
                }
            }

            foreach (var shapeId in deletedIds)
            {
                RemoveShapeFromBoardLocal(shapeId);
            }

            try
            {
                await _firebaseService.ClearAndReplaceBoardShapesAsync(_boardId.ToString(), _shapesOnBoard);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Firebase sync after multi-delete: {ex.Message}");
                foreach (var shapeId in deletedIds)
                {
                    await _firebaseService.DeleteShapeAsync(_boardId.ToString(), shapeId.ToString());
                }
            }

            ClearMultiSelection();
            RemoveResizeFrame();
            MarkSaved();
        }

        private static bool IsPointNearPolyline(Point p, IReadOnlyList<Point> points, double radius)
        {
            if (points.Count < 2)
            {
                return points.Count == 1 && Distance(p, points[0]) <= radius;
            }

            for (var i = 0; i < points.Count - 1; i++)
            {
                if (DistancePointToSegment(p, points[i], points[i + 1]) <= radius)
                {
                    return true;
                }
            }

            return false;
        }

        private static double Distance(Point a, Point b) =>
            Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static double DistancePointToSegment(Point p, Point a, Point b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
            {
                return Distance(p, a);
            }

            var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
            t = Math.Clamp(t, 0, 1);
            var proj = new Point(a.X + t * dx, a.Y + t * dy);
            return Distance(p, proj);
        }

        private void StartEraserTrail(Point world)
        {
            RemoveEraserTrail();
            _eraserTrailLine = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(100, 255, 59, 48)),
                StrokeThickness = EraserHitRadius * 1.6,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
            _eraserTrailLine.Points.Add(world);
            BoardCanvas.Children.Add(_eraserTrailLine);
            Panel.SetZIndex(_eraserTrailLine, 20000);
        }

        private void AppendEraserTrail(Point world)
        {
            if (_eraserTrailLine == null)
            {
                StartEraserTrail(world);
                return;
            }

            var pts = _eraserTrailLine.Points;
            if (pts.Count == 0 || Distance(pts[^1], world) > 2)
            {
                pts.Add(world);
            }
        }

        private void RemoveEraserTrail()
        {
            if (_eraserTrailLine == null)
            {
                return;
            }

            BoardCanvas.Children.Remove(_eraserTrailLine);
            _eraserTrailLine = null;
        }

        private async Task EraseShapeAsync(BoardShape shape)
        {
            if (!await _supabaseService.DeleteShapeAsync(shape.Id))
            {
                return;
            }

            await _firebaseService.DeleteShapeAsync(_boardId.ToString(), shape.Id.ToString());
            RemoveShapeFromBoardLocal(shape.Id);
            MarkSaved();
        }

        private void RemoveConnectorEndpointHandles()
        {
            foreach (var el in _connectorEndpointElements.ToList())
            {
                el.MouseLeftButtonDown -= ConnectorEndpoint_MouseDown;
                _selectionChromeElements.Remove(el);
                BoardCanvas.Children.Remove(el);
            }

            _connectorEndpointElements.Clear();
        }

        private void TryAddConnectorEndpointHandles(UIElement target)
        {
            RemoveConnectorEndpointHandles();

            if (_tool != ToolMode.Select || HasMultiSelection())
            {
                return;
            }

            if (IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            if (string.IsNullOrEmpty(target.Uid))
            {
                return;
            }

            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == target.Uid);
            if (shape?.Type != "connector")
            {
                return;
            }

            var pts = ConnectorAttachmentHelper.ResolveConnectorPoints(shape, _shapesOnBoard);
            if (pts.Count < 2)
            {
                return;
            }

            AddConnectorEndpointHandle(shape.Id, "start", pts[0]);
            AddConnectorEndpointHandle(shape.Id, "end", pts[^1]);
        }

        private void AddConnectorEndpointHandle(int connectorId, string which, Point world)
        {
            var el = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6)),
                StrokeThickness = 2,
                Cursor = Cursors.SizeAll,
                Tag = $"conn-end:{connectorId}:{which}"
            };
            el.MouseLeftButtonDown += ConnectorEndpoint_MouseDown;
            Canvas.SetLeft(el, world.X - 7);
            Canvas.SetTop(el, world.Y - 7);
            RegisterSelectionChrome(el);
            _connectorEndpointElements.Add(el);
        }

        private void ConnectorEndpoint_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Ellipse el || el.Tag is not string tag || !tag.StartsWith("conn-end:", StringComparison.Ordinal))
            {
                return;
            }

            if (IsBoardEditLockedForCurrentUser())
            {
                return;
            }

            e.Handled = true;
            var parts = tag.Split(':');
            if (parts.Length < 3 || !int.TryParse(parts[1], out var connectorId))
            {
                return;
            }

            _isDraggingConnectorEndpoint = true;
            _connectorEndpointDragShapeId = connectorId;
            _connectorEndpointDragWhich = parts[2];
            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id == connectorId);
            if (shape != null && ConnectorAttachmentHelper.TryDeserialize(shape.Text, out var att))
            {
                ShowConnectorSnapHighlights(_connectorEndpointDragWhich == "start" ? att.EndShapeId : att.StartShapeId);
            }

            CaptureBoardStateForUndo();
            Viewport.CaptureMouse();
        }

        private void UpdateConnectorEndpointDrag(Point world, Point viewportPos)
        {
            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id == _connectorEndpointDragShapeId);
            if (shape?.Type != "connector" || FindUIElementByUid(shape.Id.ToString()) is not { } ui)
            {
                return;
            }

            if (!ConnectorAttachmentHelper.TryDeserialize(shape.Text, out var att))
            {
                att = new ConnectorAttachment();
            }

            var pts = ConnectorAttachmentHelper.ResolveConnectorPoints(shape, _shapesOnBoard);
            if (pts.Count < 2)
            {
                return;
            }

            if (_connectorEndpointDragWhich == "start")
            {
                pts[0] = world;
                if (ConnectorAttachmentHelper.TryFindNearestPort(world, _shapesOnBoard, att.EndShapeId, out var sid, out var side))
                {
                    att.StartShapeId = sid;
                    att.StartSide = side;
                    var startShape = _shapesOnBoard.FirstOrDefault(s => s.Id == sid);
                    if (startShape != null)
                    {
                        pts[0] = ConnectorAttachmentHelper.GetAnchorWorldPoint(startShape, side);
                    }
                }
                else
                {
                    att.StartShapeId = null;
                    att.StartSide = null;
                }
            }
            else
            {
                pts[^1] = world;
                if (ConnectorAttachmentHelper.TryFindNearestPort(world, _shapesOnBoard, att.StartShapeId, out var sid, out var side))
                {
                    att.EndShapeId = sid;
                    att.EndSide = side;
                    var endShape = _shapesOnBoard.FirstOrDefault(s => s.Id == sid);
                    if (endShape != null)
                    {
                        pts[^1] = ConnectorAttachmentHelper.GetAnchorWorldPoint(endShape, side);
                    }
                }
                else
                {
                    att.EndShapeId = null;
                    att.EndSide = null;
                }
            }

            shape.Text = ConnectorAttachmentHelper.SerializeForStorage(att);
            ConnectorVisualHelper.UpdatePoints(ui, shape, pts, GetBrushFromColor(shape.Color), ConnectorStrokeThickness);
            UpdateConnectorEndpointHandlePositions(shape.Id, pts[0], pts[^1]);
            ShowConnectorSnapHighlights(att.StartShapeId);
        }

        private void UpdateConnectorEndpointHandlePositions(int connectorId, Point start, Point end)
        {
            foreach (var el in _connectorEndpointElements)
            {
                if (el.Tag is not string tag || !tag.StartsWith($"conn-end:{connectorId}:", StringComparison.Ordinal))
                {
                    continue;
                }

                var which = tag.Split(':')[^1];
                var pt = which == "start" ? start : end;
                Canvas.SetLeft(el, pt.X - 7);
                Canvas.SetTop(el, pt.Y - 7);
            }
        }

        private async Task FinalizeConnectorEndpointDragAsync(Point endWorld, Point viewportPos)
        {
            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id == _connectorEndpointDragShapeId);
            if (shape?.Type != "connector")
            {
                return;
            }

            if (!ConnectorAttachmentHelper.TryParse(shape.Text, out var att))
            {
                att = new ConnectorAttachment();
            }

            if (_connectorEndpointDragWhich == "start")
            {
                if (TryGetConnectorPortAtViewport(viewportPos, out var sid, out var side))
                {
                    att.StartShapeId = sid;
                    att.StartSide = side;
                }
                else if (ConnectorAttachmentHelper.TryFindNearestPort(endWorld, _shapesOnBoard, att.EndShapeId, out sid, out side))
                {
                    att.StartShapeId = sid;
                    att.StartSide = side;
                }
                else
                {
                    att.StartShapeId = null;
                    att.StartSide = null;
                }
            }
            else if (_connectorEndpointDragWhich == "end")
            {
                if (TryGetConnectorPortAtViewport(viewportPos, out var sid, out var side))
                {
                    att.EndShapeId = sid;
                    att.EndSide = side;
                }
                else if (ConnectorAttachmentHelper.TryFindNearestPort(endWorld, _shapesOnBoard, att.StartShapeId, out sid, out side))
                {
                    att.EndShapeId = sid;
                    att.EndSide = side;
                }
                else
                {
                    att.EndShapeId = null;
                    att.EndSide = null;
                }
            }

            shape.Text = ConnectorAttachmentHelper.SerializeForStorage(att);
            ApplyConnectorGeometryToBoardShape(shape);
            ClearConnectorSnapHighlights();

            if (FindUIElementByUid(shape.Id.ToString()) is { } ui)
            {
                var pts = ConnectorAttachmentHelper.ResolveConnectorPoints(shape, _shapesOnBoard);
                ConnectorVisualHelper.UpdatePoints(ui, shape, pts, GetBrushFromColor(shape.Color), ConnectorStrokeThickness);
                ShowResizeFrame(ui);
            }

            await _supabaseService.SaveShapeAsync(shape);
            MarkSaved();
            PushShapeToFirebase(shape);
        }
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
