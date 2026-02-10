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

namespace WhiteSpace.Pages
{
    public partial class BoardPage : Page
    {
        private List<BoardShape> _shapesOnBoard = new List<BoardShape>();
        private readonly Guid _boardId;
        private SupabaseService _supabaseService;

        private enum ToolMode { Hand, Pen, Rect, Ellipse, Text }
        private ToolMode _tool = ToolMode.Hand;

        // Пан/камера
        private bool _isPanning;
        private Point _panStartScreen;
        private double _panStartX, _panStartY;

        // Рисование (карандаш)
        private bool _isDrawing;
        private Polyline _currentStroke;

        // Призрак фигуры
        private Shape _previewShape;
        private const double DefaultRectW = 140;
        private const double DefaultRectH = 90;
        private const double DefaultEllipse = 100;

        // Изменение размеров фигру
        private bool _isResizing;
        private UIElement _resizeTarget;
        private Rectangle _resizeBorder;
        private string _resizeDirection; // "nw", "n", "ne", "e", "se", "s", "sw", "w"
        private Point _resizeStartWorld;
        private double _startW, _startH, _startX, _startY;

        // Перетаскивание объектов
        private bool _isDraggingElement;
        private UIElement _dragElement;
        private Point _dragOffsetWorld;
        private Point _dragStartWorld;
        private bool _wasTextEditingEnabled;

        public BoardPage(Guid boardId)
        {
            InitializeComponent();
            _boardId = boardId;
            _supabaseService = new SupabaseService();

            // Добавляем обработчик для событий мыши на Canvas, чтобы перехватывать клики по TextBox
            BoardCanvas.MouseDown += BoardCanvas_MouseDown;
            BoardCanvas.PreviewMouseDown += BoardCanvas_PreviewMouseDown;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Центрируем доску
            var viewportCenter = new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2);
            var canvasCenter = new Point(BoardCanvas.Width / 2, BoardCanvas.Height / 2);

            BoardTranslate.X = viewportCenter.X - canvasCenter.X;
            BoardTranslate.Y = viewportCenter.Y - canvasCenter.Y;

            // Устанавливаем начальный инструмент (например, "Рука")
            SetTool(ToolMode.Hand);

            // Загружаем все фигуры с базы данных
            var shapes = await _supabaseService.LoadBoardShapesAsync(_boardId);

            // Добавляем загруженные фигуры на канвас
            foreach (var shape in shapes)
            {
                AddShapeToCanvas(shape);
            }
        }

        private void AddShapeToCanvas(BoardShape shape)
        {
            if (shape.Type == "line")
            {
                var polyline = new Polyline
                {
                    Stroke = Brushes.Black,
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
                    Uid = shape.Id.ToString(),
                    IsReadOnly = false,
                    Focusable = true
                };

                // Обработчики событий для TextBox
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
                        Stroke = Brushes.Black,
                        StrokeThickness = 2,
                        Fill = Brushes.Transparent,
                        Uid = shape.Id.ToString()
                    },
                    "ellipse" => new Ellipse
                    {
                        Width = shape.Width,
                        Height = shape.Height,
                        Stroke = Brushes.Black,
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

        // === Обработчики событий для TextBox ===
        private void TextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            if (_tool == ToolMode.Hand && e.LeftButton == MouseButtonState.Pressed)
            {
                // В режиме руки начинаем перетаскивание TextBox
                var world = ScreenToWorld(e.GetPosition(Viewport));
                _dragStartWorld = world;

                // Запоминаем состояние редактирования
                _wasTextEditingEnabled = !textBox.IsReadOnly;

                // Временно блокируем редактирование для перетаскивания
                textBox.IsReadOnly = true;
                textBox.Cursor = Cursors.SizeAll;

                // Начинаем перетаскивание
                _isDraggingElement = true;
                _dragElement = textBox;

                double left = Canvas.GetLeft(textBox);
                double top = Canvas.GetTop(textBox);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                _dragOffsetWorld = new Point(world.X - left, world.Y - top);

                Viewport.CaptureMouse();
                e.Handled = true; // Предотвращаем дальнейшую обработку
            }
        }

        private void TextBox_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            if (_isDraggingElement && _dragElement == textBox)
            {
                // Завершаем перетаскивание
                _isDraggingElement = false;
                _dragElement = null;

                // Восстанавливаем возможность редактирования
                textBox.IsReadOnly = false;
                textBox.Cursor = Cursors.IBeam;

                Viewport.ReleaseMouseCapture();

                // Сохраняем позицию
                SaveTextBoxPosition(textBox);
                e.Handled = true;
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox != null)
            {
                // Сохраняем текст при потере фокуса
                SaveTextBoxText(textBox);
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox != null && textBox.IsFocused)
            {
                // Можно добавить автосохранение или другие действия
            }
        }

        private void BoardCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Этот обработчик срабатывает до всех остальных
            // Полезно для отладки
        }

        private void BoardCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Этот обработчик может перехватывать события от элементов на Canvas
        }

        // === Инструменты ===
        private void Hand_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Hand);
        private void Pen_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Pen);
        private void Rect_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Rect);
        private void Ellipse_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Ellipse);
        private void Text_Click(object sender, RoutedEventArgs e) => SetTool(ToolMode.Text);

        private void SetTool(ToolMode tool)
        {
            _tool = tool;

            // Сброс состояний
            _isDrawing = false;
            _currentStroke = null;
            _isPanning = false;
            _isDraggingElement = false;
            _dragElement = null;

            // Убираем старую фигуру-призрак
            RemovePreviewShape();

            // Призрак включаем только для фигур
            if (_tool == ToolMode.Rect || _tool == ToolMode.Ellipse)
            {
                EnsurePreviewShape();
            }

            // Курсор для Viewport
            Viewport.Cursor = _tool switch
            {
                ToolMode.Hand => Cursors.Hand,
                ToolMode.Pen => Cursors.Pen,
                ToolMode.Text => Cursors.IBeam,
                _ => Cursors.Cross
            };

            // Настройка TextBox в зависимости от режима
            foreach (var child in BoardCanvas.Children)
            {
                if (child is TextBox textBox)
                {
                    if (_tool == ToolMode.Hand)
                    {
                        // В режиме руки можно перетаскивать, но не редактировать сразу
                        textBox.IsReadOnly = true;
                        textBox.Cursor = Cursors.Hand;
                        textBox.Background = Brushes.Transparent;
                    }
                    else if (_tool == ToolMode.Text)
                    {
                        // В режиме текста можно редактировать
                        textBox.IsReadOnly = false;
                        textBox.Cursor = Cursors.IBeam;
                        textBox.Background = Brushes.White;
                    }
                    else
                    {
                        // В других режимах блокируем всё
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
        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Viewport.Focus();
            var screen = e.GetPosition(Viewport);
            var world = ScreenToWorld(screen);

            // Если уже перетаскиваем элемент, не начинаем панорамирование
            if (_isDraggingElement)
                return;

            // 1) Если клик по объекту — начинаем перетаскивание (в режиме Hand)
            if (_tool == ToolMode.Hand)
            {
                // Проверяем клик по не-TexBox элементам
                var hitTestResult = VisualTreeHelper.HitTest(Viewport, screen);
                if (hitTestResult != null)
                {
                    var hitElement = hitTestResult.VisualHit;

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
                return;
            }

            // 4) Rect/Ellipse: клик фиксирует фигуру
            if ((_tool == ToolMode.Rect || _tool == ToolMode.Ellipse) && e.LeftButton == MouseButtonState.Pressed)
            {
                PlaceShapeAt(world);
                return;
            }

            // 5) Text: клик ставит textbox
            if (_tool == ToolMode.Text && e.LeftButton == MouseButtonState.Pressed)
            {
                // Проверяем, не кликнули ли по существующему TextBox
                var hitTestResult = VisualTreeHelper.HitTest(Viewport, screen);
                if (hitTestResult != null && hitTestResult.VisualHit is TextBox)
                {
                    // Кликнули по существующему TextBox - фокусируемся на нём
                    var textBox = FindParentTextBox(hitTestResult.VisualHit);
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
                return;
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

            if (_isResizing && _resizeTarget != null)
            {
                ResizeElement(world);
                return;
            }
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // Завершаем перетаскивание не-TexBox элементов
            if (_isDraggingElement && !(_dragElement is TextBox))
            {
                if (_dragElement != null)
                {
                    SaveElementPosition(_dragElement);  // Сохраняем новое положение
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

            if (_isResizing)
            {
                SaveResizedShape();
                _isResizing = false;
                Viewport.ReleaseMouseCapture();
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

            // Обновляем координаты в объекте BoardShape
            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _dragElement.Uid);
            if (shape != null)
            {
                shape.X = offsetX;
                shape.Y = offsetY;

                // Если это линия (Polyline), обновляем все точки
                if (_dragElement is Polyline polyline)
                {
                    // Обновляем список точек для полилинии
                    var newPoints = new List<Point>();
                    foreach (var point in polyline.Points)
                    {
                        // Сдвигаем каждую точку с учетом новой позиции
                        newPoints.Add(new Point(point.X + offsetX, point.Y + offsetY));
                    }

                    // Обновляем DeserializedPoints
                    shape.DeserializedPoints = newPoints;

                    // Сериализуем обновленные точки в строку JSON
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
                Stroke = Brushes.Black,
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
                Color = "black",
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
                    Color = "black",
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
                    Color = "black",
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
                        Stroke = Brushes.Black,
                        StrokeThickness = 2,
                        Fill = Brushes.Transparent,
                        Uid = shape.Id.ToString()
                    },
                    "ellipse" => new Ellipse
                    {
                        Width = shape.Width,
                        Height = shape.Height,
                        Stroke = Brushes.Black,
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
                    await _supabaseService.SaveShapeAsync(shape);
                }
            }
        }

        private async void PlaceTextAt(Point world)
        {
            var tb = new TextBox
            {
                Text = "Текст",
                MinWidth = 120,
                FontSize = 16,
                Background = Brushes.White,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                IsReadOnly = false,
                Focusable = true,
                Cursor = Cursors.IBeam
            };

            // Добавляем обработчики
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
                Id = uniqueId
            };

            _shapesOnBoard.Add(shape);
            await _supabaseService.SaveShapeAsync(shape);

            tb.Focus();
            tb.SelectAll();
        }

        //Ручки для изменения размеров фигуры
        private void AddHandle(double x, double y, string dir)
        {
            var handle = new Rectangle
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.White,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 1,
                Cursor = GetResizeCursor(dir),
                Tag = dir
            };

            handle.MouseDown += ResizeHandle_MouseDown;

            Canvas.SetLeft(handle, x - 4);
            Canvas.SetTop(handle, y - 4);

            BoardCanvas.Children.Add(handle);
        }

        private void AddResizeHandles(double x, double y, double w, double h)
        {
            AddHandle(x, y, "nw");
            AddHandle(x + w / 2, y, "n");
            AddHandle(x + w, y, "ne");

            AddHandle(x + w, y + h / 2, "e");

            AddHandle(x + w, y + h, "se");
            AddHandle(x + w / 2, y + h, "s");
            AddHandle(x, y + h, "sw");

            AddHandle(x, y + h / 2, "w");
        }

        private void ShowResizeFrame(UIElement element)
        {
            RemoveResizeFrame();

            _resizeTarget = element;

            double left = Canvas.GetLeft(element);
            double top = Canvas.GetTop(element);
            double width = ((FrameworkElement)element).ActualWidth;
            double height = ((FrameworkElement)element).ActualHeight;

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

            AddResizeHandles(left, top, width, height);
        }

        private void RemoveResizeFrame()
        {
            if (_resizeBorder != null)
            {
                BoardCanvas.Children.Remove(_resizeBorder);
                _resizeBorder = null;
            }

            // удаляем все хэндлы
            var handles = BoardCanvas.Children
                .OfType<Rectangle>()
                .Where(r => r.Tag is string s && s.Length <= 2)
                .ToList();

            foreach (var h in handles)
                BoardCanvas.Children.Remove(h);

            _resizeTarget = null;
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

        private void UpdateResizeFrame(double x, double y, double w, double h)
        {
            if (_resizeBorder == null) return;

            _resizeBorder.Width = w;
            _resizeBorder.Height = h;

            Canvas.SetLeft(_resizeBorder, x);
            Canvas.SetTop(_resizeBorder, y);

            // пересоздаём хэндлы
            var oldHandles = BoardCanvas.Children
                .OfType<Rectangle>()
                .Where(r => r.Tag is string s && s.Length <= 2)
                .ToList();

            foreach (var hnd in oldHandles)
                BoardCanvas.Children.Remove(hnd);

            AddResizeHandles(x, y, w, h);
        }

        private void ResizeHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_resizeTarget == null) return;

            var handle = sender as FrameworkElement;
            _resizeDirection = handle.Tag.ToString();
            _isResizing = true;

            var world = ScreenToWorld(e.GetPosition(Viewport));
            _resizeStartWorld = world;

            _startX = Canvas.GetLeft(_resizeTarget);
            _startY = Canvas.GetTop(_resizeTarget);
            _startW = ((FrameworkElement)_resizeTarget).ActualWidth;
            _startH = ((FrameworkElement)_resizeTarget).ActualHeight;

            Viewport.CaptureMouse();
            e.Handled = true;
        }

        private void ResizeElement(Point world)
        {
            double dx = world.X - _resizeStartWorld.X;
            double dy = world.Y - _resizeStartWorld.Y;

            double newX = _startX;
            double newY = _startY;
            double newW = _startW;
            double newH = _startH;

            if (_resizeDirection.Contains("e")) newW += dx;
            if (_resizeDirection.Contains("s")) newH += dy;
            if (_resizeDirection.Contains("w"))
            {
                newW -= dx;
                newX += dx;
            }
            if (_resizeDirection.Contains("n"))
            {
                newH -= dy;
                newY += dy;
            }

            if (newW < 20 || newH < 20) return;

            var fe = (FrameworkElement)_resizeTarget;
            fe.Width = newW;
            fe.Height = newH;

            Canvas.SetLeft(fe, newX);
            Canvas.SetTop(fe, newY);

            UpdateResizeFrame(newX, newY, newW, newH);
        }

        private async void SaveResizedShape()
        {
            var shape = _shapesOnBoard
                .FirstOrDefault(s => s.Id.ToString() == _resizeTarget.Uid);

            if (shape == null) return;

            shape.Width = ((FrameworkElement)_resizeTarget).ActualWidth;
            shape.Height = ((FrameworkElement)_resizeTarget).ActualHeight;
            shape.X = Canvas.GetLeft(_resizeTarget) + shape.Width / 2;
            shape.Y = Canvas.GetTop(_resizeTarget) + shape.Height / 2;

            await _supabaseService.SaveShapeAsync(shape);
        }
    }
}