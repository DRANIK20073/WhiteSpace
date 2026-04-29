using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System;

namespace WhiteSpace.Pages
{
    [Table("admin_credentials")]
    public class AdminCredential : BaseModel
    {
        [PrimaryKey("id")]
        public long Id { get; set; }

        [Column("login")]
        public string Login { get; set; } = string.Empty;

        [Column("password")]
        public string Password { get; set; } = string.Empty;

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
