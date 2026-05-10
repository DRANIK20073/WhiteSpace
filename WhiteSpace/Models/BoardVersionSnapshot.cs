using System;
using System.Collections.Generic;

namespace WhiteSpace.Models;

public sealed class BoardVersionSnapshot
{
    public DateTime SavedAtUtc { get; set; }
    public List<BoardShape> Shapes { get; set; } = new();
}
