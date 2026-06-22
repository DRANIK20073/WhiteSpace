namespace WhiteSpace.Models;

/// <summary>Событие изменения фигуры из Firebase: обновление или удаление (Shape == null).</summary>
public sealed class BoardShapeChange
{
    public int ShapeId { get; init; }

    public BoardShape? Shape { get; init; }

    public bool IsDelete => Shape == null;
}
