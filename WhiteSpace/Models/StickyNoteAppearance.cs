using Newtonsoft.Json;

namespace WhiteSpace.Models;

/// <summary>Метаданные стикера в поле <see cref="BoardShape.Points"/> (JSON, не координаты).</summary>
public sealed class StickyNoteAppearance
{
    private const string DefaultPaper = "#DCE8F2";

    [JsonProperty("author")]
    public string? Author { get; set; }

    /// <summary>Цвет «бумаги» стикера (#RRGGBB).</summary>
    [JsonProperty("paper")]
    public string? PaperHex { get; set; }

    public static StickyNoteAppearance Parse(BoardShape shape)
    {
        var raw = shape.Points?.Trim();
        if (string.IsNullOrEmpty(raw) || raw.StartsWith("[", StringComparison.Ordinal))
        {
            return new StickyNoteAppearance();
        }

        try
        {
            return JsonConvert.DeserializeObject<StickyNoteAppearance>(raw)
                   ?? new StickyNoteAppearance();
        }
        catch
        {
            return new StickyNoteAppearance();
        }
    }

    public void SaveTo(BoardShape shape)
    {
        shape.Points = JsonConvert.SerializeObject(this);
    }

    public string EffectivePaperHex() =>
        string.IsNullOrWhiteSpace(PaperHex) ? DefaultPaper : PaperHex!;

    public string DisplayAuthor() =>
        string.IsNullOrWhiteSpace(Author) ? "" : Author!.Trim().ToUpperInvariant();
}
