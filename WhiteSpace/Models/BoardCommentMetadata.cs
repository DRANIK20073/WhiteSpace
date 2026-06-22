using Newtonsoft.Json;
using System;

namespace WhiteSpace.Models;

/// <summary>Сериализуется в <see cref="BoardShape.Text"/> для типа comment.</summary>
public sealed class BoardCommentMetadata
{
    [JsonProperty("authorId")] public string? AuthorId { get; set; }
    [JsonProperty("authorName")] public string AuthorName { get; set; } = "";
    [JsonProperty("message")] public string Message { get; set; } = "";
    [JsonProperty("createdAtUtc")] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Имя автора для отображения в UI (верхний регистр, fallback «УЧАСТНИК»).</summary>
    public string DisplayAuthor() =>
        string.IsNullOrWhiteSpace(AuthorName) ? "УЧАСТНИК" : AuthorName.Trim().ToUpperInvariant();
}

/// <summary>Разбор и форматирование комментариев на доске из поля Text.</summary>
public static class BoardCommentMetadataHelper
{
    /// <summary>Читает JSON или plain-text fallback для старых записей.</summary>
    public static bool TryParse(string? text, out BoardCommentMetadata meta)
    {
        meta = new BoardCommentMetadata();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith('{'))
        {
            meta.Message = text;
            return true;
        }

        try
        {
            meta = JsonConvert.DeserializeObject<BoardCommentMetadata>(text) ?? new BoardCommentMetadata();
            return true;
        }
        catch
        {
            meta.Message = text;
            return true;
        }
    }

    /// <summary>Сериализует комментарий для сохранения в Text.</summary>
    public static string Serialize(BoardCommentMetadata meta) =>
        JsonConvert.SerializeObject(meta);

    /// <summary>Человекочитаемое «сколько времени назад» для подписи комментария.</summary>
    public static string FormatRelativeTime(DateTime createdAtUtc)
    {
        var delta = DateTime.UtcNow - createdAtUtc;
        if (delta.TotalSeconds < 60)
        {
            return "только что";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{(int)delta.TotalMinutes} мин. назад";
        }

        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours} ч. назад";
        }

        return createdAtUtc.ToLocalTime().ToString("g");
    }
}
