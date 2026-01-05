using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assistant.Net.Data.Entities;

[Table("StarredMessages")]
public class StarredMessageEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public decimal GuildId { get; set; }

    [ForeignKey(nameof(GuildId))] public GuildEntity Guild { get; set; } = null!;

    public decimal OriginalChannelId { get; set; }

    public decimal OriginalMessageId { get; set; }

    public decimal AuthorId { get; set; }

    [ForeignKey(nameof(AuthorId))] public UserEntity Author { get; set; } = null!;

    public decimal? StarboardMessageId { get; set; }

    public int StarCount { get; set; }

    public bool IsPosted { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    public ICollection<StarVoteEntity> Votes { get; set; } = new List<StarVoteEntity>();
}