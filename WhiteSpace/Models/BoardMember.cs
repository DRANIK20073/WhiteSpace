using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System;

namespace WhiteSpace.Pages
{
    [Table("board_members")]
    public class BoardMember : BaseModel
    {
        [PrimaryKey("id")]
        public int Id { get; set; }

        [Column("board_id")]
        public Guid BoardId { get; set; }

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Column("role")]
        public string Role { get; set; } = "viewer"; // "editor" или "viewer"

        [Column("joined_at")]
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }
}