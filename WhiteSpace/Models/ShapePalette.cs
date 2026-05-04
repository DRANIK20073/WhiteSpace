namespace WhiteSpace.Models;

/// <summary>Палитра фигур для доски: соответствует категориям «Базовые» и «Блок-схема».</summary>
public static class ShapePalette
{
    public sealed record Entry(string Id, string Title);

    public static IReadOnlyList<Entry> Basic { get; }
    public static IReadOnlyList<Entry> Flowchart { get; }

    static ShapePalette()
    {
        Basic = new[]
        {
            new Entry("rect", "Квадрат"),
            new Entry("circle", "Круг"),
            new Entry("diamond", "Ромб"),
            new Entry("triangle", "Треугольник"),
            new Entry("triangleInv", "Треугольник вниз"),
            new Entry("roundRect", "Скруглённый"),
            new Entry("pentagon", "Пятиугольник"),
            new Entry("octagon", "Восьмиугольник"),
            new Entry("plus", "Плюс"),
            new Entry("arrowLeft", "Стрелка влево"),
            new Entry("arrowRight", "Стрелка вправо"),
            new Entry("process", "Процесс"),
            new Entry("star", "Звезда"),
            new Entry("callout", "Выноска"),
        };

        Flowchart = new[]
        {
            new Entry("parallelogram", "Данные"),
            new Entry("parallelogramAlt", "Данные (наклон)"),
            new Entry("cylinderV", "Цилиндр"),
            new Entry("cylinderH", "Цилиндр гориз."),
            new Entry("document", "Документ"),
            new Entry("folder", "Папка"),
            new Entry("storedData", "Хранение"),
            new Entry("multiDoc", "Несколько док."),
            new Entry("internalStorage", "Внутр. память"),
            new Entry("flowPentagon", "Слияние"),
            new Entry("trapezoid", "Трапеция"),
            new Entry("manualInput", "Ручной ввод"),
            new Entry("prepHex", "Подготовка"),
            new Entry("card", "Соединение"),
            new Entry("magDisk", "Накопитель"),
            new Entry("orGate", "Или"),
        };
    }

    /// <summary>Тип в БД и значение sk в JSON (null — не писать).</summary>
    public static (string DbType, string? KindForJson) ResolveStorage(string paletteId)
    {
        if (paletteId == "circle")
        {
            return ("ellipse", null);
        }

        if (string.IsNullOrEmpty(paletteId) || paletteId == "rect")
        {
            return ("rectangle", null);
        }

        return ("rectangle", paletteId);
    }

    public static string GetPaletteId(BoardShape shape)
    {
        if (shape.Type == "ellipse")
        {
            return "circle";
        }

        var app = RectEllipseAppearance.Parse(shape);
        if (string.IsNullOrEmpty(app.ShapeKind) || app.ShapeKind == "rect")
        {
            return "rect";
        }

        return app.ShapeKind;
    }
}
