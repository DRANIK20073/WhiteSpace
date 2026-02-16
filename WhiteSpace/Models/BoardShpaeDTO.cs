using System;
using System.Collections.Generic;
using System.Windows; // Добавляем для Point

namespace WhiteSpace.Models
{
    public class BoardShapeDto
    {
        public int Id { get; set; }
        public Guid BoardId { get; set; }
        public string Type { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Color { get; set; }
        public string Text { get; set; }
        public string Points { get; set; }

        // Для десериализации точек на клиенте
        [System.Text.Json.Serialization.JsonIgnore]
        public List<Point> DeserializedPoints { get; set; }
    }
}