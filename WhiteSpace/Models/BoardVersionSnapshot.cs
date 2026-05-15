using System;
using System.Collections.Generic;

namespace WhiteSpace.Models;

public sealed class BoardVersionSnapshot
{
    public string? Name { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public List<BoardShape> Shapes { get; set; } = new();

    public string GetDisplayName() =>
        string.IsNullOrWhiteSpace(Name)
            ? $"Версия от {SavedAtUtc.ToLocalTime():dd MMMM yyyy, HH:mm}"
            : Name.Trim();
}
