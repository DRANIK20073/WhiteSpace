using System;
using System.Collections.Generic;

namespace WhiteSpace.Models;

/// <summary>Снимок версии доски: имя, время сохранения и список фигур.</summary>
public sealed class BoardVersionSnapshot
{
    public string? Name { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public List<BoardShape> Shapes { get; set; } = new();

    /// <summary>Имя для UI: заданное пользователем или автоподпись по дате.</summary>
    public string GetDisplayName() =>
        string.IsNullOrWhiteSpace(Name)
            ? $"Версия от {SavedAtUtc.ToLocalTime():dd MMMM yyyy, HH:mm}"
            : Name.Trim();
}
