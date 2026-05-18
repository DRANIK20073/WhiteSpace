using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

/// <summary>Частичное обновление блокировки в profiles.</summary>
[Table("profiles")]
public class ProfileBanPatch : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("is_banned")]
    public bool IsBanned { get; set; }

    [Column("banned_at")]
    public DateTime? BannedAt { get; set; }

    [Column("ban_reason")]
    public string? BanReason { get; set; }
}
