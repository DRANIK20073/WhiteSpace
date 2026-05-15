namespace WhiteSpace.Models;

public sealed class BoardShapeChange
{
    public int ShapeId { get; init; }

    public BoardShape? Shape { get; init; }

    public bool IsDelete => Shape == null;
}
