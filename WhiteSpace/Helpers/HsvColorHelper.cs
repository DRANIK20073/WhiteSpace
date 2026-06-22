using System.Windows.Media;

namespace WhiteSpace.Helpers;

/// <summary>Конвертация RGB ↔ HSV для цветового пикера.</summary>
internal static class HsvColorHelper
{
    /// <summary>RGB 0–255 → H (0–360), S и V (0–1).</summary>
    public static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        var rf = r / 255.0;
        var gf = g / 255.0;
        var bf = b / 255.0;

        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var d = max - min;

        v = max;
        s = max < 1e-8 ? 0 : d / max;

        if (d < 1e-8)
        {
            h = 0;
            return;
        }

        double hh;
        if (Math.Abs(max - rf) < 1e-8)
        {
            hh = ((gf - bf) / d) % 6;
        }
        else if (Math.Abs(max - gf) < 1e-8)
        {
            hh = (bf - rf) / d + 2;
        }
        else
        {
            hh = (rf - gf) / d + 4;
        }

        h = hh * 60;
        if (h < 0)
        {
            h += 360;
        }
    }

    /// <summary>HSV → Color; hue нормализуем на круг 0–360.</summary>
    public static Color ColorFromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);

        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        var m = v - c;

        double r1 = 0, g1 = 0, b1 = 0;
        if (h < 60)
        {
            r1 = c;
            g1 = x;
        }
        else if (h < 120)
        {
            r1 = x;
            g1 = c;
        }
        else if (h < 180)
        {
            g1 = c;
            b1 = x;
        }
        else if (h < 240)
        {
            g1 = x;
            b1 = c;
        }
        else if (h < 300)
        {
            r1 = x;
            b1 = c;
        }
        else
        {
            r1 = c;
            b1 = x;
        }

        return Color.FromRgb(
            (byte)Math.Round((r1 + m) * 255),
            (byte)Math.Round((g1 + m) * 255),
            (byte)Math.Round((b1 + m) * 255));
    }

    public static Color PureHueColor(double h)
    {
        return ColorFromHsv(h, 1, 1);
    }
}
