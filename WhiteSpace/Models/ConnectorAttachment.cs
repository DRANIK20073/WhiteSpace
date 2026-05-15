using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using WhiteSpace.Rendering;

namespace WhiteSpace.Models;

/// <summary>Сериализуется в <see cref="BoardShape.Text"/> для типа connector.</summary>
public sealed class ConnectorAttachment
{
    [JsonProperty("startShapeId")] public int? StartShapeId { get; set; }
    [JsonProperty("startSide")] public string? StartSide { get; set; }
    [JsonProperty("endShapeId")] public int? EndShapeId { get; set; }
    [JsonProperty("endSide")] public string? EndSide { get; set; }

    /// <summary>solid, dash, dashdot</summary>
    [JsonProperty("strokeDash")] public string? StrokeDash { get; set; }

    /// <summary>none, block, open, diamond, oval</summary>
    [JsonProperty("arrowStart")] public string? ArrowStart { get; set; }

    /// <summary>none, block, open, diamond, oval</summary>
    [JsonProperty("arrowEnd")] public string? ArrowEnd { get; set; }

    public bool HasAnyAttachment =>
        StartShapeId.HasValue || EndShapeId.HasValue;

    public string EffectiveArrowStart() =>
        string.IsNullOrWhiteSpace(ArrowStart) ? "none" : ArrowStart.Trim().ToLowerInvariant();

    public string EffectiveArrowEnd() =>
        string.IsNullOrWhiteSpace(ArrowEnd) ? "block" : ArrowEnd.Trim().ToLowerInvariant();
}

public static class ConnectorAttachmentHelper
{
    private const double RouteStubLength = 28;
    private const double SnapPortRadius = 22;

    public static bool TryDeserialize(string? text, out ConnectorAttachment attachment)
    {
        attachment = new ConnectorAttachment();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith('{'))
        {
            return false;
        }

        try
        {
            attachment = JsonConvert.DeserializeObject<ConnectorAttachment>(text)
                         ?? new ConnectorAttachment();
            return true;
        }
        catch
        {
            attachment = new ConnectorAttachment();
            return false;
        }
    }

    public static bool TryParse(string? text, out ConnectorAttachment attachment)
    {
        if (!TryDeserialize(text, out attachment))
        {
            return false;
        }

        return attachment.HasAnyAttachment;
    }

    public static string SerializeForStorage(ConnectorAttachment attachment) =>
        JsonConvert.SerializeObject(attachment);

    private const double AnchorOutwardOffset = 12;

    /// <summary>Мировые координаты точки порта снаружи границы фигуры (центр — X,Y).</summary>
    public static Point GetAnchorWorldPoint(BoardShape shape, string? side)
    {
        var left = shape.X - shape.Width / 2;
        var top = shape.Y - shape.Height / 2;
        var w = shape.Width > 0 ? shape.Width : 1;
        var h = shape.Height > 0 ? shape.Height : 1;

        Point norm;
        if (shape.Type is "rectangle" or "ellipse")
        {
            var kind = shape.Type == "ellipse"
                ? "ellipse"
                : (RectEllipseAppearance.Parse(shape).ShapeKind ?? "rect");
            norm = kind is "rect" or "roundRect" or "ellipse"
                ? DefaultBBoxPortNormalized(side)
                : BoardShapeOutlineGeometry.GetConnectionPortNormalized(kind, side);
        }
        else
        {
            norm = DefaultBBoxPortNormalized(side);
        }

        var edge = new Point(left + norm.X / 100.0 * w, top + norm.Y / 100.0 * h);
        return OffsetOutward(edge, side, AnchorOutwardOffset);
    }

    private static Point DefaultBBoxPortNormalized(string? side) =>
        (side ?? "n").ToLowerInvariant() switch
        {
            "e" => new Point(92, 50),
            "s" => new Point(50, 92),
            "w" => new Point(8, 50),
            _ => new Point(50, 8),
        };

    private static Point OffsetOutward(Point edge, string? side, double offset) =>
        (side ?? "n").ToLowerInvariant() switch
        {
            "e" => new Point(edge.X + offset, edge.Y),
            "s" => new Point(edge.X, edge.Y + offset),
            "w" => new Point(edge.X - offset, edge.Y),
            _ => new Point(edge.X, edge.Y - offset),
        };

    public static List<Point> ResolveConnectorPoints(BoardShape connector, IReadOnlyList<BoardShape> allShapes)
    {
        if (!TryParse(connector.Text, out var att) || !att.HasAnyAttachment)
        {
            return DeserializePointsFallback(connector.Points);
        }

        Point start;
        Point end;
        string? startSide = att.StartSide;
        string? endSide = att.EndSide;

        if (att.StartShapeId.HasValue
            && TryFindShape(allShapes, att.StartShapeId.Value, out var startShape))
        {
            start = GetAnchorWorldPoint(startShape, att.StartSide);
        }
        else
        {
            var fb = DeserializePointsFallback(connector.Points);
            start = fb.Count > 0 ? fb[0] : new Point(connector.X, connector.Y);
            startSide = null;
        }

        if (att.EndShapeId.HasValue
            && TryFindShape(allShapes, att.EndShapeId.Value, out var endShape))
        {
            end = GetAnchorWorldPoint(endShape, att.EndSide);
        }
        else
        {
            var fb = DeserializePointsFallback(connector.Points);
            end = fb.Count >= 2 ? fb[^1] : ptsFallbackEnd(fb, start);
        }

        if (att.StartShapeId.HasValue && att.EndShapeId.HasValue)
        {
            return ComputeOrthogonalRoute(start, end, startSide, endSide);
        }

        if (att.StartShapeId.HasValue || att.EndShapeId.HasValue)
        {
            var attachedSide = att.StartShapeId.HasValue ? startSide : endSide;
            var attachedPoint = att.StartShapeId.HasValue ? start : end;
            var freePoint = att.StartShapeId.HasValue ? end : start;
            return ComputeOrthogonalRouteToFreePoint(attachedPoint, freePoint, attachedSide);
        }

        return new List<Point> { start, end };
    }

    private static Point ptsFallbackEnd(List<Point> fb, Point start) =>
        fb.Count > 0 ? fb[0] : start;

    /// <summary>Ортогональный маршрут между двумя привязанными фигурами.</summary>
    public static List<Point> ComputeOrthogonalRoute(Point start, Point end, string? startSide, string? endSide)
    {
        var route = new List<Point> { start };

        var exit = OffsetAlongSide(start, startSide, RouteStubLength);
        var approach = OffsetAlongSide(end, endSide, RouteStubLength, inward: false);

        route.Add(exit);

        foreach (var mid in BuildOrthogonalMidPoints(exit, approach, startSide, endSide))
        {
            if (route.Count == 0 || Distance(route[^1], mid) > 0.5)
            {
                route.Add(mid);
            }
        }

        if (Distance(route[^1], approach) > 0.5)
        {
            route.Add(approach);
        }

        if (Distance(route[^1], end) > 0.5)
        {
            route.Add(end);
        }

        return SimplifyCollinear(route);
    }

    public static List<Point> ComputeOrthogonalRouteToFreePoint(Point attached, Point free, string? side)
    {
        var exit = OffsetAlongSide(attached, side, RouteStubLength);
        var route = new List<Point> { attached, exit };

        if (IsHorizontalSide(side))
        {
            route.Add(new Point(free.X, exit.Y));
        }
        else
        {
            route.Add(new Point(exit.X, free.Y));
        }

        route.Add(free);
        return SimplifyCollinear(route);
    }

    private static IEnumerable<Point> BuildOrthogonalMidPoints(
        Point exit,
        Point approach,
        string? startSide,
        string? endSide)
    {
        if (Math.Abs(exit.X - approach.X) < 1 && Math.Abs(exit.Y - approach.Y) < 1)
        {
            yield break;
        }

        if (IsHorizontalSide(startSide) && IsHorizontalSide(endSide))
        {
            var midY = (exit.Y + approach.Y) / 2;
            yield return new Point(exit.X, midY);
            yield return new Point(approach.X, midY);
            yield break;
        }

        if (IsVerticalSide(startSide) && IsVerticalSide(endSide))
        {
            var midX = (exit.X + approach.X) / 2;
            yield return new Point(midX, exit.Y);
            yield return new Point(midX, approach.Y);
            yield break;
        }

        if (IsHorizontalSide(startSide))
        {
            yield return new Point(approach.X, exit.Y);
        }
        else
        {
            yield return new Point(exit.X, approach.Y);
        }
    }

    private static Point OffsetAlongSide(Point anchor, string? side, double distance, bool inward = false)
    {
        var sign = inward ? -1 : 1;
        return (side ?? "n").ToLowerInvariant() switch
        {
            "e" => new Point(anchor.X + distance * sign, anchor.Y),
            "s" => new Point(anchor.X, anchor.Y + distance * sign),
            "w" => new Point(anchor.X - distance * sign, anchor.Y),
            _ => new Point(anchor.X, anchor.Y - distance * sign),
        };
    }

    private static bool IsHorizontalSide(string? side) =>
        side is "e" or "w";

    private static bool IsVerticalSide(string? side) =>
        side is "n" or "s" || string.IsNullOrEmpty(side);

    private static List<Point> SimplifyCollinear(List<Point> points)
    {
        if (points.Count <= 2)
        {
            return points;
        }

        var result = new List<Point> { points[0] };
        for (var i = 1; i < points.Count - 1; i++)
        {
            var prev = result[^1];
            var cur = points[i];
            var next = points[i + 1];
            var collinearH = Math.Abs(prev.Y - cur.Y) < 0.5 && Math.Abs(cur.Y - next.Y) < 0.5;
            var collinearV = Math.Abs(prev.X - cur.X) < 0.5 && Math.Abs(cur.X - next.X) < 0.5;
            if (!collinearH && !collinearV)
            {
                result.Add(cur);
            }
        }

        result.Add(points[^1]);
        return result;
    }

    private static double Distance(Point a, Point b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Ближайший порт фигуры к точке в мировых координатах.</summary>
    public static bool TryFindNearestPort(
        Point world,
        IReadOnlyList<BoardShape> shapes,
        int? excludeShapeId,
        out int shapeId,
        out string side)
    {
        shapeId = 0;
        side = "n";
        var bestDist = SnapPortRadius;
        var found = false;

        foreach (var shape in shapes)
        {
            if (shape.Id == excludeShapeId)
            {
                continue;
            }

            if (shape.Type is not ("rectangle" or "ellipse" or "stickyNote"))
            {
                continue;
            }

            foreach (var s in new[] { "n", "e", "s", "w" })
            {
                var port = GetAnchorWorldPoint(shape, s);
                var d = Distance(world, port);
                if (d < bestDist)
                {
                    bestDist = d;
                    shapeId = shape.Id;
                    side = s;
                    found = true;
                }
            }
        }

        return found;
    }

    private static bool TryFindShape(IReadOnlyList<BoardShape> allShapes, int id, out BoardShape shape)
    {
        foreach (var s in allShapes)
        {
            if (s.Id == id)
            {
                shape = s;
                return true;
            }
        }

        shape = null!;
        return false;
    }

    private static List<Point> DeserializePointsFallback(string? pointsJson)
    {
        if (string.IsNullOrWhiteSpace(pointsJson))
        {
            return new List<Point>();
        }

        try
        {
            return JsonConvert.DeserializeObject<List<Point>>(pointsJson) ?? new List<Point>();
        }
        catch
        {
            return new List<Point>();
        }
    }

    public static bool ReferencesShape(BoardShape connector, int shapeId)
    {
        if (!TryParse(connector.Text, out var att))
        {
            return false;
        }

        return att.StartShapeId == shapeId || att.EndShapeId == shapeId;
    }
}
