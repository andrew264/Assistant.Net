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

    [Required] public string Uri { get; set; } = null!;

    [Required] public string Title { get; set; } = null!;

    public string? Artist { get; set; }

    public string? ThumbnailUrl { get; set; }

    public double Duration { get; set; }

    public string Source { get; set; } = "unknown";
}