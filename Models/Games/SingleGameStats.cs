using Assistant.Net.Services.Games;
using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Games;

public record SingleGameStats
{
    [BsonElement("wins")] public int Wins { get; set; } = 0;

    [BsonElement("losses")] public int Losses { get; set; } = 0;

    [BsonElement("ties")] public int Ties { get; set; } = 0;

    [BsonElement("elo")] public double Elo { get; set; } = GameStatsService.DefaultElo;

    [BsonElement("matches_played")] public int MatchesPlayed { get; set; } = 0;
}