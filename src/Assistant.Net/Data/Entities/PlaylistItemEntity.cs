using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assistant.Net.Data.Entities;

[Table("PlaylistItems")]
public class PlaylistItemEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int PlaylistId { get; set; }

    [ForeignKey(nameof(PlaylistId))] public PlaylistEntity Playlist { get; set; } = null!;

    public long TrackId { get; set; }

    [ForeignKey(nameof(TrackId))] public TrackEntity Track { get; set; } = null!;

    public int Position { get; set; }
}