namespace Assistant.Net.Data.Repositories.Interfaces;

public interface IUnitOfWork : IAsyncDisposable, IDisposable
{
    IUserRepository Users { get; }
    IGuildRepository Guilds { get; }
    IGameStatsRepository GameStats { get; }
    IMusicRepository Music { get; }
    IPlaylistRepository Playlists { get; }
    IReminderRepository Reminders { get; }
    IStarboardRepository Starboard { get; }
    ILoggingConfigRepository Logging { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}