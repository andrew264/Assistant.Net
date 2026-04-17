using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assistant.Net.Data.Entities;

[Table("PlayHistory")]
public class PlayHistoryEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public ulong GuildId { get; set; }

    [ForeignKey(nameof(GuildId))] public GuildEntity Guild { get; set; } = null!;

    public long TrackId { get; set; }

    [ForeignKey(nameof(TrackId))] public TrackEntity Track { get; set; } = null!;

    public ulong RequestedBy { get; set; }

    [ForeignKey(nameof(RequestedBy))] public UserEntity Requester { get; set; } = null!;

    public DateTime PlayedAt { get; set; }
}