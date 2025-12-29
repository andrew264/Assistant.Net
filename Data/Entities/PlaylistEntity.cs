using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assistant.Net.Data.Entities;

[Table("Playlists")]
public class PlaylistEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public decimal UserId { get; set; }

    [ForeignKey(nameof(UserId))] public UserEntity User { get; set; } = null!;

    public decimal GuildId { get; set; }

    [Required] public string Name { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PlaylistItemEntity> Items { get; set; } = new List<PlaylistItemEntity>();
}