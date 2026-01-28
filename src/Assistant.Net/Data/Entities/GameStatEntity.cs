using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Entities;

[Table("GameStats")]
[PrimaryKey(nameof(GuildId), nameof(UserId), nameof(GameType))]
public class GameStatEntity
{
    public decimal GuildId { get; set; }

    [ForeignKey(nameof(GuildId))] public GuildEntity Guild { get; set; } = null!;

    public decimal UserId { get; set; }

    [ForeignKey(nameof(UserId))] public UserEntity User { get; set; } = null!;

    public int GameType { get; set; }

    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Ties { get; set; }
    public double Elo { get; set; } = 1000.0;
}