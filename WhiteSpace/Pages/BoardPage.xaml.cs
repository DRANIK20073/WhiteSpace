using System;
using System.Collections.Generic;
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

        // Перетаскивание объектов
        private bool _isDraggingElement;
        private UIElement _dragElement;
        private Point _dragOffsetWorld;

        public BoardPage(Guid boardId)
        {
            InitializeComponent();
            _boardId = boardId;
            _supabaseService = new SupabaseService();
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
                // Добавляем фигуры на доску
                if (shape.Type == "line")
                {
                    var polyline = new Polyline
                    {
                        Stroke = Brushes.Black,
                        StrokeThickness = 2,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };

                    // Добавляем все точки из shape.Points
                    var points = JsonConvert.DeserializeObject<List<Point>>(shape.Points);
                    foreach (var point in points)
                    {
                        polyline.Points.Add(new Point(point.X, point.Y));
                    }

                    // Добавляем polyline на BoardCanvas
                    BoardCanvas.Children.Add(polyline);
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
                            Fill = Brushes.Transparent
                        },
                        "ellipse" => new Ellipse
                        {
                            Width = shape.Width,
                            Height = shape.Height,
                            Stroke = Brushes.Black,
                            StrokeThickness = 2,
                            Fill = Brushes.Transparent
                        },
                        _ => null
                    };

                    if (element != null)
                    {
                        // Проставляем координаты для фигур
                        Canvas.SetLeft(element, shape.X - shape.Width / 2);
                        Canvas.SetTop(element, shape.Y - shape.Height / 2);

                        // Добавляем на BoardCanvas
                        BoardCanvas.Children.Add(element);
                        // Добавляем фигуру в коллекцию
                        _shapesOnBoard.Add(shape);
                    }
                }
            }
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

            // Курсор
            Cursor = _tool switch
            {
                ToolMode.Hand => Cursors.Hand,
                ToolMode.Pen => Cursors.Pen,
                ToolMode.Text => Cursors.IBeam,
                _ => Cursors.Cross
            };
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

            // 1) Если клик по объекту — начинаем перетаскивание (в режиме Hand)
            if (_tool == ToolMode.Hand)
            {
                if (TryStartDragElement(e, world))
                {
                    return; // Если объект перетаскивается, не продолжаем панорамирование
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
                PlaceTextAt(world);
                return;
            }
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
                _currentStroke.Points.Add(world);  // Добавляем точку в линию

                // Сохраняем точку в BoardShape
                var shape = _shapesOnBoard.Last();
                shape.DeserializedPoints.Add(world);  // Добавляем точку в коллекцию точек линии
            }

            // Перемещение перетаскиваемого объекта
            if (_isDraggingElement)
            {
                MoveElementTo(world);  // Перемещаем элемент
            }
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isPanning = false;

            if (_isDraggingElement)
            {
                _isDraggingElement = false;
                _dragElement = null;
            }

            if (_isDrawing)
            {
                _isDrawing = false;
                _currentStroke = null;
            }

            Viewport.ReleaseMouseCapture();
        }

        // Zoom относительно курсора
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

            // Компенсируем, чтобы точка под курсором оставалась под курсором
            BoardTranslate.X += (after.X - before.X) * scale;
            BoardTranslate.Y += (after.Y - before.Y) * scale;
        }

        // === Пан ===
        private void StartPan(Point screen)
        {
            _isPanning = true;
            _panStartScreen = screen;
            _panStartX = BoardTranslate.X;
            _panStartY = BoardTranslate.Y;
            Viewport.CaptureMouse();
        }

        // === Рисование ===
        // Рисование линии (карандаш)
        private void StartStroke(Point startWorld)
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

            // Сохраняем начальную точку линии в BoardShape
            var shape = new BoardShape
            {
                BoardId = _boardId,
                Type = "line",
                X = startWorld.X,
                Y = startWorld.Y,
                Color = "black"
            };
            shape.DeserializedPoints.Add(startWorld);  // Добавляем первую точку

            _shapesOnBoard.Add(shape);  // Добавляем фигуру (линию) в коллекцию

            Viewport.CaptureMouse();
        }

        // === Призрак фигуры ===
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
            // Генерируем уникальный id для новой фигуры
            int uniqueId = await _supabaseService.GenerateUniqueIdAsync(_boardId);

            // Создаем BoardShape с уникальным id
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
                    Id = uniqueId // Устанавливаем уникальный Id
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
                    Id = uniqueId // Устанавливаем уникальный Id
                },
                _ => null
            };

            if (shape != null)
            {
                _shapesOnBoard.Add(shape);  // Добавляем фигуру в коллекцию

                // Создаем визуальный элемент для отображения на канвасе
                UIElement element = shape.Type switch
                {
                    "rectangle" => new Rectangle
                    {
                        Width = shape.Width,
                        Height = shape.Height,
                        Stroke = Brushes.Black,
                        StrokeThickness = 2,
                        Fill = Brushes.Transparent,
                        Uid = shape.Id.ToString() // Устанавливаем Id для элемента
                    },
                    "ellipse" => new Ellipse
                    {
                        Width = shape.Width,
                        Height = shape.Height,
                        Stroke = Brushes.Black,
                        StrokeThickness = 2,
                        Fill = Brushes.Transparent,
                        Uid = shape.Id.ToString() // Устанавливаем Id для элемента
                    },
                    _ => null
                };

                if (element != null)
                {
                    Canvas.SetLeft(element, shape.X - shape.Width / 2);
                    Canvas.SetTop(element, shape.Y - shape.Height / 2);

                    BoardCanvas.Children.Add(element);  // Добавляем визуальный элемент на канвас
                }
            }
        }

        // === Текст ===
        private void PlaceTextAt(Point world)
        {
            var tb = new TextBox
            {
                Text = "",
                MinWidth = 120,
                FontSize = 16,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1)
            };

            Canvas.SetLeft(tb, world.X);
            Canvas.SetTop(tb, world.Y);

            BoardCanvas.Children.Add(tb);
            tb.Focus();
        }

        // === Выбор и перетаскивание объектов ===
        private bool TryStartDragElement(MouseButtonEventArgs e, Point world)
        {
            DependencyObject src = (DependencyObject)e.OriginalSource;

            while (src != null && src is not UIElement)
                src = VisualTreeHelper.GetParent(src);

            UIElement el = src as UIElement;

            if (el == null || el == BoardCanvas || el == Viewport)
                return false;

            if (!BoardCanvas.Children.Contains(el))
                return false;

            _isDraggingElement = true;
            _dragElement = el;

            double left = Canvas.GetLeft(el);
            double top = Canvas.GetTop(el);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            _dragOffsetWorld = new Point(world.X - left, world.Y - top);

            Viewport.CaptureMouse();
            return true;
        }

        private void MoveElementTo(Point world)
        {
            if (_dragElement == null) return;

            // Ищем фигуру по Id, который был установлен в BoardShape
            var shape = _shapesOnBoard.FirstOrDefault(s => s.Id.ToString() == _dragElement.Uid); // Преобразуем Id в строку для корректного сравнения

            if (shape != null)
            {
                // Обновляем координаты фигуры в коллекции
                shape.X = world.X - _dragOffsetWorld.X;
                shape.Y = world.Y - _dragOffsetWorld.Y;

                // Обновляем положение фигуры на канвасе
                Canvas.SetLeft(_dragElement, world.X - _dragOffsetWorld.X);
                Canvas.SetTop(_dragElement, world.Y - _dragOffsetWorld.Y);
            }
        }

        // Метод для сохранения всех изменений на доске
        private async void SaveBoardChanges_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Проходим по всем фигурам на доске и сохраняем их
                foreach (var shape in _shapesOnBoard)
                {
                    // Для каждой фигуры проверяем, нужно ли её обновить или создать
                    var result = await _supabaseService.SaveShapeAsync(shape);

                    if (!result)
                    {
                        MessageBox.Show("Ошибка при сохранении изменений.");
                        return;
                    }
                }

                MessageBox.Show("Все изменения успешно сохранены.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении: {ex.Message}");
            }
        }

    }
}
