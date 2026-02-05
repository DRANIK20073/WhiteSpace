using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

[Table("boards")]
public class Board : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("title")]
    public string Title { get; set; } = "";

    [Column("owner_id")]
    public Guid OwnerId { get; set; }

    [Column("access_code")]
    public string AccessCode { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
