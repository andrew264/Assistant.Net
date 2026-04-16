using Assistant.Net.Data.Entities;

namespace Assistant.Net.Data.Repositories.Interfaces;

public interface IStarboardRepository
{
    Task<StarboardConfigEntity?> GetConfigAsync(ulong guildId);
    void AddConfig(StarboardConfigEntity config);

    Task<StarredMessageEntity?> GetStarredMessageAsync(ulong guildId, ulong originalMessageId);
    Task<StarredMessageEntity?> GetStarredMessageByIdAsync(long id);

    Task<List<StarredMessageEntity>> GetStarredMessagesByOriginalIdsAsync(ulong guildId,
        IEnumerable<ulong> originalMessageIds);

    void AddStarredMessage(StarredMessageEntity message);
    void RemoveStarredMessage(StarredMessageEntity message);
    void RemoveStarredMessages(IEnumerable<StarredMessageEntity> messages);

    void AddVote(StarVoteEntity vote);
    void RemoveVote(StarVoteEntity vote);
    void RemoveVotes(IEnumerable<StarVoteEntity> votes);
}