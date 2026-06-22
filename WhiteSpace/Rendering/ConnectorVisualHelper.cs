using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WhiteSpace.Models;

namespace WhiteSpace.Rendering;

// Визуал коннектора: линия, штрихи и стрелки на концах.
public static class ConnectorVisualHelper
{
    public const string ArrowHeadTag = "connectorArrowHead";
    public const string LineTag = "connectorLine";

    // Собираем Canvas с Polyline и стрелками по точкам и метаданным в Text.
    public static Canvas Build(BoardShape shape, IReadOnlyList<Point> points, Brush stroke, double thickness)
    {
        ConnectorAttachmentHelper.TryDeserialize(shape.Text, out var att);

        var canvas = new Canvas { Uid = shape.Id.ToString() };
        var line = CreateLine(stroke, thickness);
        line.Tag = LineTag;

        foreach (var p in points)
        {
            line.Points.Add(p);
        }

        ApplyLineStyle(line, att);
        canvas.Children.Add(line);
        RefreshArrowheads(canvas, line, att, stroke, thickness);
        return canvas;
    }

    public static Polyline? GetLine(UIElement? element)
    {
        if (element is Polyline pl)
        {
            return pl;
        }

        if (element is Canvas canvas)
        {
            return canvas.Children.OfType<Polyline>().FirstOrDefault(c => LineTag.Equals(c.Tag));
        }

        return null;
    }

    // Обновляем точки линии и пересчитываем стрелки.
    public static void UpdatePoints(UIElement element, BoardShape shape, IReadOnlyList<Point> points, Brush stroke, double thickness)
    {
        if (GetLine(element) is not { } line)
        {
            return;
        }

        ConnectorAttachmentHelper.TryDeserialize(shape.Text, out var att);
        line.Points.Clear();
        foreach (var p in points)
        {
            line.Points.Add(p);
        }

        ApplyLineStyle(line, att);
        line.Stroke = stroke;
        line.StrokeThickness = thickness;

        if (element is Canvas canvas)
        {
            RefreshArrowheads(canvas, line, att, stroke, thickness);
        }
    }

    public static void ApplyStyle(UIElement element, BoardShape shape, Brush stroke, double thickness)
    {
        if (GetLine(element) is not { } line)
        {
            return;
        }

        ConnectorAttachmentHelper.TryDeserialize(shape.Text, out var att);
        line.Stroke = stroke;
        line.StrokeThickness = thickness;
        ApplyLineStyle(line, att);

        if (element is Canvas canvas)
        {
            RefreshArrowheads(canvas, line, att, stroke, thickness);
        }
    }

    private static Polyline CreateLine(Brush stroke, double thickness) =>
        new()
        {
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Flat,
            StrokeEndLineCap = PenLineCap.Flat,
            StrokeMiterLimit = 1,
            Fill = Brushes.Transparent,
            SnapsToDevicePixels = true
        };

    private static void ApplyLineStyle(Polyline line, ConnectorAttachment att)
    {
        line.StrokeDashArray = null;
        line.StrokeDashCap = PenLineCap.Flat;
        switch (att.StrokeDash?.ToLowerInvariant())
        {
            case "dash":
                line.StrokeDashArray = new DoubleCollection(new[] { 8.0, 5.0 });
                break;
            case "dashdot":
                line.StrokeDashArray = new DoubleCollection(new[] { 10.0, 5.0, 2.0, 5.0 });
                break;
        }
    }

    private static void RefreshArrowheads(
        Canvas canvas,
        Polyline line,
        ConnectorAttachment att,
        Brush stroke,
        double thickness)
    {
        foreach (var child in canvas.Children.OfType<FrameworkElement>().Where(c => ArrowHeadTag.Equals(c.Tag)).ToList())
        {
            canvas.Children.Remove(child);
        }

        if (line.Points.Count < 2)
        {
            return;
        }

        var pts = line.Points;
        AddHead(canvas, att.EffectiveArrowStart(), pts[1], pts[0], stroke, thickness, atStart: true);
        AddHead(canvas, att.EffectiveArrowEnd(), pts[pts.Count - 2], pts[pts.Count - 1], stroke, thickness, atStart: false);
    }

    private static void AddHead(
        Canvas canvas,
        string kind,
        Point from,
        Point tip,
        Brush stroke,
        double thickness,
        bool atStart)
    {
        if (kind is "none" or "")
        {
            return;
        }

        var angle = Math.Atan2(tip.Y - from.Y, tip.X - from.X);
        var size = Math.Max(thickness * 4, 12);

        UIElement? head = kind switch
        {
            "open" => CreateOpenArrow(tip, angle, stroke, thickness, size),
            "diamond" => CreateDiamondHead(tip, angle, stroke, thickness, size),
            "oval" => CreateOvalHead(tip, angle, stroke, size),
            _ => CreateBlockArrow(tip, angle, stroke, size)
        };

        if (head == null)
        {
            return;
        }

        if (head is FrameworkElement fe)
        {
            fe.Tag = ArrowHeadTag;
            fe.IsHitTestVisible = false;
        }

        canvas.Children.Add(head);
    }

    private static Polygon CreateBlockArrow(Point tip, double angle, Brush fill, double size)
    {
        var back = tip - new Vector(Math.Cos(angle), Math.Sin(angle)) * size;
        var left = back + new Vector(Math.Cos(angle + Math.PI / 2), Math.Sin(angle + Math.PI / 2)) * (size * 0.42);
        var right = back + new Vector(Math.Cos(angle - Math.PI / 2), Math.Sin(angle - Math.PI / 2)) * (size * 0.42);
        return new Polygon
        {
            Points = new PointCollection { tip, left, right },
            Fill = fill,
            Stroke = fill,
            StrokeThickness = 1
        };
    }

    private static Polyline CreateOpenArrow(Point tip, double angle, Brush stroke, double thickness, double size)
    {
        var back = tip - new Vector(Math.Cos(angle), Math.Sin(angle)) * size;
        var left = back + new Vector(Math.Cos(angle + Math.PI / 2), Math.Sin(angle + Math.PI / 2)) * (size * 0.45);
        var right = back + new Vector(Math.Cos(angle - Math.PI / 2), Math.Sin(angle - Math.PI / 2)) * (size * 0.45);
        return new Polyline
        {
            Points = new PointCollection { left, tip, right },
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        };
    }

    private static Polygon CreateDiamondHead(Point tip, double angle, Brush stroke, double thickness, double size)
    {
        var center = tip - new Vector(Math.Cos(angle), Math.Sin(angle)) * (size * 0.5);
        var p1 = center + new Vector(Math.Cos(angle), Math.Sin(angle)) * (size * 0.5);
        var p2 = center + new Vector(Math.Cos(angle + Math.PI / 2), Math.Sin(angle + Math.PI / 2)) * (size * 0.35);
        var p3 = center - new Vector(Math.Cos(angle), Math.Sin(angle)) * (size * 0.5);
        var p4 = center + new Vector(Math.Cos(angle - Math.PI / 2), Math.Sin(angle - Math.PI / 2)) * (size * 0.35);
        return new Polygon
        {
            Points = new PointCollection { p1, p2, p3, p4 },
            Fill = stroke,
            Stroke = stroke,
            StrokeThickness = thickness
        };
    }

    private static Ellipse CreateOvalHead(Point tip, double angle, Brush stroke, double size)
    {
        var center = tip - new Vector(Math.Cos(angle), Math.Sin(angle)) * (size * 0.35);
        var el = new Ellipse
        {
            Width = size * 0.55,
            Height = size * 0.7,
            Fill = Brushes.White,
            Stroke = stroke,
            StrokeThickness = 2,
            RenderTransform = new RotateTransform(angle * 180 / Math.PI, size * 0.275, size * 0.35)
        };
        Canvas.SetLeft(el, center.X - size * 0.275);
        Canvas.SetTop(el, center.Y - size * 0.35);
        return el;
    }
}
