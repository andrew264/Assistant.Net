using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assistant.Net.Data.Entities;

[Table("GuildMusicSettings")]
public class GuildMusicSettingsEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public decimal GuildId { get; set; }

    public float Volume { get; set; } = 1.0f;
}