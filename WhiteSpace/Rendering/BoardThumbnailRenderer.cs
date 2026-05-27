using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Newtonsoft.Json;
using WhiteSpace.Models;

namespace WhiteSpace.Rendering;

public static class BoardThumbnailRenderer
{
    public const int DefaultWidth = 360;
    public const int DefaultHeight = 96;

    private static readonly string CacheDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WhiteSpace",
        "thumbnails");

    public static string GetCacheImagePath(Guid boardId) =>
        System.IO.Path.Combine(CacheDirectory, $"{boardId:N}.png");

    private static string GetCacheMetaPath(Guid boardId) =>
        System.IO.Path.Combine(CacheDirectory, $"{boardId:N}.meta");

    public static string ComputeFingerprint(IReadOnlyList<BoardShape> shapes)
    {
        if (shapes.Count == 0)
        {
            return "empty";
        }

        var builder = new StringBuilder();
        foreach (var shape in shapes.OrderBy(s => s.Id))
        {
            builder.Append(shape.Id).Append('|')
                .Append(shape.Type).Append('|')
                .Append(shape.X.ToString("F1", CultureInfo.InvariantCulture)).Append('|')
                .Append(shape.Y.ToString("F1", CultureInfo.InvariantCulture)).Append('|')
                .Append(shape.Width.ToString("F1", CultureInfo.InvariantCulture)).Append('|')
                .Append(shape.Height.ToString("F1", CultureInfo.InvariantCulture)).Append('|')
                .Append(shape.Color).Append('|')
                .Append(shape.Text).Append('|')
                .Append(shape.Points).Append(';');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }

    public static ImageSource? TryLoadCached(Guid boardId, string fingerprint)
    {
        try
        {
            var imagePath = GetCacheImagePath(boardId);
            var metaPath = GetCacheMetaPath(boardId);
            if (!File.Exists(imagePath) || !File.Exists(metaPath))
            {
                return null;
            }

            var cachedFingerprint = File.ReadAllText(metaPath).Trim();
            if (!string.Equals(cachedFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return null;
            }

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

    public static void SaveCache(Guid boardId, string fingerprint, ImageSource source)
    {
        if (source is not BitmapSource bitmapSource)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var imagePath = GetCacheImagePath(boardId);
            var metaPath = GetCacheMetaPath(boardId);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            using (var stream = File.Create(imagePath))
            {
                encoder.Save(stream);
            }

            File.WriteAllText(metaPath, fingerprint);
        }
        catch
        {
            // cache is best-effort
        }
    }

    public static ImageSource? Render(IReadOnlyList<BoardShape> shapes, int width = DefaultWidth, int height = DefaultHeight)
    {
        if (shapes.Count == 0)
        {
            return null;
        }

        if (!TryGetContentBounds(shapes, out var minX, out var minY, out var contentW, out var contentH))
        {
            return null;
        }

        var canvas = new Canvas
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
            SnapsToDevicePixels = true
        };

        var scale = Math.Min(width / contentW, height / contentH) * 0.88;
        var offsetX = (width - contentW * scale) / 2 - minX * scale;
        var offsetY = (height - contentH * scale) / 2 - minY * scale;

        foreach (var shape in shapes.Take(400))
        {
            var element = CreateThumbnailElement(shape, scale, offsetX, offsetY);
            if (element != null)
            {
                canvas.Children.Add(element);
            }
        }

        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        canvas.UpdateLayout();

        var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(canvas);
        renderTarget.Freeze();
        return renderTarget;
    }

    public static async Task<ImageSource?> EnsureThumbnailAsync(
        Guid boardId,
        IReadOnlyList<BoardShape> shapes,
        bool allowDiskCache = true)
    {
        if (shapes.Count == 0)
        {
            return null;
        }

        var fingerprint = ComputeFingerprint(shapes);
        if (allowDiskCache)
        {
            var cached = TryLoadCached(boardId, fingerprint);
            if (cached != null)
            {
                return cached;
            }
        }

        var rendered = await RenderOnUiThreadAsync(shapes);
        if (rendered != null && allowDiskCache)
        {
            SaveCache(boardId, fingerprint, rendered);
        }

        return rendered;
    }

    private static Task<ImageSource?> RenderOnUiThreadAsync(IReadOnlyList<BoardShape> shapes)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            return Task.FromResult(Render(shapes));
        }

        return dispatcher.InvokeAsync(() => Render(shapes), DispatcherPriority.Background).Task;
    }

    private static bool TryGetContentBounds(
        IReadOnlyList<BoardShape> shapes,
        out double minX,
        out double minY,
        out double width,
        out double height)
    {
        minX = double.MaxValue;
        minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var shape in shapes)
        {
            if (shape.Type is "line" or "marker" or "connector")
            {
                var points = DeserializePoints(shape);
                if (points.Count == 0)
                {
                    continue;
                }

                foreach (var point in points)
                {
                    minX = Math.Min(minX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxX = Math.Max(maxX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                }

                continue;
            }

            var (left, top, right, bottom) = GetAxisAlignedBounds(shape);
            minX = Math.Min(minX, left);
            minY = Math.Min(minY, top);
            maxX = Math.Max(maxX, right);
            maxY = Math.Max(maxY, bottom);
        }

        if (minX == double.MaxValue)
        {
            minX = minY = 0;
            width = height = 1;
            return false;
        }

        width = Math.Max(maxX - minX, 48);
        height = Math.Max(maxY - minY, 48);
        return true;
    }

    private static (double Left, double Top, double Right, double Bottom) GetAxisAlignedBounds(BoardShape shape)
    {
        var w = Math.Max(shape.Width, 8);
        var h = Math.Max(shape.Height, 8);

        return shape.Type switch
        {
            "rectangle" or "ellipse" or "stickyNote" or "image" or "boardImage" =>
                (shape.X - w / 2, shape.Y - h / 2, shape.X + w / 2, shape.Y + h / 2),
            "text" or "comment" =>
                (shape.X, shape.Y, shape.X + w, shape.Y + h),
            _ => (shape.X - w / 2, shape.Y - h / 2, shape.X + w / 2, shape.Y + h / 2)
        };
    }

    private static UIElement? CreateThumbnailElement(BoardShape shape, double scale, double offsetX, double offsetY)
    {
        var brush = CreateBrush(shape.Color, Colors.SlateGray);

        switch (shape.Type)
        {
            case "rectangle":
            case "ellipse":
            {
                var w = Math.Max(shape.Width, 12) * scale;
                var h = Math.Max(shape.Height, 12) * scale;
                var left = shape.X * scale + offsetX - w / 2;
                var top = shape.Y * scale + offsetY - h / 2;
                var stroke = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
                if (shape.Type == "ellipse")
                {
                    var ellipse = new Ellipse
                    {
                        Width = w,
                        Height = h,
                        Fill = brush,
                        Stroke = stroke,
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(ellipse, left);
                    Canvas.SetTop(ellipse, top);
                    return ellipse;
                }

                var appearance = RectEllipseAppearance.Parse(shape);
                if (string.IsNullOrWhiteSpace(appearance.ShapeKind) || appearance.ShapeKind == "rect")
                {
                    var rect = new Rectangle
                    {
                        Width = w,
                        Height = h,
                        Fill = brush,
                        Stroke = stroke,
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(rect, left);
                    Canvas.SetTop(rect, top);
                    return rect;
                }

                if (appearance.ShapeKind == "roundRect")
                {
                    var radius = Math.Min(w, h) * 0.15;
                    var rounded = new Rectangle
                    {
                        Width = w,
                        Height = h,
                        RadiusX = radius,
                        RadiusY = radius,
                        Fill = brush,
                        Stroke = stroke,
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(rounded, left);
                    Canvas.SetTop(rounded, top);
                    return rounded;
                }

                var path = new System.Windows.Shapes.Path
                {
                    Width = w,
                    Height = h,
                    Stretch = Stretch.Fill,
                    Data = BoardShapeOutlineGeometry.Get(appearance.ShapeKind),
                    Fill = brush,
                    Stroke = stroke,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(path, left);
                Canvas.SetTop(path, top);
                return path;
            }
            case "stickyNote":
            {
                var w = Math.Max(shape.Width, 48) * scale;
                var h = Math.Max(shape.Height, 48) * scale;
                var left = shape.X * scale + offsetX - w / 2;
                var top = shape.Y * scale + offsetY - h / 2;
                var rect = new Rectangle
                {
                    Width = w,
                    Height = h,
                    RadiusX = 4 * scale,
                    RadiusY = 4 * scale,
                    Fill = brush,
                    Stroke = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                    StrokeThickness = 1
                };
                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);
                return rect;
            }
            case "text":
            {
                var w = Math.Max(shape.Width, 40) * scale;
                var h = Math.Max(shape.Height, 18) * scale;
                var left = shape.X * scale + offsetX;
                var top = shape.Y * scale + offsetY;
                var rect = new Rectangle
                {
                    Width = w,
                    Height = h,
                    Fill = Brushes.Transparent,
                    Stroke = brush,
                    StrokeThickness = Math.Max(1, 1.5 * scale)
                };
                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);
                return rect;
            }
            case "line":
            case "marker":
            case "connector":
            {
                var points = DeserializePoints(shape);
                if (points.Count < 2)
                {
                    return null;
                }

                var polyline = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = Math.Max(1, (shape.Type == "marker" ? 4 : 2) * scale),
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };

                foreach (var point in points)
                {
                    polyline.Points.Add(new Point(point.X * scale + offsetX, point.Y * scale + offsetY));
                }

                return polyline;
            }
            case "image":
            case "boardImage":
            {
                var w = Math.Max(shape.Width, 24) * scale;
                var h = Math.Max(shape.Height, 24) * scale;
                var left = shape.X * scale + offsetX - w / 2;
                var top = shape.Y * scale + offsetY - h / 2;
                var rect = new Rectangle
                {
                    Width = w,
                    Height = h,
                    Fill = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                    Stroke = brush,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);
                return rect;
            }
            case "comment":
            {
                var size = Math.Max(16, 20 * scale);
                var left = shape.X * scale + offsetX;
                var top = shape.Y * scale + offsetY;
                var ellipse = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = brush,
                    Stroke = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                    StrokeThickness = 1
                };
                Canvas.SetLeft(ellipse, left);
                Canvas.SetTop(ellipse, top);
                return ellipse;
            }
            default:
            {
                var w = Math.Max(shape.Width, 16) * scale;
                var h = Math.Max(shape.Height, 16) * scale;
                var left = shape.X * scale + offsetX - w / 2;
                var top = shape.Y * scale + offsetY - h / 2;
                var rect = new Rectangle
                {
                    Width = w,
                    Height = h,
                    Fill = brush,
                    Stroke = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                    StrokeThickness = 1
                };
                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);
                return rect;
            }
        }
    }

    private static List<Point> DeserializePoints(BoardShape shape)
    {
        if (shape.DeserializedPoints is { Count: > 0 })
        {
            return shape.DeserializedPoints;
        }

        if (string.IsNullOrWhiteSpace(shape.Points))
        {
            return new List<Point>();
        }

        try
        {
            return JsonConvert.DeserializeObject<List<Point>>(shape.Points) ?? new List<Point>();
        }
        catch
        {
            return new List<Point>();
        }
    }

    private static Brush CreateBrush(string? color, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(color)
                && new BrushConverter().ConvertFromString(color.Trim()) is SolidColorBrush brush)
            {
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
            // ignore invalid color
        }

        var fallbackBrush = new SolidColorBrush(fallback);
        fallbackBrush.Freeze();
        return fallbackBrush;
    }
}
