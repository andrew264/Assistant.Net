namespace Assistant.Net.Services.Features.Logic;

/// <summary>
///     Manages the state for a single user's ephemeral voting session.
/// </summary>
public class UserVotingState(ulong userId, ulong channelId, List<(string Candidate1, string Candidate2)> shuffledPairs)
{
    public ulong UserId { get; } = userId;
    public ulong ChannelId { get; } = channelId;
    public List<(string Candidate1, string Candidate2)> ShuffledPairs { get; } = shuffledPairs;
    public int CurrentPairIndex { get; set; }

    public bool IsVotingComplete => CurrentPairIndex >= ShuffledPairs.Count;

    public (string Candidate1, string Candidate2)? GetCurrentPair()
    {
        if (CurrentPairIndex >= ShuffledPairs.Count) return null;
        return ShuffledPairs[CurrentPairIndex];
    }
}