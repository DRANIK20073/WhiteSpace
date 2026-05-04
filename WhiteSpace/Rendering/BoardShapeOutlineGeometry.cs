using System.Windows;
using System.Windows.Media;

namespace WhiteSpace.Rendering;

/// <summary>Нормализованный контур 100×100 для <see cref="System.Windows.Shapes.Path"/> со Stretch=Fill.</summary>
public static class BoardShapeOutlineGeometry
{
    private const double U = 100;

    public static Geometry Get(string kind)
    {
        return kind switch
        {
            "rect" => Rect(),
            "diamond" => Diamond(),
            "triangle" => TriangleUp(),
            "triangleInv" => TriangleDown(),
            "pentagon" => PentagonUp(),
            "octagon" => Octagon(),
            "plus" => Plus(),
            "arrowLeft" => ArrowLeft(),
            "arrowRight" => ArrowRight(),
            "process" => ProcessBar(),
            "star" => Star5(),
            "callout" => Callout(),
            "parallelogram" => Parallelogram(12),
            "parallelogramAlt" => Parallelogram(22),
            "cylinderV" => CylinderVertical(),
            "cylinderH" => CylinderHorizontal(),
            "document" => Document(),
            "folder" => Folder(),
            "storedData" => StoredData(),
            "multiDoc" => MultiDoc(),
            "internalStorage" => InternalStorage(),
            "flowPentagon" => PentagonDown(),
            "trapezoid" => Trapezoid(),
            "manualInput" => ManualInput(),
            "prepHex" => Hexagon(),
            "card" => Card(),
            "magDisk" => MagDisk(),
            "orGate" => OrGate(),
            _ => Rect()
        };
    }

    private static Geometry Rect() => new RectangleGeometry(new Rect(5, 5, 90, 90));

    private static Geometry Diamond() => Poly(new[]
    {
        new Point(50, 4), new Point(92, 50), new Point(50, 96), new Point(8, 50)
    });

    private static Geometry TriangleUp() => Poly(new[]
    {
        new Point(50, 6), new Point(92, 90), new Point(8, 90)
    });

    private static Geometry TriangleDown() => Poly(new[]
    {
        new Point(8, 10), new Point(92, 10), new Point(50, 90)
    });

    private static Geometry PentagonUp()
    {
        return RegularPolygon(5, 50, 50, 44, -90);
    }

    private static Geometry Octagon() => RegularPolygon(8, 50, 50, 40, 22.5);

    private static Geometry PentagonDown()
    {
        return RegularPolygon(5, 50, 50, 42, 90);
    }

    private static Geometry Hexagon() => RegularPolygon(6, 50, 50, 42, 0);

    private static Geometry RegularPolygon(int n, double cx, double cy, double r, double startDeg)
    {
        var pts = new Point[n];
        double rad0 = startDeg * Math.PI / 180;
        for (int i = 0; i < n; i++)
        {
            double a = rad0 + i * (2 * Math.PI / n);
            pts[i] = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        }

        return Poly(pts);
    }

    private static Geometry Poly(Point[] pts)
    {
        var sg = new StreamGeometry();
        using (var ctx = sg.Open())
        {
            ctx.BeginFigure(pts[0], true, true);
            for (var i = 1; i < pts.Length; i++)
            {
                ctx.LineTo(pts[i], true, false);
            }
        }

        sg.Freeze();
        return sg;
    }

    private static Geometry Plus()
    {
        var a = new RectangleGeometry(new Rect(36, 14, 28, 72));
        var b = new RectangleGeometry(new Rect(14, 36, 72, 28));
        return Geometry.Combine(a, b, GeometryCombineMode.Union, null);
    }

    private static Geometry ArrowRight()
    {
        return Poly(new[]
        {
            new Point(14, 28),
            new Point(58, 28),
            new Point(58, 14),
            new Point(86, 50),
            new Point(58, 86),
            new Point(58, 72),
            new Point(14, 72),
        });
    }

    private static Geometry ArrowLeft()
    {
        return Poly(new[]
        {
            new Point(86, 28),
            new Point(42, 28),
            new Point(42, 14),
            new Point(14, 50),
            new Point(42, 86),
            new Point(42, 72),
            new Point(86, 72),
        });
    }

    private static Geometry ProcessBar()
    {
        return Poly(new[]
        {
            new Point(22, 22),
            new Point(78, 22),
            new Point(84, 50),
            new Point(78, 78),
            new Point(22, 78),
            new Point(16, 50),
        });
    }

    private static Geometry Star5()
    {
        var pts = new Point[10];
        double outer = 46;
        double inner = 20;
        for (int i = 0; i < 10; i++)
        {
            double ang = -Math.PI / 2 + i * Math.PI / 5;
            double rad = (i % 2 == 0) ? outer : inner;
            pts[i] = new Point(50 + rad * Math.Cos(ang), 50 + rad * Math.Sin(ang));
        }

        return Poly(pts);
    }

    private static Geometry Callout()
    {
        var body = new RectangleGeometry(new Rect(10, 12, 72, 56), 8, 8);
        var tail = Poly(new[]
        {
            new Point(28, 68),
            new Point(40, 68),
            new Point(22, 94),
        });
        return Geometry.Combine(body, tail, GeometryCombineMode.Union, null);
    }

    private static Geometry Parallelogram(double skew)
    {
        return Poly(new[]
        {
            new Point(12 + skew, 18),
            new Point(88, 18),
            new Point(88 - skew, 82),
            new Point(12, 82),
        });
    }

    private static Geometry CylinderVertical()
    {
        var pg = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(18, 26), IsClosed = true };
        fig.Segments.Add(new ArcSegment(new Point(82, 26), new Size(32, 12), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new Point(82, 74), true));
        fig.Segments.Add(new ArcSegment(new Point(18, 74), new Size(32, 12), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new Point(18, 26), true));
        pg.Figures.Add(fig);
        pg.Freeze();
        return pg;
    }

    private static Geometry CylinderHorizontal()
    {
        var pg = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(26, 18), IsClosed = true };
        fig.Segments.Add(new ArcSegment(new Point(26, 82), new Size(12, 32), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new Point(74, 82), true));
        fig.Segments.Add(new ArcSegment(new Point(74, 18), new Size(12, 32), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new Point(26, 18), true));
        pg.Figures.Add(fig);
        pg.Freeze();
        return pg;
    }

    private static Geometry Document()
    {
        return Poly(new[]
        {
            new Point(18, 14),
            new Point(66, 14),
            new Point(82, 30),
            new Point(82, 86),
            new Point(18, 86),
        });
    }

    private static Geometry Folder()
    {
        return Poly(new[]
        {
            new Point(14, 34),
            new Point(14, 26),
            new Point(28, 14),
            new Point(58, 14),
            new Point(68, 22),
            new Point(86, 22),
            new Point(86, 84),
            new Point(14, 84),
        });
    }

    private static Geometry StoredData()
    {
        var pg = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(14, 22), IsClosed = true };
        fig.Segments.Add(new LineSegment(new Point(86, 22), true));
        fig.Segments.Add(new LineSegment(new Point(86, 68), true));
        fig.Segments.Add(new BezierSegment(new Point(74, 82), new Point(50, 54), new Point(26, 82), true));
        fig.Segments.Add(new LineSegment(new Point(14, 68), true));
        fig.Segments.Add(new LineSegment(new Point(14, 22), true));
        pg.Figures.Add(fig);
        pg.Freeze();
        return pg;
    }

    private static Geometry MultiDoc()
    {
        var a = new RectangleGeometry(new Rect(24, 18, 58, 62));
        var b = new RectangleGeometry(new Rect(16, 26, 58, 62));
        var c = new RectangleGeometry(new Rect(8, 34, 58, 54));
        var u = Geometry.Combine(c, b, GeometryCombineMode.Union, null);
        return Geometry.Combine(u, a, GeometryCombineMode.Union, null);
    }

    private static Geometry InternalStorage()
    {
        var outer = new RectangleGeometry(new Rect(16, 18, 68, 66));
        var v1 = new RectangleGeometry(new Rect(34, 22, 4, 58));
        var v2 = new RectangleGeometry(new Rect(62, 22, 4, 58));
        var u = Geometry.Combine(outer, v1, GeometryCombineMode.Union, null);
        return Geometry.Combine(u, v2, GeometryCombineMode.Union, null);
    }

    private static Geometry Trapezoid()
    {
        return Poly(new[]
        {
            new Point(24, 22),
            new Point(76, 22),
            new Point(88, 78),
            new Point(12, 78),
        });
    }

    private static Geometry ManualInput()
    {
        return Poly(new[]
        {
            new Point(28, 18),
            new Point(88, 34),
            new Point(72, 82),
            new Point(12, 66),
        });
    }

    private static Geometry Card()
    {
        var body = new RectangleGeometry(new Rect(22, 18, 58, 66));
        var line = new RectangleGeometry(new Rect(22, 18, 16, 66));
        return Geometry.Combine(body, line, GeometryCombineMode.Union, null);
    }

    private static Geometry MagDisk()
    {
        var circle = new EllipseGeometry(new Point(50, 50), 38, 38);
        var h = new LineGeometry(new Point(22, 50), new Point(78, 50));
        var v = new LineGeometry(new Point(50, 22), new Point(50, 78));
        var u = Geometry.Combine(circle, h, GeometryCombineMode.Union, null);
        return Geometry.Combine(u, v, GeometryCombineMode.Union, null);
    }

    private static Geometry OrGate()
    {
        var c = new EllipseGeometry(new Point(50, 50), 38, 38);
        var x1 = new LineGeometry(new Point(30, 30), new Point(70, 70));
        var x2 = new LineGeometry(new Point(70, 30), new Point(30, 70));
        var u = Geometry.Combine(c, x1, GeometryCombineMode.Union, null);
        return Geometry.Combine(u, x2, GeometryCombineMode.Union, null);
    }
}
