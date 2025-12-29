using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Entities;

[Table("StarVotes")]
[PrimaryKey(nameof(StarredMessageId), nameof(UserId))]
public class StarVoteEntity
{
    public long StarredMessageId { get; set; }

    [ForeignKey(nameof(StarredMessageId))] public StarredMessageEntity StarredMessage { get; set; } = null!;

    public decimal UserId { get; set; }

    [ForeignKey(nameof(UserId))] public UserEntity User { get; set; } = null!;
}