using System.Windows.Shapes;
using WhiteSpace.Models;

namespace WhiteSpace.Rendering;

public static class BoardShapeVisualFactory
{
    public static Shape Create(BoardShape shape)
    {
        // Размер задаёт контейнер Grid на доске; фиксированные W/H ломали масштаб при ресайзе (фигура «уезжала» под рамку).
        if (shape.Type == "ellipse")
        {
            return new Ellipse();
        }

        if (shape.Type != "rectangle")
        {
            return new Rectangle();
        }

        var app = RectEllipseAppearance.Parse(shape);
        var sk = app.ShapeKind;

        if (string.IsNullOrEmpty(sk) || sk == "rect")
        {
            return new Rectangle();
        }

        if (sk == "roundRect")
        {
            var w0 = shape.Width > 0 ? shape.Width : 1;
            var h0 = shape.Height > 0 ? shape.Height : 1;
            var rr = Math.Min(w0, h0) * 0.15;
            return new Rectangle
            {
                RadiusX = rr,
                RadiusY = rr,
            };
        }

        return new Path
        {
            Stretch = System.Windows.Media.Stretch.Fill,
            Data = BoardShapeOutlineGeometry.Get(sk),
        };
    }
}
