using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assistant.Net.Data.Entities;

[Table("GuildMusicSettings")]
public class GuildMusicSettingsEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public ulong GuildId { get; set; }

    [ForeignKey(nameof(GuildId))] public GuildEntity Guild { get; set; } = null!;

    public float Volume
    {
        get;
        set => field = Math.Clamp(value, 0.0f, 2.0f);
    } = 1.0f;
}