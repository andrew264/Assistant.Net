using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assistant.Net.Data.Entities;

[Table("DmRelayChannels")]
public class DmRelayChannelEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public ulong UserId { get; set; }

    [ForeignKey(nameof(UserId))] public UserEntity User { get; set; } = null!;

    public ulong ChannelId { get; set; }
}