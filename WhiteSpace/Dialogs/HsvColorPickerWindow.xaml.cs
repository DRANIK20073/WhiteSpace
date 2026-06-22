using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WhiteSpace.Helpers;

namespace WhiteSpace.Dialogs;

/// <summary>HSV-пикер с ползунком hue, плоскостью S/V и пипеткой.</summary>
public partial class HsvColorPickerWindow : Window
{
    private double _h;
    private double _s = 1;
    private double _v = 1;
    private bool _suppress;
    private bool _dragSb;
    private readonly WriteableBitmap _sbBitmap;

    public string? SelectedHex { get; private set; }

    public HsvColorPickerWindow(Color initial, string title)
    {
        InitializeComponent();
        Title = title;
        HsvColorHelper.RgbToHsv(initial.R, initial.G, initial.B, out _h, out _s, out _v);

        _sbBitmap = new WriteableBitmap(220, 180, 96, 96, PixelFormats.Bgra32, null);
        SbImage.Source = _sbBitmap;

        _suppress = true;
        HueSlider.Value = _h;
        HexBox.Text = ToHex(initial);
        _suppress = false;

        RenderSbPlane();
        Loaded += (_, _) => UpdateSbRing();
        SbBorder.SizeChanged += (_, _) => UpdateSbRing();
        UpdateSbRing();
    }

    private static string ToHex(Color c)
    {
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private void HueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress)
        {
            return;
        }

        _h = e.NewValue;
        RenderSbPlane();
        PushRgbFromHsv();
    }

    /// <summary>Рисуем bitmap S/V при текущем hue.</summary>
    private void RenderSbPlane()
    {
        var w = _sbBitmap.PixelWidth;
        var h = _sbBitmap.PixelHeight;
        var stride = w * 4;
        var pixels = new byte[stride * h];
        for (var y = 0; y < h; y++)
        {
            var vv = 1.0 - y / (double)Math.Max(1, h - 1);
            for (var x = 0; x < w; x++)
            {
                var ss = x / (double)Math.Max(1, w - 1);
                var col = HsvColorHelper.ColorFromHsv(_h, ss, vv);
                var i = y * stride + x * 4;
                pixels[i] = col.B;
                pixels[i + 1] = col.G;
                pixels[i + 2] = col.R;
                pixels[i + 3] = 255;
            }
        }

        _sbBitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
    }

    private void PushRgbFromHsv()
    {
        var c = HsvColorHelper.ColorFromHsv(_h, _s, _v);
        _suppress = true;
        HexBox.Text = ToHex(c);
        _suppress = false;
        UpdateSbRing();
    }

    private void UpdateSbRing()
    {
        var w = SbBorder.ActualWidth;
        var h = SbBorder.ActualHeight;
        if (w <= 1 || h <= 1)
        {
            w = 220;
            h = 180;
        }

        Canvas.SetLeft(SbRing, _s * w - SbRing.Width / 2);
        Canvas.SetTop(SbRing, (1 - _v) * h - SbRing.Height / 2);
    }

    private void SbBorder_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragSb = true;
        SbBorder.CaptureMouse();
        PickSb(e.GetPosition(SbBorder));
        e.Handled = true;
    }

    private void SbBorder_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragSb || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        PickSb(e.GetPosition(SbBorder));
    }

    private void SbBorder_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragSb = false;
        SbBorder.ReleaseMouseCapture();
    }

    /// <summary>Клик/перетаскивание на плоскости насыщенности и яркости.</summary>
    private void PickSb(Point p)
    {
        var w = SbBorder.ActualWidth;
        var h = SbBorder.ActualHeight;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        _s = Math.Clamp(p.X / w, 0, 1);
        _v = Math.Clamp(1 - p.Y / h, 0, 1);
        PushRgbFromHsv();
    }

    private void HexBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        ApplyHexFromText();
    }

    private void HexBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyHexFromText();
            e.Handled = true;
        }
    }

    private void ApplyHexFromText()
    {
        if (_suppress)
        {
            return;
        }

        var raw = HexBox.Text?.Trim() ?? "";
        if (!raw.StartsWith("#", StringComparison.Ordinal))
        {
            raw = "#" + raw;
        }

        if (Regex.IsMatch(raw, "^#[0-9A-Fa-f]{3}$"))
        {
            raw = $"#{raw[1]}{raw[1]}{raw[2]}{raw[2]}{raw[3]}{raw[3]}";
        }

        if (!Regex.IsMatch(raw, "^#[0-9A-Fa-f]{6}$"))
        {
            return;
        }

        try
        {
            var col = (Color)ColorConverter.ConvertFromString(raw)!;
            HsvColorHelper.RgbToHsv(col.R, col.G, col.B, out _h, out _s, out _v);
            _suppress = true;
            HueSlider.Value = _h;
            _suppress = false;
            RenderSbPlane();
            PushRgbFromHsv();
        }
        catch
        {
            // ignore
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ApplyHexFromText();
        SelectedHex = HexBox.Text?.Trim() ?? "";
        if (!SelectedHex.StartsWith('#'))
        {
            SelectedHex = "#" + SelectedHex;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void EyedropperButton_Click(object sender, RoutedEventArgs e)
    {
        var ov = new EyedropperOverlayWindow();
        if (ov.ShowDialog() == true && ov.PickedColor is { } c)
        {
            HsvColorHelper.RgbToHsv(c.R, c.G, c.B, out _h, out _s, out _v);
            _suppress = true;
            HueSlider.Value = _h;
            HexBox.Text = ToHex(c);
            _suppress = false;
            RenderSbPlane();
            PushRgbFromHsv();
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        UpdateSbRing();
    }

    /// <summary>Полноэкранный слой для выбора пикселя под курсором.</summary>
    private sealed class EyedropperOverlayWindow : Window
    {
        internal Color? PickedColor { get; private set; }

        public EyedropperOverlayWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            WindowState = WindowState.Maximized;
            Topmost = true;
            Cursor = Cursors.Cross;
            ShowInTaskbar = false;
            PreviewMouseLeftButtonDown += (_, args) =>
            {
                try
                {
                    var p = args.GetPosition(this);
                    var screen = PointToScreen(p);
                    var ix = (int)Math.Round(screen.X);
                    var iy = (int)Math.Round(screen.Y);

                    using var bmp = new System.Drawing.Bitmap(1, 1);
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(ix, iy, 0, 0, new System.Drawing.Size(1, 1));
                    }

                    var px = bmp.GetPixel(0, 0);
                    PickedColor = System.Windows.Media.Color.FromRgb(px.R, px.G, px.B);
                    DialogResult = true;
                }
                catch
                {
                    DialogResult = false;
                }

                Close();
            };

            KeyDown += (_, k) =>
            {
                if (k.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                }
            };
        }
    }
}
