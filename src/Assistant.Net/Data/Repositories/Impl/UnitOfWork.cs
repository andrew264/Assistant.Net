using Assistant.Net.Data.Repositories.Interfaces;

namespace Assistant.Net.Data.Repositories.Impl;

public class UnitOfWork(AssistantDbContext context) : IUnitOfWork
{
    public IUserRepository Users => field ??= new UserRepository(context);
    public IGuildRepository Guilds => field ??= new GuildRepository(context);
    public IGameStatsRepository GameStats => field ??= new GameStatsRepository(context);
    public IMusicRepository Music => field ??= new MusicRepository(context);
    public IPlaylistRepository Playlists => field ??= new PlaylistRepository(context);
    public IReminderRepository Reminders => field ??= new ReminderRepository(context);
    public IStarboardRepository Starboard => field ??= new StarboardRepository(context);
    public ILoggingConfigRepository Logging => field ??= new LoggingConfigRepository(context);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

    public void Dispose()
    {
        context.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await context.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}