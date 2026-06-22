using Newtonsoft.Json;

namespace WhiteSpace.Models;

/// <summary>Доп. стиль для line/marker в <see cref="BoardShape.Text"/>.</summary>
public sealed class StrokeStyleMetadata
{
    [JsonProperty("thickness")] public double Thickness { get; set; } = 2;
    [JsonProperty("opacity")] public double Opacity { get; set; } = 1;
    [JsonProperty("isMarker")] public bool IsMarker { get; set; }
}

/// <summary>Разбор и сборка JSON стиля линии/маркера в поле Text.</summary>
public static class StrokeStyleMetadataHelper
{
    /// <summary>Пытается прочитать JSON стиля из Text фигуры.</summary>
    public static bool TryParse(string? text, out StrokeStyleMetadata style)
    {
        style = new StrokeStyleMetadata();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!text.TrimStart().StartsWith('{'))
        {
            return false;
        }

        try
        {
            style = JsonConvert.DeserializeObject<StrokeStyleMetadata>(text) ?? new StrokeStyleMetadata();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Сериализует стиль для сохранения в Text.</summary>
    public static string Serialize(StrokeStyleMetadata style) =>
        JsonConvert.SerializeObject(style);

    /// <summary>Пресет для инструмента «маркер».</summary>
    public static StrokeStyleMetadata ForMarker() =>
        new() { Thickness = 14, Opacity = 0.42, IsMarker = true };

    /// <summary>Пресет для обычной ручки.</summary>
    public static StrokeStyleMetadata ForPen() =>
        new() { Thickness = 2, Opacity = 1, IsMarker = false };
}
