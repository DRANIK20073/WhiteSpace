using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;
using System.Windows;

[Table("boardshape")]
public class BoardShape : BaseModel
{
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("board_id")]
    public Guid BoardId { get; set; }

    [Column("type")]
    public string Type { get; set; }

    [Column("x")]
    public double X { get; set; }

    [Column("y")]
    public double Y { get; set; }

    [Column("width")]
    public double Width { get; set; }

    [Column("height")]
    public double Height { get; set; }

    [Column("color")]
    public string Color { get; set; }

    [Column("text")]
    public string Text { get; set; }

    [Column("points")]
    public List<Point> Points { get; set; } = new List<Point>();
}
