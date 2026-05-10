using Newtonsoft.Json;
using System.Collections.Generic;
using System.Windows;

namespace WhiteSpace.Models;

/// <summary>Сериализуется в <see cref="BoardShape.Text"/> для типа connector.</summary>
public sealed class ConnectorAttachment
{
    [JsonProperty("startShapeId")] public int? StartShapeId { get; set; }
    [JsonProperty("startSide")] public string? StartSide { get; set; }
    [JsonProperty("endShapeId")] public int? EndShapeId { get; set; }
    [JsonProperty("endSide")] public string? EndSide { get; set; }

    public bool HasAnyAttachment =>
        StartShapeId.HasValue || EndShapeId.HasValue;
}

public static class ConnectorAttachmentHelper
{
    public static bool TryParse(string? text, out ConnectorAttachment attachment)
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
            return attachment.HasAnyAttachment;
        }
        catch
        {
            attachment = new ConnectorAttachment();
            return false;
        }
    }

    public static string SerializeForStorage(ConnectorAttachment attachment) =>
        JsonConvert.SerializeObject(attachment);

    /// <summary>Мировые координаты точки порта на стороне прямоугольника фигуры (центр — X,Y).</summary>
    public static Point GetAnchorWorldPoint(BoardShape shape, string? side)
    {
        var left = shape.X - shape.Width / 2;
        var top = shape.Y - shape.Height / 2;
        var w = shape.Width > 0 ? shape.Width : 1;
        var h = shape.Height > 0 ? shape.Height : 1;

        return (side ?? "n").ToLowerInvariant() switch
        {
            "e" => new Point(left + w, top + h / 2),
            "s" => new Point(left + w / 2, top + h),
            "w" => new Point(left, top + h / 2),
            _ => new Point(left + w / 2, top),
        };
    }

    public static List<Point> ResolveConnectorPoints(BoardShape connector, IReadOnlyList<BoardShape> allShapes)
    {
        if (!TryParse(connector.Text, out var att) || !att.HasAnyAttachment)
        {
            return DeserializePointsFallback(connector.Points);
        }

        var pts = new List<Point>();

        if (att.StartShapeId.HasValue
            && TryFindShape(allShapes, att.StartShapeId.Value, out var startShape))
        {
            pts.Add(GetAnchorWorldPoint(startShape, att.StartSide));
        }
        else
        {
            var fb = DeserializePointsFallback(connector.Points);
            pts.Add(fb.Count > 0 ? fb[0] : new Point(connector.X, connector.Y));
        }

        if (att.EndShapeId.HasValue
            && TryFindShape(allShapes, att.EndShapeId.Value, out var endShape))
        {
            pts.Add(GetAnchorWorldPoint(endShape, att.EndSide));
        }
        else
        {
            var fb = DeserializePointsFallback(connector.Points);
            pts.Add(fb.Count >= 2 ? fb[1] : pts.Count > 0 ? pts[0] : new Point(connector.X, connector.Y));
        }

        return pts;
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
