using Newtonsoft.Json;

namespace WhiteSpace.Models;

/// <summary>Метаданные стикера в поле <see cref="BoardShape.Points"/> (JSON, не координаты).</summary>
public sealed class StickyNoteAppearance
{
    /// <summary>Светло-синий цвет «бумаги» для новых стикеров.</summary>
    public const string DefaultPaper = "#A8D8FF";

    /// <summary>Цвет бумаги нового стикера по умолчанию (#RRGGBB).</summary>
    public static string DefaultPaperHex => DefaultPaper;

    [JsonProperty("author")]
    public string? Author { get; set; }

    /// <summary>Цвет «бумаги» стикера (#RRGGBB).</summary>
    [JsonProperty("paper")]
    public string? PaperHex { get; set; }

    /// <summary>Читает метаданные стикера из Points; старый формат с координатами игнорируется.</summary>
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

    /// <summary>Записывает метаданные обратно в Points фигуры.</summary>
    public void SaveTo(BoardShape shape)
    {
        shape.Points = JsonConvert.SerializeObject(this);
    }

    /// <summary>Цвет бумаги с подстановкой значения по умолчанию.</summary>
    public string EffectivePaperHex() =>
        string.IsNullOrWhiteSpace(PaperHex) ? DefaultPaper : PaperHex!;

    /// <summary>Имя автора для отображения (верхний регистр).</summary>
    public string DisplayAuthor() =>
        string.IsNullOrWhiteSpace(Author) ? "" : Author!.Trim().ToUpperInvariant();
}
