using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Windows;

[Table("boardshape")]
public class BoardShape : BaseModel
{
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("board_id")]
    public Guid BoardId { get; set; }

    [Column("type")]
    public string Type { get; set; } = "";

    [Column("x")]
    public double X { get; set; }

    [Column("y")]
    public double Y { get; set; }

    [Column("width")]
    public double Width { get; set; }

    [Column("height")]
    public double Height { get; set; }

    [Column("color")]
    public string? Color { get; set; }

    [Column("text")]
    public string? Text { get; set; }

    // Храним строку JSON в базе данных
    [Column("points")]
    public string? Points { get; set; }

    // Используем List<Point> для хранения точек в памяти
    [JsonIgnore]  // Этот атрибут указывает, что это свойство не нужно сериализовать в JSON
    public List<Point> DeserializedPoints { get; set; } = new List<Point>();  // Коллекция точек для работы в коде
}
