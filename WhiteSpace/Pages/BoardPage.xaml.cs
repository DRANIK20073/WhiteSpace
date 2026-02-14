using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Supabase;
using Newtonsoft.Json;
using System.Linq;
using System.Windows.Threading;

namespace WhiteSpace.Pages
{
    public partial class BoardPage : Page
    {
        private List<BoardShape> _shapesOnBoard = new List<BoardShape>();
        private readonly Guid _boardId;
        private SupabaseService _supabaseService;

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

        public BoardPage(Guid boardId)
        {
            InitializeComponent();
            _boardId = boardId;
            _supabaseService = new SupabaseService();

            // Добавляем обработчики событий для Viewport
            Viewport.MouseDown += Viewport_MouseDown;
            Viewport.MouseMove += Viewport_MouseMove;
            Viewport.MouseUp += Viewport_MouseUp;
            Viewport.MouseWheel += Viewport_MouseWheel;

            Loaded += Page_Loaded;
            Unloaded += Page_Unloaded;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var viewportCenter = new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2);
            var canvasCenter = new Point(BoardCanvas.Width / 2, BoardCanvas.Height / 2);

            BoardTranslate.X = viewportCenter.X - canvasCenter.X;
            BoardTranslate.Y = viewportCenter.Y - canvasCenter.Y;

            SetTool(ToolMode.Hand);

            // Получаем роль пользователя
            var userRole = await _supabaseService.GetUserRoleForBoardAsync(_boardId);

            // Если роль "viewer", ограничиваем возможности
            if (userRole == "viewer")
            {
                DisableEditingTools();  // Отключаем все инструменты редактирования
            }

            var shapes = await _supabaseService.LoadBoardShapesAsync(_boardId);

            foreach (var shape in shapes)
            {
                AddShapeToCanvas(shape);
            }

            await ConnectToRealtime();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            SupabaseRealtimeService.Disconnect(_boardId);
        }

        private async Task ConnectToRealtime()
        {
            try
            {
                await SupabaseRealtimeService.ConnectAndSubscribe(
                    SupabaseService.Client,
                    _boardId,
                    OnShapeInserted,
                    OnShapeUpdated,
                    OnShapeDeleted
                );
                Console.WriteLine($"[Realtime] Подключение к каналу для доски {_boardId} успешно.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подключения к Realtime: {ex.Message}");
                Console.WriteLine($"Ошибка подключения: {ex.Message}");
            }
        }

        private void OnShapeInserted(BoardShape shape)
        {
            Console.WriteLine($"[Realtime] Фигура вставлена: {shape.Id}");
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Проверяем, есть ли уже элемент с таким Uid
                var existing = BoardCanvas.Children
                    .OfType<UIElement>()
                    .FirstOrDefault(el => el.Uid == shape.Id.ToString());

                if (existing == null)
                {
                    AddShapeToCanvas(shape); // Добавляем только если ещё нет
                }
            });
        }

        private void OnShapeUpdated(BoardShape updatedShape)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = BoardCanvas.Children
                    .OfType<UIElement>()
                    .FirstOrDefault(el => el.Uid == updatedShape.Id.ToString());

                if (existing == null) return;

                switch (existing)
                {
                    case Rectangle rect:
                    case Ellipse ell:
                        var shape = (Shape)existing;
                        shape.Stroke = GetBrushFromColor(updatedShape.Color);
                        shape.Width = updatedShape.Width;
                        shape.Height = updatedShape.Height;
                        Canvas.SetLeft(shape, updatedShape.X - updatedShape.Width / 2);
                        Canvas.SetTop(shape, updatedShape.Y - updatedShape.Height / 2);
                        break;

                    case Polyline polyline:
                        polyline.Stroke = GetBrushFromColor(updatedShape.Color);
                        if (!string.IsNullOrEmpty(updatedShape.Points))
                        {
                            var points = JsonConvert.DeserializeObject<List<Point>>(updatedShape.Points);
                            polyline.Points.Clear();
                            foreach (var p in points) polyline.Points.Add(p);
                        }
                        break;

                    case TextBox textBox:
                        textBox.Foreground = GetBrushFromColor(updatedShape.Color);
                        textBox.Text = updatedShape.Text;
                        Canvas.SetLeft(textBox, updatedShape.X);
                        Canvas.SetTop(textBox, updatedShape.Y);
                        break;
                }

                // Обновить локальный список
                var local = _shapesOnBoard.FirstOrDefault(s => s.Id == updatedShape.Id);
                if (local != null)
                {
                    local.Color = updatedShape.Color;
                    local.X = updatedShape.X;
                    local.Y = updatedShape.Y;
                    local.Width = updatedShape.Width;
                    local.Height = updatedShape.Height;
                    local.Text = updatedShape.Text;
                    local.Points = updatedShape.Points;
                }
            });
        }

        private void OnShapeDeleted(BoardShape deletedShape)
        {
            Console.WriteLine($"[Realtime] Фигура удалена: {deletedShape.Id}");
            Application.Current.Dispatcher.Invoke(() =>
            {
                var element = BoardCanvas.Children
                    .OfType<UIElement>()
                    .FirstOrDefault(el => el.Uid == deletedShape.Id.ToString());

                if (element != null)
                {
                    BoardCanvas.Children.Remove(element);
                    _shapesOnBoard.RemoveAll(s => s.Id == deletedShape.Id);
                }
            });
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
            // Отключаем инструменты рисования и редактирования
            PenButton.IsEnabled = false;
            RectButton.IsEnabled = false;
            EllipseButton.IsEnabled = false;
            TextButton.IsEnabled = false;
            ColorPanel.Visibility = Visibility.Collapsed;  // Скрываем панель выбора цвета
        }


        //Загрузка фигур на доску из бд
        private void AddShapeToCanvas(BoardShape shape)
        {
            // Получаем цвет из shape.Color
            Brush brush = Brushes.Black;

            if (!string.IsNullOrEmpty(shape.Color))
            {
                try
                {
                    brush = (Brush)new BrushConverter().ConvertFromString(shape.Color);
                }
                catch
                {
                    brush = Brushes.Black;
                }
            }

            if (shape.Type == "line")
            {
                var polyline = new Polyline
                {
                    Stroke = brush,   // ✅ используем сохранённый цвет
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
                _shapesOnBoard.Add(shape);
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
                    Foreground = brush,  // ✅ цвет текста
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
                _shapesOnBoard.Add(shape);
            }
            else
            {
                UIElement element = shape.Type switch
                {
                    "rectangle" => new Rectangle
                    {
                        Width = shape.Width,
                        Height = shape.Height,
                        Stroke = brush,      // ✅ цвет фигуры
                        StrokeThickness = 2,
                        Fill = Brushes.Transparent,
                        Uid = shape.Id.ToString()
                    },
                    "ellipse" => new Ellipse
                    {
                        Width = shape.Width,
                        Height = shape.Height,
                        Stroke = brush,      // ✅
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
                    _shapesOnBoard.Add(shape);
                }
            }
        }


        //Обработчики событий для TextBox
        private void TextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            if (_tool == ToolMode.Hand && e.LeftButton == MouseButtonState.Pressed)
            {
                var world = ScreenToWorld(e.GetPosition(Viewport));
                _dragStartWorld = world;

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
            // Автосохранение можно добавить при необходимости
        }

        //Инструменты
        private void Hand_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Hand);
        private void Pen_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Pen);
        private void Rect_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Rect);
        private void Ellipse_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Ellipse);
        private void Text_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Text);

        private void SetTool(ToolMode tool)
        {
            _tool = tool;

            // ===== Панель цветов =====
            if (_tool == ToolMode.Pen ||
                _tool == ToolMode.Rect ||
                _tool == ToolMode.Ellipse ||
                _tool == ToolMode.Text)
            {
                ColorPanel.Visibility = Visibility.Visible;
            }
            else if (_tool == ToolMode.Hand)
            {
                // В режиме перемещения показываем только если есть выделение
                ColorPanel.Visibility =
                    _selectedElement != null
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            else
            {
                ColorPanel.Visibility = Visibility.Collapsed;
            }

            // ===== Сброс состояний =====
            _isDrawing = false;
            _currentStroke = null;
            _isPanning = false;
            _isDraggingElement = false;
            _dragElement = null;

            // Убираем ручки и рамку
            RemoveResizeFrame();

            // Убираем старую фигуру-призрак
            RemovePreviewShape();

            // Призрак включаем только для фигур
            if (_tool == ToolMode.Rect || _tool == ToolMode.Ellipse)
            {
                EnsurePreviewShape();
            }

            // ===== Курсор =====
            Viewport.Cursor = _tool switch
            {
                ToolMode.Hand => Cursors.Hand,
                ToolMode.Pen => Cursors.Pen,
                ToolMode.Text => Cursors.IBeam,
                _ => Cursors.Cross
            };

            // ===== Настройка существующих TextBox =====
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

        // === Mouse helpers (Screen <-> World) ===
        private Point ScreenToWorld(Point screenPoint)
        {
            var s = BoardScale.ScaleX;
            return new Point(
                (screenPoint.X - BoardTranslate.X) / s,
                (screenPoint.Y - BoardTranslate.Y) / s
            );
        }

        // === Viewport events ===
        private async void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var userRole = await _supabaseService.GetUserRoleForBoardAsync(_boardId);
            if (userRole == "viewer")
            {
                // Режим только просмотра — никаких изменений, только панорамирование
                if (_tool == ToolMode.Hand && e.LeftButton == MouseButtonState.Pressed)
                {
                    StartPan(e.GetPosition(Viewport));
                }
                return;
            }

            // Проверяем, не кликнули ли по ручке
            var hitTestResult = VisualTreeHelper.HitTest(Viewport, e.GetPosition(Viewport));
            if (hitTestResult != null)
            {
                var hitElement = hitTestResult.VisualHit;
                while (hitElement != null && !(hitElement is Rectangle))
                    hitElement = VisualTreeHelper.GetParent(hitElement);

                // Если кликнули по ручке, не обрабатываем дальше
                if (hitElement is Rectangle rect && rect.Tag is string tag && tag.Length <= 2)
                {
                    return;
                }
            }

            Viewport.Focus();
            var screen = e.GetPosition(Viewport);
            var world = ScreenToWorld(screen);

            // Если уже перетаскиваем элемент, не начинаем панорамирование
            if (_isDraggingElement)
                return;

            // 1) Если клик по объекту — начинаем перетаскивание (в режиме Hand)
            if (_tool == ToolMode.Hand)
            {
                RemoveResizeFrame();

                // Проверяем клик по не-TexBox элементам
                var hitTestResult2 = VisualTreeHelper.HitTest(Viewport, screen);
                if (hitTestResult2 != null)
                {
                    var hitElement = hitTestResult2.VisualHit;

                    // Ищем родительский элемент
                    while (hitElement != null && !(hitElement is UIElement))
                        hitElement = VisualTreeHelper.GetParent(hitElement);

                    var uiElement = hitElement as UIElement;

                    if (uiElement != null && uiElement != BoardCanvas && uiElement != Viewport &&
                        !(uiElement is TextBox) && uiElement != _previewShape)
                    {
                        // Начинаем перетаскивание для не-TextBox элементов
                        if (uiElement is Polyline || uiElement is Rectangle || uiElement is Ellipse)
                        {
                            _isDraggingElement = true;
                            _dragElement = uiElement;

                            double left = Canvas.GetLeft(uiElement);
                            double top = Canvas.GetTop(uiElement);
                            if (double.IsNaN(left)) left = 0;
                            if (double.IsNaN(top)) top = 0;

                            _dragOffsetWorld = new Point(world.X - left, world.Y - top);
                            Viewport.CaptureMouse();

                            // Показываем рамку для перемещаемого элемента
                            ShowResizeFrame(uiElement);

                            // ВАЖНО: Прерываем выполнение, чтобы не создавать новую фигуру
                            return;
                        }
                    }
                }

                // 2) Если не было клика по объекту — начинаем панорамирование доски
                StartPan(screen);
                return;
            }

            // 3) Pen — Начало рисования
            if (_tool == ToolMode.Pen && e.LeftButton == MouseButtonState.Pressed)
            {
                StartStroke(world);

                // ВАЖНО: Прерываем выполнение
                return;
            }

            // 4) Rect/Ellipse: клик фиксирует фигуру
            if ((_tool == ToolMode.Rect || _tool == ToolMode.Ellipse) && e.LeftButton == MouseButtonState.Pressed)
            {
                PlaceShapeAt(world);

                // ВАЖНО: Прерываем выполнение
                return;
            }

            // 5) Text: клик ставит textbox
            if (_tool == ToolMode.Text && e.LeftButton == MouseButtonState.Pressed)
            {
                // Проверяем, не кликнули ли по существующему TextBox
                var hitTestResult3 = VisualTreeHelper.HitTest(Viewport, screen);
                if (hitTestResult3 != null && hitTestResult3.VisualHit is TextBox)
                {
                    // Кликнули по существующему TextBox - фокусируемся на нём
                    var textBox = FindParentTextBox(hitTestResult3.VisualHit);
                    if (textBox != null)
                    {
                        textBox.Focus();
                        textBox.SelectAll();
                    }
                }
                else
                {
                    // Кликнули на пустое место - создаём новый TextBox
                    PlaceTextAt(world);
                }

                // ВАЖНО: Прерываем выполнение
                return;
            }
        }

        private void BoardCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // ПРОВЕРЯЕМ: Если мы в режиме создания фигур (не Hand), то блокируем обработку
            if (_tool != ToolMode.Hand)
            {
                e.Handled = true; // Блокируем дальнейшую обработку
                return;
            }

            // Только для режима Hand показываем рамку выделения
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
                        if (uiElement is Shape || uiElement is TextBox)
                        {
                            ShowResizeFrame(uiElement);

                            // Блокируем дальнейшую обработку, чтобы Viewport_MouseDown не создал новую фигуру
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

            // Если изменяем размер - только обрабатываем ресайз
            if (_isResizing && _resizeTarget != null)
            {
                ResizeElement(world);
                return; // Выходим раньше
            }

            // Призрак фигуры следует за мышью
            UpdatePreview(world);

            // Рисование линии
            if (_isDrawing && _currentStroke != null && e.LeftButton == MouseButtonState.Pressed)
            {
                _currentStroke.Points.Add(world);
                var shape = _shapesOnBoard.Find(s => s.Id.ToString() == _currentStroke.Uid);
                if (shape != null)
                {
                    shape.DeserializedPoints.Add(world);
                }
            }

            // Перемещение перетаскиваемого объекта
            if (_isDraggingElement && _dragElement != null)
            {
                MoveElementTo(world);
            }

            // Панорамирование
            if (_isPanning)
            {
                BoardTranslate.X = _panStartX + (screen.X - _panStartScreen.X);
                BoardTranslate.Y = _panStartY + (screen.Y - _panStartScreen.Y);
            }
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isResizing)
            {
                SaveResizedShape();
                _isResizing = false;
                _resizeDirection = null;
                Viewport.ReleaseMouseCapture();
                return;
            }

            // Завершаем перетаскивание не-TexBox элементов
            if (_isDraggingElement && !(_dragElement is TextBox))
            {
                if (_dragElement != null)
                {
                    SaveElementPosition(_dragElement);
                }
                _isDraggingElement = false;
                _dragElement = null;
            }

            // Для рисования (если фигура рисуется)
            if (_isDrawing)
            {
                if (_currentStroke != null)
                {
                    var shape = _shapesOnBoard.Find(s => s.Id.ToString() == _currentStroke.Uid);
                    if (shape != null)
                    {
                        shape.Points = JsonConvert.SerializeObject(_currentStroke.Points);
                        _ = _supabaseService.SaveShapeAsync(shape);
                    }
                }
                _isDrawing = false;
                _currentStroke = null;
            }

            _isPanning = false;
            Viewport.ReleaseMouseCapture();
        }

        // === Перемещение элементов ===
        private void MoveElementTo(Point world)
        {
            if (_dragElement == null) return;

            double offsetX = world.X - _dragOffsetWorld.X;
            double offsetY = world.Y - _dragOffsetWorld.Y;

            // Перемещаем элемент на канвасе
            Canvas.SetLeft(_dragElement, offsetX);
            Canvas.SetTop(_dragElement, offsetY);

            // Если элемент выделен, перемещаем и рамку с ручками
            if (_resizeTarget == _dragElement && _resizeBorder != null)
            {
                // Обновляем рамку
                Canvas.SetLeft(_resizeBorder, offsetX);
                Canvas.SetTop(_resizeBorder, offsetY);

                // Обновляем ручки
                double width, height;

                if (_dragElement is Polyline polyline)
                {
                    // Для линии вычисляем bounding box
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


            // Обновляем координаты в объекте BoardShape
            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _dragElement.Uid);
            if (shape != null)
            {
                // Для фигур (Rectangle, Ellipse) центрируем координаты
                if (_dragElement is Shape visualShape)
                {
                    shape.X = offsetX + shape.Width / 2;
                    shape.Y = offsetY + shape.Height / 2;
                }
                // Для TextBox используем верхний левый угол
                else if (_dragElement is TextBox textBox)
                {
                    shape.X = offsetX;
                    shape.Y = offsetY;
                }
                // Для Polyline обновляем все точки
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
            }
        }

        private async void SaveElementPosition(UIElement element)
        {
            var shape = _shapesOnBoard.Find(s => s.Id.ToString() == element.Uid);
            if (shape != null)
            {
                // Если это текстовое поле, сохраняем координаты
                if (element is TextBox textBox)
                {
                    shape.X = Canvas.GetLeft(textBox);
                    shape.Y = Canvas.GetTop(textBox);
                }
                // Для других фигур (например, прямоугольников или эллипсов)
                else if (element is Polyline polyline)
                {
                    shape.X = Canvas.GetLeft(element);
                    shape.Y = Canvas.GetTop(element);
                }
                else if (element is Shape visualShape)
                {
                    shape.X = Canvas.GetLeft(element) + shape.Width / 2;
                    shape.Y = Canvas.GetTop(element) + shape.Height / 2;
                }

                // Сохраняем в базу данных
                await _supabaseService.SaveShapeAsync(shape);
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
            }
        }

        private async void SaveTextBoxText(TextBox textBox)
        {
            var shape = _shapesOnBoard.Find(s => s.Id.ToString() == textBox.Uid);
            if (shape != null)
            {
                shape.Text = textBox.Text;
                await _supabaseService.SaveShapeAsync(shape);
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
            _isDrawing = true;

            _currentStroke = new Polyline
            {
                Stroke = _currentBrush,              // ✅ выбранный цвет
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
                Color = _currentColorString,   // ✅ сохраняем цвет
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
                int uniqueId = await _supabaseService.GenerateUniqueIdAsync(_boardId);

                // Создаем фигуру в зависимости от инструмента
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
                        Color = _currentColorString,   // Сохраняем выбранный цвет
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
                        Color = _currentColorString,   // Сохраняем выбранный цвет
                        Text = "",
                        Id = uniqueId
                    },
                    _ => null
                };

                if (shape != null)
                {
                    // Добавляем фигуру в локальный список
                    _shapesOnBoard.Add(shape);

                    // Создаем UI элемент для фигуры (Rectangle или Ellipse)
                    UIElement element = shape.Type switch
                    {
                        "rectangle" => new Rectangle
                        {
                            Width = shape.Width,
                            Height = shape.Height,
                            Stroke = _currentBrush,     // Используем выбранный цвет
                            StrokeThickness = 2,
                            Fill = Brushes.Transparent,
                            Uid = shape.Id.ToString()
                        },
                        "ellipse" => new Ellipse
                        {
                            Width = shape.Width,
                            Height = shape.Height,
                            Stroke = _currentBrush,     // Используем выбранный цвет
                            StrokeThickness = 2,
                            Fill = Brushes.Transparent,
                            Uid = shape.Id.ToString()
                        },
                        _ => null
                    };

                    if (element != null)
                    {
                        // Устанавливаем позицию фигуры на канвасе
                        Canvas.SetLeft(element, shape.X - shape.Width / 2);
                        Canvas.SetTop(element, shape.Y - shape.Height / 2);

                        // Добавляем фигуру на канвас
                        BoardCanvas.Children.Add(element);

                        // Сохраняем фигуру в базу данных Supabase
                        await _supabaseService.SaveShapeAsync(shape);

                        // Сбросим цвет инструмента на дефолтный
                        ResetToolColorToDefault();
                    }
                }
            }
            finally
            {
                // Разрешаем создание новых фигур
                _isCreatingShape = false;
            }
        }

        private async void PlaceTextAt(Point world)
        {
            if (_isCreatingShape) return;

            _isCreatingShape = true;

            var tb = new TextBox
            {
                Text = "Текст",
                MinWidth = 120,
                FontSize = 16,
                Background = Brushes.White,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Foreground = _currentBrush,   // ✅ цвет текста
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
                Color = _currentColorString,   // ✅ сохраняем цвет
                Id = uniqueId
            };

            _shapesOnBoard.Add(shape);
            await _supabaseService.SaveShapeAsync(shape);
            ResetToolColorToDefault();

            tb.Focus();
            tb.SelectAll();

            _isCreatingShape = false;
        }

        // ===== РУЧКИ ИЗМЕНЕНИЯ РАЗМЕРА =====

        private void ShowResizeFrame(UIElement element)
        {
            RemoveResizeFrame();
            ColorPanel.Visibility = Visibility.Visible;

            _resizeTarget = element;

            double left, top, width, height;

            if (element is Polyline polyline)
            {
                // Для линии вычисляем bounding box
                if (polyline.Points.Count > 0)
                {
                    double minX = polyline.Points.Min(p => p.X);
                    double maxX = polyline.Points.Max(p => p.X);
                    double minY = polyline.Points.Min(p => p.Y);
                    double maxY = polyline.Points.Max(p => p.Y);

                    left = minX;
                    top = minY;
                    width = Math.Max(maxX - minX, 1); // Убедимся, что ширина хотя бы 1
                    height = Math.Max(maxY - minY, 1); // Убедимся, что высота хотя бы 1
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
                // Для других элементов
                left = Canvas.GetLeft(element);
                top = Canvas.GetTop(element);
                width = ((FrameworkElement)element).ActualWidth;
                height = ((FrameworkElement)element).ActualHeight;

                // Убедимся, что размеры не слишком маленькие
                if (width < 1) width = 1;
                if (height < 1) height = 1;
            }

            // Если значения NaN, используем 0
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            // Создаем рамку выделения
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

            // Создаем ручки
            CreateResizeHandles(left, top, width, height);
        }

        private void RemoveResizeFrame()
        {
            if (_resizeBorder != null)
            {
                BoardCanvas.Children.Remove(_resizeBorder);
                _resizeBorder = null;
            }

            // Удаляем все ручки
            RemoveAllHandles();
            _resizeTarget = null;
            ColorPanel.Visibility = Visibility.Collapsed;
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

            // Позиции для 8 ручек
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

            // Обновляем рамку
            _resizeBorder.Width = w;
            _resizeBorder.Height = h;
            Canvas.SetLeft(_resizeBorder, x);
            Canvas.SetTop(_resizeBorder, y);

            // Обновляем позиции ручек
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
                    // Центрируем ручку относительно точки
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

            // Центрируем ручку относительно точки
            Canvas.SetLeft(handle, x - 4);
            Canvas.SetTop(handle, y - 4);

            // Обработчик для перетаскивания
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

            _resizeDirection = handle.Tag.ToString();
            _isResizing = true;

            // Используем позицию мыши относительно Viewport
            var screenPos = e.GetPosition(Viewport);
            _resizeStartWorld = ScreenToWorld(screenPos);

            if (_resizeTarget is Polyline polyline)
            {
                // Для линии вычисляем bounding box и запоминаем точку, за которую тянем
                if (polyline.Points.Count > 0)
                {
                    // Получаем текущий bounding box
                    double minX = polyline.Points.Min(p => p.X);
                    double maxX = polyline.Points.Max(p => p.X);
                    double minY = polyline.Points.Min(p => p.Y);
                    double maxY = polyline.Points.Max(p => p.Y);

                    // Запоминаем не только размеры, но и положение
                    _startX = minX;
                    _startY = minY;
                    _startW = Math.Max(maxX - minX, 0.1); // Минимум 0.1
                    _startH = Math.Max(maxY - minY, 0.1);

                    // Запоминаем координаты углов
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
                // Для других элементов
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

            // Просто используем разницу в мировых координатах
            double dx = world.X - _resizeStartWorld.X;
            double dy = world.Y - _resizeStartWorld.Y;

            double newX = _startX;
            double newY = _startY;
            double newW = _startW;
            double newH = _startH;

            // Прямое изменение размеров - 1:1 с движением мыши
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

            // Применяем изменения к фигуре
            if (_resizeTarget is Polyline polyline)
            {
                // Для линии используем правильное масштабирование
                ResizePolyline(polyline, newX, newY, newW, newH);
            }
            else
            {
                // Для других фигур (Rectangle, Ellipse)
                var fe = (FrameworkElement)_resizeTarget;
                fe.Width = newW;
                fe.Height = newH;
                Canvas.SetLeft(fe, newX);
                Canvas.SetTop(fe, newY);
            }

            // Обновляем рамку и ручки
            UpdateResizeFrame(newX, newY, newW, newH);
        }

        private void ResizePolyline(Polyline polyline, double newX, double newY, double newW, double newH)
        {
            if (polyline == null || polyline.Points.Count == 0 || _originalCorners == null) return;

            // Определяем, какой угол изменяется
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

            // Получаем текущие границы
            double currentMinX = polyline.Points.Min(p => p.X);
            double currentMaxX = polyline.Points.Max(p => p.X);
            double currentMinY = polyline.Points.Min(p => p.Y);
            double currentMaxY = polyline.Points.Max(p => p.Y);

            double currentWidth = currentMaxX - currentMinX;
            double currentHeight = currentMaxY - currentMinY;

            if (currentWidth <= 0) currentWidth = 0.1;
            if (currentHeight <= 0) currentHeight = 0.1;

            // Создаем новые точки
            PointCollection newPoints = new PointCollection();

            foreach (var point in polyline.Points)
            {
                // Нормализуем координату точки (0-1)
                double normalizedX = (point.X - currentMinX) / currentWidth;
                double normalizedY = (point.Y - currentMinY) / currentHeight;

                // Преобразуем в новые координаты
                double newPointX = newX + normalizedX * newW;
                double newPointY = newY + normalizedY * newH;

                newPoints.Add(new Point(newPointX, newPointY));
            }

            // Обновляем точки
            polyline.Points = newPoints;
        }

        private async void SaveResizedShape()
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

            await _supabaseService.SaveShapeAsync(shape);
        }
        private async void Color_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                var selectedBrush = btn.Background;
                var selectedColorString = btn.Background.ToString();

                // Если есть выделенная фигура — меняем только её цвет
                if (_resizeTarget != null)
                {
                    await ApplyColorToElement(_resizeTarget, selectedBrush, selectedColorString);
                }
                else
                {
                    // Иначе меняем цвет инструмента
                    _currentBrush = selectedBrush;
                    _currentColorString = selectedColorString;
                }
            }
        }
        private async Task ApplyColorToElement(
            UIElement element,
            Brush brush,
            string colorString)
        {
            if (element is Shape shapeElement)
                shapeElement.Stroke = brush;

            else if (element is Polyline polyline)
                polyline.Stroke = brush;

            else if (element is TextBox tb)
                tb.Foreground = brush;

            var shape = _shapesOnBoard
                .FirstOrDefault(s => s.Id.ToString() == element.Uid);

            if (shape != null)
            {
                shape.Color = colorString;
                await _supabaseService.SaveShapeAsync(shape);
            }
        }
        private void ResetToolColorToDefault()
        {
            _currentBrush = Brushes.Black;
            _currentColorString = Brushes.Black.ToString();
        }


    }
}