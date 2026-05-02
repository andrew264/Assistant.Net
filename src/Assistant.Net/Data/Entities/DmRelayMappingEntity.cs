using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assistant.Net.Data.Entities;

[Table("DmRelayMappings")]
public class DmRelayMappingEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public ulong UserId { get; set; }

    [ForeignKey(nameof(UserId))] public UserEntity User { get; set; } = null!;

    public ulong OriginalMessageId { get; set; }

    public ulong RelayMessageId { get; set; }
}