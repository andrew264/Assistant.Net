using System.Collections.Concurrent;
using System.Text;
using Assistant.Net.Utilities;
using Discord;

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

    private ConcurrentDictionary<string, double> Ratings { get; } = new();
    private HashSet<ulong> Voters { get; } = [];
    public ulong CreatorId { get; }
    public string Title { get; }
    private List<string> Candidates { get; }

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

    public MessageComponent GenerateResultsComponent()
    {
        var sortedRatings = Ratings.OrderByDescending(pair => pair.Value).ToList();

        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder($"# {Title.Truncate(250)} | Final Results"))
            .WithTextDisplay(new TextDisplayBuilder($"Poll by <@{CreatorId}>"))
            .WithSeparator();

        if (sortedRatings.Count > 0)
        {
            var highestRating = sortedRatings.First().Value;
            var winners = sortedRatings
                .Where(pair => Math.Abs(pair.Value - highestRating) < 0.01)
                .Select(pair => pair.Key)
                .ToList();

            var insights = new StringBuilder();
            insights.AppendLine($"🏆 **Winner{(winners.Count > 1 ? "s" : "")}:** {string.Join(", ", winners)}");
            insights.AppendLine($"⭐ **Highest Rating:** {highestRating:F2}");
            insights.AppendLine($"👥 **Total Voters:** {Voters.Count}");
            container.WithTextDisplay(new TextDisplayBuilder(insights.ToString()));
            container.WithSeparator();
        }

        container.WithTextDisplay(new TextDisplayBuilder("## 📈 Final Elo Ratings"));

        var rankings = new StringBuilder();
        if (sortedRatings.Count == 0)
            rankings.AppendLine("No ratings available.");
        else
            for (var i = 0; i < sortedRatings.Count; i++)
            {
                var (candidate, rating) = sortedRatings[i];
                rankings.AppendLine($"{i + 1}. **{candidate}**: {rating:F2}");
            }

        container.WithTextDisplay(new TextDisplayBuilder(rankings.ToString()));

        return new ComponentBuilderV2().WithContainer(container).Build();
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