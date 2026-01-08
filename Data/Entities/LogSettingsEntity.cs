using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Assistant.Net.Data.Enums;

namespace Assistant.Net.Data.Entities;

[Table("log_settings")]
public class LogSettingsEntity
{
    [Key] [Column("id")] public int Id { get; set; }

    [Column("guild_id")] public decimal GuildId { get; set; }

    [Column("log_type")] public LogType LogType { get; set; }

    [Column("is_enabled")] public bool IsEnabled { get; set; }

    [Column("channel_id")] public decimal? ChannelId { get; set; }

    [Column("delete_delay_ms")] public int DeleteDelayMs { get; set; } = 24 * 60 * 60 * 1000;

    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(GuildId))] public GuildEntity? Guild { get; set; }
}