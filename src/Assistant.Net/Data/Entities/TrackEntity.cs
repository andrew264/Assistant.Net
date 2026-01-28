using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Entities;

[Table("Tracks")]
[Index(nameof(Uri), IsUnique = true)]
public class TrackEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required] [MaxLength(2048)] public string Uri { get; set; } = null!;

    [Required] [MaxLength(512)] public string Title { get; set; } = null!;

    [MaxLength(255)] public string? Artist { get; set; }

    [MaxLength(2048)] public string? ThumbnailUrl { get; set; }

    public double Duration { get; set; }

    [MaxLength(50)] public string Source { get; set; } = "unknown";
}