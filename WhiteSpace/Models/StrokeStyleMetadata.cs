using Newtonsoft.Json;

namespace WhiteSpace.Models;

/// <summary>Доп. стиль для line/marker в <see cref="BoardShape.Text"/>.</summary>
public sealed class StrokeStyleMetadata
{
    [JsonProperty("thickness")] public double Thickness { get; set; } = 2;
    [JsonProperty("opacity")] public double Opacity { get; set; } = 1;
    [JsonProperty("isMarker")] public bool IsMarker { get; set; }
}

public static class StrokeStyleMetadataHelper
{
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

    public static string Serialize(StrokeStyleMetadata style) =>
        JsonConvert.SerializeObject(style);

    public static StrokeStyleMetadata ForMarker() =>
        new() { Thickness = 14, Opacity = 0.42, IsMarker = true };

    public static StrokeStyleMetadata ForPen() =>
        new() { Thickness = 2, Opacity = 1, IsMarker = false };
}
