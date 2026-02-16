using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

public class BoardShape : BaseModel
{
    [JsonIgnore]
    [PrimaryKey]
    public string PrimaryKey { get; set; }

    public Guid Id { get; set; }
    public string Type { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Color { get; set; }
    public string Text { get; set; }
}
