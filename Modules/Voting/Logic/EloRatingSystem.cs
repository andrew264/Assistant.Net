using System.Collections.Concurrent;
using System.Text;

namespace Assistant.Net.Modules.Voting.Logic;

public class EloRatingSystem
{
    private const double InitialRating = 1000.0;
    private const double KFactor = 32.0;
    private readonly Random _random = new();

    public EloRatingSystem(IEnumerable<string> candidates, ulong creatorId, string title)
    {
        Candidates = candidates.Distinct().ToList();
        CreatorId = creatorId;
        Title = title;

        if (Candidates.Count < 2)
            throw new ArgumentException("At least two unique candidates are required for an Elo poll.",
                nameof(candidates));

        foreach (var candidate in Candidates) Ratings.TryAdd(candidate, InitialRating);
    }

    public ConcurrentDictionary<string, double> Ratings { get; } = new();
    public HashSet<ulong> Voters { get; } = [];
    public ulong CreatorId { get; }
    public string Title { get; }
    public List<string> Candidates { get; }

    public void AddVoter(ulong userId)
    {
        Voters.Add(userId);
    }

    public bool HasVotedBefore(ulong userId) => Voters.Contains(userId);

    public List<(string Candidate1, string Candidate2)> GetShuffledCandidatePairings()
    {
        var allPairs = new List<(string, string)>();
        for (var i = 0; i < Candidates.Count; i++)
        for (var j = i + 1; j < Candidates.Count; j++)
            allPairs.Add((Candidates[i], Candidates[j]));

        // Fisher-Yates shuffle
        var n = allPairs.Count;
        while (n > 1)
        {
            n--;
            var k = _random.Next(n + 1);
            (allPairs[k], allPairs[n]) = (allPairs[n], allPairs[k]);
        }

        return allPairs;
    }

    private static double CalculateExpectedScore(double ratingA, double ratingB) =>
        1.0 / (1.0 + Math.Pow(10.0, (ratingB - ratingA) / 400.0));

    public void UpdateRatings(string winner, string loser)
    {
        if (!Ratings.TryGetValue(winner, out var winnerRating) || !Ratings.TryGetValue(loser, out var loserRating))
        {
            Console.WriteLine($"Error: Candidate not found during rating update. Winner: {winner}, Loser: {loser}");
            return;
        }

        var expectedWinner = CalculateExpectedScore(winnerRating, loserRating);
        var expectedLoser = CalculateExpectedScore(loserRating, winnerRating);

        var newWinnerRating = winnerRating + KFactor * (1.0 - expectedWinner);
        var newLoserRating = loserRating + KFactor * (0.0 - expectedLoser);

        Ratings[winner] = newWinnerRating;
        Ratings[loser] = newLoserRating;
    }

    public string GenerateSummary()
    {
        var sortedRatings = Ratings.OrderByDescending(pair => pair.Value).ToList();

        var highestRating = sortedRatings.Count > 0 ? sortedRatings.First().Value : InitialRating;
        var lowestRating = sortedRatings.Count > 0 ? sortedRatings.Last().Value : InitialRating;

        var highestRatedCandidates = sortedRatings
            .Where(pair => Math.Abs(pair.Value - highestRating) < 0.01)
            .Select(pair => pair.Key)
            .ToList();

        var lowestRatedCandidates = sortedRatings
            .Where(pair => Math.Abs(pair.Value - lowestRating) < 0.01)
            .Select(pair => pair.Key)
            .ToList();

        var summary = new StringBuilder();
        summary.AppendLine($"# {Title} | Results (Poll by <@{CreatorId}>)");
        summary.AppendLine();

        summary.AppendLine("## Insights");
        summary.AppendLine($"- **Highest Rating**: {highestRating:F2} ({string.Join(", ", highestRatedCandidates)})");
        summary.AppendLine($"- **Lowest Rating**: {lowestRating:F2} ({string.Join(", ", lowestRatedCandidates)})");
        summary.AppendLine($"- **Total Votes Cast**: {Voters.Count}");
        summary.AppendLine();

        summary.AppendLine("## Final Elo Ratings");
        if (sortedRatings.Count == 0)
            summary.AppendLine("No ratings available.");
        else
            foreach (var (candidate, rating) in sortedRatings)
                summary.AppendLine($"- **{candidate}**: {rating:F2}");

        return summary.ToString();
    }

    public static string EncodeCandidate(string candidate) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(candidate))
            .Replace('+', '-')
            .Replace('/', '_');

    public static string DecodeCandidate(string encodedCandidate)
    {
        var base64 = encodedCandidate
            .Replace('-', '+')
            .Replace('_', '/');
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}