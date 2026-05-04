namespace WhiteSpace.Models;

/// <summary>Подбор «нормальных» пропорций при смене вида фигуры (без сильного сжатия по одной оси).</summary>
public static class ShapeLayoutHelper
{
    public static (double W, double H) NormalizeOnKindChange(string paletteId, double w, double h)
    {
        w = Math.Max(w, 1);
        h = Math.Max(h, 1);
        var minD = Math.Min(w, h);
        var maxD = Math.Max(w, h);
        var aspect = w / h;

        if (paletteId == "circle")
        {
            var d = maxD;
            return (d, d);
        }

        if (IsRoughlySquareKind(paletteId))
        {
            var side = maxD;
            return (side, side);
        }

        // Сильно вытянутый прямоугольник → мягко приводим к читаемому соотношению
        if (aspect > 2.4 || aspect < 0.42)
        {
            return (maxD, maxD * 0.75);
        }

        if (minD < 32)
        {
            return (Math.Max(w, 40), Math.Max(h, 40));
        }

        return (w, h);
    }

    private static bool IsRoughlySquareKind(string id) =>
        id is "rect" or "roundRect" or "diamond" or "triangle" or "triangleInv" or "pentagon" or "octagon" or
            "plus" or "star" or "prepHex" or "flowPentagon" or "orGate" or "magDisk" or "process" or
            "callout" or "cylinderV" or "cylinderH" or "internalStorage" or "card" or "folder" or
            "document" or "multiDoc" or "storedData" or "trapezoid" or "manualInput" or "parallelogram" or
            "parallelogramAlt" or "arrowLeft" or "arrowRight";
}
