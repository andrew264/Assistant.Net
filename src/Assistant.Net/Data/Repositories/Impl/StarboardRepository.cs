using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class StarboardRepository(AssistantDbContext context) : IStarboardRepository
{
    public async Task<StarboardConfigEntity?> GetConfigAsync(ulong guildId) =>
        await context.StarboardConfigs.FindAsync(guildId).ConfigureAwait(false);

    public void AddConfig(StarboardConfigEntity config)
    {
        context.StarboardConfigs.Add(config);
    }

    public async Task<StarredMessageEntity?> GetStarredMessageAsync(ulong guildId, ulong originalMessageId)
    {
        return await context.StarredMessages
            .Include(sm => sm.Votes)
            .FirstOrDefaultAsync(sm => sm.GuildId == guildId && sm.OriginalMessageId == originalMessageId)
            .ConfigureAwait(false);
    }

    public async Task<StarredMessageEntity?> GetStarredMessageByIdAsync(long id)
    {
        return await context.StarredMessages
            .Include(sm => sm.Votes)
            .FirstOrDefaultAsync(sm => sm.Id == id)
            .ConfigureAwait(false);
    }

    public async Task<List<StarredMessageEntity>> GetStarredMessagesByOriginalIdsAsync(ulong guildId,
        IEnumerable<ulong> originalMessageIds)
    {
        return await context.StarredMessages
            .Where(sm => sm.GuildId == guildId && originalMessageIds.Contains(sm.OriginalMessageId))
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public void AddStarredMessage(StarredMessageEntity message)
    {
        context.StarredMessages.Add(message);
    }

    public void RemoveStarredMessage(StarredMessageEntity message)
    {
        context.StarredMessages.Remove(message);
    }

    public void RemoveStarredMessages(IEnumerable<StarredMessageEntity> messages)
    {
        context.StarredMessages.RemoveRange(messages);
    }

    public void AddVote(StarVoteEntity vote)
    {
        context.StarVotes.Add(vote);
    }

    public void RemoveVote(StarVoteEntity vote)
    {
        context.StarVotes.Remove(vote);
    }

    public void RemoveVotes(IEnumerable<StarVoteEntity> votes)
    {
        context.StarVotes.RemoveRange(votes);
    }
}