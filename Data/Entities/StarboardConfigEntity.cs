using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assistant.Net.Data.Entities;

[Table("StarboardConfigs")]
public class StarboardConfigEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public decimal GuildId { get; set; }

    public bool IsEnabled { get; set; }

    public decimal? StarboardChannelId { get; set; }

    public string StarEmoji { get; set; } = "⭐";

    public int Threshold { get; set; } = 3;

    public bool AllowSelfStar { get; set; }

    public bool AllowBotMessages { get; set; }

    public bool IgnoreNsfwChannels { get; set; } = true;

    public bool DeleteIfUnStarred { get; set; }

    public decimal? LogChannelId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}