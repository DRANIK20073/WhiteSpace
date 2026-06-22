using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;

/// <summary>Профиль пользователя в Supabase, включая статус блокировки.</summary>
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

    [Column("is_banned")]
    public bool IsBanned { get; set; }

    [Column("banned_at")]
    public DateTime? BannedAt { get; set; }

    [Column("ban_reason")]
    public string? BanReason { get; set; }
}
