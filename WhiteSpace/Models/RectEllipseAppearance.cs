using Newtonsoft.Json;

namespace WhiteSpace.Models;

/// <summary>Сохраняется в поле Text модели фигуры для типов rectangle и ellipse (JSON).</summary>
public sealed class RectEllipseAppearance
{
    /// <summary>solid — сплошная заливка; tint — полупрозрачная заливка; stroke — только контур.</summary>
    [JsonProperty("m")]
    public string Mode { get; set; } = "stroke";

    /// <summary>Цвет заливки (HEX). Контур хранится в поле Color модели фигуры.</summary>
    [JsonProperty("fill")]
    public string? FillHex { get; set; }

    /// <summary>Стиль линии контура: solid, dash, dot, dashdot.</summary>
    [JsonProperty("dash")]
    public string? StrokeDash { get; set; }

    /// <summary>Вариант контура для type=rectangle (ромб, стрелка и т.д.). Для ellipse не используется.</summary>
    [JsonProperty("sk")]
    public string? ShapeKind { get; set; }

    /// <summary>Текст внутри фигуры (подпись).</summary>
    [JsonProperty("lbl")]
    public string? Label { get; set; }

    public static RectEllipseAppearance StrokeOnly() => new() { Mode = "stroke" };

    public static RectEllipseAppearance Parse(BoardShape shape)
    {
        if (shape.Type is not ("rectangle" or "ellipse"))
        {
            return StrokeOnly();
        }

        var t = shape.Text?.Trim();
        if (string.IsNullOrEmpty(t) || t[0] != '{')
        {
            return StrokeOnly();
        }

        try
        {
            return JsonConvert.DeserializeObject<RectEllipseAppearance>(t) ?? StrokeOnly();
        }
        catch
        {
            return StrokeOnly();
        }
    }

    public void SaveTo(BoardShape shape)
    {
        if (shape.Type is not ("rectangle" or "ellipse"))
        {
            return;
        }

        shape.Text = JsonConvert.SerializeObject(this);
    }
}
