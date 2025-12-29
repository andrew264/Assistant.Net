using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assistant.Net.Data.Entities;

[Table("PlayHistory")]
public class PlayHistoryEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public decimal GuildId { get; set; }

    [ForeignKey(nameof(GuildId))] public GuildMusicSettingsEntity? GuildSettings { get; set; }

    public long TrackId { get; set; }

    [ForeignKey(nameof(TrackId))] public TrackEntity Track { get; set; } = null!;

    public decimal RequestedBy { get; set; }

    [ForeignKey(nameof(RequestedBy))] public UserEntity Requester { get; set; } = null!;

    public DateTime PlayedAt { get; set; }
}