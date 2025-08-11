using System.Collections.Concurrent;
using Assistant.Net.Modules.Voting.Logic;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Voting;

public class PollService(ILogger<PollService> logger)
{
    private readonly ConcurrentDictionary<ulong, EloRatingSystem> _activePolls = new();
    private readonly ConcurrentDictionary<ulong, UserVotingState> _userVotingStates = new();

    // Poll Management
    public bool TryGetPoll(ulong channelId, out EloRatingSystem? poll) => _activePolls.TryGetValue(channelId, out poll);

    public bool CreatePoll(ulong channelId, ulong creatorId, string title, List<string> candidates,
        out EloRatingSystem? poll, out string? errorMessage)
    {
        if (_activePolls.TryGetValue(channelId, out var existingPoll))
        {
            poll = existingPoll;
            errorMessage =
                $"There is already an active poll ('{poll.Title}') in this channel, created by <@{poll.CreatorId}>. Use `/poll results` to finish it first.";
            return false;
        }

        try
        {
            poll = new EloRatingSystem(candidates, creatorId, title);
            if (!_activePolls.TryAdd(channelId, poll))
            {
                errorMessage = "Failed to create poll due to a conflict. Please try again.";
                logger.LogWarning("Failed to add poll for Channel {ChannelId} due to race condition.", channelId);
                return false;
            }

            errorMessage = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            poll = null;
            errorMessage = $"Error creating poll: {ex.Message}";
            return false;
        }
    }

    public bool TryEndPoll(ulong channelId, out EloRatingSystem? poll)
    {
        if (!_activePolls.TryRemove(channelId, out poll)) return false;
        // Clean up any ongoing voting sessions for this poll
        var userKeysToRemove = _userVotingStates
            .Where(kvp => kvp.Value.ChannelId == channelId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in userKeysToRemove) _userVotingStates.TryRemove(key, out _);

        return true;
    }

    // Voting Session Management
    public bool HasActiveVotingSession(ulong userId) => _userVotingStates.ContainsKey(userId);

    public UserVotingState? StartVotingSession(ulong userId, ulong channelId, EloRatingSystem poll)
    {
        var shuffledPairs = poll.GetShuffledCandidatePairings();
        if (shuffledPairs.Count == 0)
        {
            logger.LogWarning("No pairs generated for poll in channel {ChannelId}", channelId);
            return null;
        }

        var userState = new UserVotingState(userId, channelId, shuffledPairs);

        if (_userVotingStates.TryAdd(userId, userState)) return userState;

        logger.LogWarning("Failed to add user voting state for User {UserId} due to race condition.", userId);
        return null;
    }

    public bool TryGetUserVotingState(ulong userId, out UserVotingState? userState) =>
        _userVotingStates.TryGetValue(userId, out userState);

    public void EndVotingSession(ulong userId)
    {
        _userVotingStates.TryRemove(userId, out _);
    }
}