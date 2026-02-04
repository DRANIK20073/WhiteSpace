using Supabase.Postgrest.Models;  // именно этот BaseModel нужен
using Supabase.Postgrest.Attributes;

[Table("profiles")]
public class Profile : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("email")]
    public string Email { get; set; }

    [Column("username")]
    public string Username { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
