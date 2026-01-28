using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assistant.Net.Data.Entities;

[Table("Reminders")]
public class ReminderEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public decimal CreatorId { get; set; }

    [ForeignKey(nameof(CreatorId))] public UserEntity Creator { get; set; } = null!;

    public decimal? TargetUserId { get; set; }

    [ForeignKey(nameof(TargetUserId))] public UserEntity? TargetUser { get; set; }

    public decimal GuildId { get; set; }

    [ForeignKey(nameof(GuildId))] public GuildEntity Guild { get; set; } = null!;

    public decimal ChannelId { get; set; }

    [Required] [MaxLength(2000)] public string Message { get; set; } = null!;

    public DateTime TriggerTime { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(50)] public string? Recurrence { get; set; }

    [MaxLength(200)] public string? Title { get; set; }

    public DateTime? LastTriggered { get; set; }

    public bool IsActive { get; set; }

    public bool IsDm { get; set; }
}