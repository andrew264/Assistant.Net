using Assistant.Net.Data;
using Assistant.Net.Data.Entities;
using Lavalink4NET.Tracks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Data;

public record PlaylistOperationResult(bool Success, string Message, PlaylistEntity? Playlist = null);

public record PlaylistCreationResult(
    bool Success,
    string Message,
    PlaylistEntity? Playlist = null,
    bool LimitReached = false,
    bool NameExists = false);

public class PlaylistService(
    IDbContextFactory<AssistantDbContext> dbFactory,
    ILogger<PlaylistService> logger,
    UserService userService)
{
    private const int MaxPlaylistsPerUser = 10;
    private const int MaxSongsPerPlaylist = 200;

    public async Task<PlaylistCreationResult> CreatePlaylistAsync(ulong userId, ulong guildId, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return new PlaylistCreationResult(false, "Playlist name must be between 1 and 100 characters.",
                NameExists: true);

        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var dUserId = (decimal)userId;
        var dGuildId = (decimal)guildId;

        await userService.EnsureUserExistsAsync(context, userId).ConfigureAwait(false);

        var userPlaylistCount = await context.Playlists
            .CountAsync(p => p.UserId == dUserId && p.GuildId == dGuildId).ConfigureAwait(false);

        if (userPlaylistCount >= MaxPlaylistsPerUser)
            return new PlaylistCreationResult(false,
                $"You have reached the maximum limit of {MaxPlaylistsPerUser} playlists.", LimitReached: true);

        var existingByName = await context.Playlists
            .FirstOrDefaultAsync(p => p.UserId == dUserId && p.GuildId == dGuildId && p.Name == name)
            .ConfigureAwait(false);

        if (existingByName != null)
            return new PlaylistCreationResult(false, "You already have a playlist with that name.", NameExists: true);

        var newPlaylist = new PlaylistEntity
        {
            UserId = dUserId,
            GuildId = dGuildId,
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            context.Playlists.Add(newPlaylist);
            await context.SaveChangesAsync().ConfigureAwait(false);
            logger.LogInformation("Created playlist '{PlaylistName}' for User {UserId} in Guild {GuildId}", name,
                userId, guildId);
            return new PlaylistCreationResult(true, $"Playlist '{name}' created successfully!", newPlaylist);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Error creating playlist '{PlaylistName}' for User {UserId}, Guild {GuildId}", name,
                userId, guildId);
            return new PlaylistCreationResult(false, "An error occurred while creating the playlist.");
        }
    }

    public async Task<PlaylistEntity?> GetPlaylistAsync(ulong userId, ulong guildId, string name)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var dUserId = (decimal)userId;
        var dGuildId = (decimal)guildId;

        return await context.Playlists
            .Include(p => p.Items)
            .ThenInclude(i => i.Track)
            .FirstOrDefaultAsync(p => p.UserId == dUserId && p.GuildId == dGuildId && p.Name == name)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeletePlaylistAsync(ulong userId, ulong guildId, string name)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var dUserId = (decimal)userId;
        var dGuildId = (decimal)guildId;

        var playlist = await context.Playlists
            .FirstOrDefaultAsync(p => p.UserId == dUserId && p.GuildId == dGuildId && p.Name == name)
            .ConfigureAwait(false);

        if (playlist == null)
        {
            logger.LogWarning(
                "Attempted to delete non-existent playlist '{PlaylistName}' for User {UserId} in Guild {GuildId}", name,
                userId, guildId);
            return false;
        }

        context.Playlists.Remove(playlist);
        await context.SaveChangesAsync().ConfigureAwait(false);
        logger.LogInformation("Deleted playlist '{PlaylistName}' for User {UserId} in Guild {GuildId}", name, userId,
            guildId);
        return true;
    }

    public async Task<PlaylistOperationResult> AddTracksToPlaylistAsync(ulong userId, ulong guildId,
        string playlistName, IReadOnlyCollection<LavalinkTrack> tracksToAdd)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var dUserId = (decimal)userId;
        var dGuildId = (decimal)guildId;

        var playlist = await context.Playlists
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.UserId == dUserId && p.GuildId == dGuildId && p.Name == playlistName)
            .ConfigureAwait(false);

        if (playlist == null) return new PlaylistOperationResult(false, "Playlist not found.");

        var initialSongCount = playlist.Items.Count;
        var spaceAvailable = MaxSongsPerPlaylist - initialSongCount;

        if (spaceAvailable <= 0) return new PlaylistOperationResult(false, "Playlist is already full.");

        var actualTracksToAdd = tracksToAdd.Take(spaceAvailable).ToList();
        if (actualTracksToAdd.Count == 0)
            return new PlaylistOperationResult(false,
                "No songs provided or playlist capacity would be exceeded (even with one song).");

        var addedCount = 0;
        var currentPosition = initialSongCount;

        foreach (var trackModel in actualTracksToAdd)
        {
            if (trackModel.Uri is null) continue;
            var trackUri = trackModel.Uri.ToString();

            var track = await context.Tracks.FirstOrDefaultAsync(t => t.Uri == trackUri).ConfigureAwait(false);
            if (track == null)
            {
                track = new TrackEntity
                {
                    Uri = trackUri,
                    Title = trackModel.Title,
                    Artist = trackModel.Author,
                    ThumbnailUrl = trackModel.ArtworkUri?.ToString(),
                    Duration = trackModel.Duration.TotalSeconds,
                    Source = trackModel.SourceName ?? "other"
                };
                context.Tracks.Add(track);
                await context.SaveChangesAsync().ConfigureAwait(false);
            }

            var item = new PlaylistItemEntity
            {
                PlaylistId = playlist.Id,
                TrackId = track.Id,
                Position = ++currentPosition
            };
            context.PlaylistItems.Add(item);
            addedCount++;
        }

        try
        {
            await context.SaveChangesAsync().ConfigureAwait(false);
            var message = $"Added {addedCount} song(s) to '{playlistName}'.";
            if (tracksToAdd.Count > addedCount)
                message += $" Could not add {tracksToAdd.Count - addedCount} song(s) due to playlist capacity.";

            logger.LogInformation(
                "Added {AddedCount} songs to playlist '{PlaylistName}' for User {UserId}, Guild {GuildId}.",
                addedCount, playlistName, userId, guildId);

            return new PlaylistOperationResult(true, message, playlist);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add songs to playlist '{PlaylistName}' for User {UserId}, Guild {GuildId}.",
                playlistName, userId, guildId);
            return new PlaylistOperationResult(false, "Failed to add songs to the playlist.");
        }
    }

    public async Task<(bool Success, string Message, TrackEntity? RemovedTrack)> RemoveSongFromPlaylistAsync(
        ulong userId,
        ulong guildId, string playlistName, int oneBasedIndex)
    {
        if (oneBasedIndex <= 0) return (false, "Invalid song index. Position must be 1 or greater.", null);

        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var dUserId = (decimal)userId;
        var dGuildId = (decimal)guildId;

        var playlist = await context.Playlists
            .Include(p => p.Items)
            .ThenInclude(i => i.Track)
            .FirstOrDefaultAsync(p => p.UserId == dUserId && p.GuildId == dGuildId && p.Name == playlistName)
            .ConfigureAwait(false);

        if (playlist == null) return (false, "Playlist not found.", null);

        var sortedItems = playlist.Items.OrderBy(i => i.Position).ToList();
        if (oneBasedIndex > sortedItems.Count) return (false, "Invalid song index; it's out of bounds.", null);

        var itemToRemove = sortedItems[oneBasedIndex - 1];
        var removedTrack = itemToRemove.Track;

        context.PlaylistItems.Remove(itemToRemove);

        for (var i = oneBasedIndex; i < sortedItems.Count; i++) sortedItems[i].Position--;

        try
        {
            await context.SaveChangesAsync().ConfigureAwait(false);
            logger.LogInformation(
                "Removed song '{SongTitle}' from playlist '{PlaylistName}' for User {UserId}, Guild {GuildId}",
                itemToRemove.Track.Title, playlistName, userId, guildId);
            return (true, $"Removed '{itemToRemove.Track.Title}' from playlist '{playlistName}'.", removedTrack);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to remove song from playlist '{PlaylistName}' (Index {Index}) for User {UserId}, Guild {GuildId}.",
                playlistName, oneBasedIndex, userId, guildId);
            return (false, "Failed to remove song from the playlist.", null);
        }
    }

    public async Task<List<PlaylistEntity>> GetUserPlaylistsAsync(ulong userId, ulong guildId)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var dUserId = (decimal)userId;
        var dGuildId = (decimal)guildId;

        return await context.Playlists
            .Where(p => p.UserId == dUserId && p.GuildId == dGuildId)
            .OrderBy(p => p.Name)
            .Include(p => p.Items)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<PlaylistOperationResult> RenamePlaylistAsync(ulong userId, ulong guildId, string oldName,
        string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName.Length > 100)
            return new PlaylistOperationResult(false, "New playlist name must be between 1 and 100 characters.");

        if (oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
            return new PlaylistOperationResult(false, "The new name is the same as the old name.");

        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var dUserId = (decimal)userId;
        var dGuildId = (decimal)guildId;

        var existingWithNewName = await context.Playlists
            .AnyAsync(p => p.UserId == dUserId && p.GuildId == dGuildId && p.Name == newName)
            .ConfigureAwait(false);

        if (existingWithNewName)
            return new PlaylistOperationResult(false, $"You already have a playlist named '{newName}'.");

        var playlist = await context.Playlists
            .FirstOrDefaultAsync(p => p.UserId == dUserId && p.GuildId == dGuildId && p.Name == oldName)
            .ConfigureAwait(false);

        if (playlist == null)
            return new PlaylistOperationResult(false, $"Could not find a playlist named '{oldName}'.");

        playlist.Name = newName;

        try
        {
            await context.SaveChangesAsync().ConfigureAwait(false);
            logger.LogInformation(
                "Renamed playlist '{OldName}' to '{NewName}' for User {UserId}, Guild {GuildId}", oldName, newName,
                userId, guildId);
            return new PlaylistOperationResult(true, $"Renamed playlist '{oldName}' to '{newName}'.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rename playlist '{OldName}' for User {UserId}, Guild {GuildId}", oldName,
                userId, guildId);
            return new PlaylistOperationResult(false, $"Failed to rename playlist '{oldName}'.");
        }
    }

    public async Task<PlaylistOperationResult> ShufflePlaylistAsync(ulong userId, ulong guildId, string playlistName)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var dUserId = (decimal)userId;
        var dGuildId = (decimal)guildId;

        var playlist = await context.Playlists
            .Include(p => p.Items)
            .ThenInclude(i => i.Track)
            .FirstOrDefaultAsync(p => p.UserId == dUserId && p.GuildId == dGuildId && p.Name == playlistName)
            .ConfigureAwait(false);

        if (playlist == null) return new PlaylistOperationResult(false, "Playlist not found.");
        if (playlist.Items.Count == 0)
            return new PlaylistOperationResult(false, "Playlist is empty, nothing to shuffle.");

        var items = playlist.Items.ToList();
        var random = new Random();
        var shuffledItems = items.OrderBy(_ => random.Next()).ToList();

        for (var i = 0; i < shuffledItems.Count; i++) shuffledItems[i].Position = i + 1;

        try
        {
            await context.SaveChangesAsync().ConfigureAwait(false);
            logger.LogInformation("Shuffled playlist '{PlaylistName}' for User {UserId}, Guild {GuildId}",
                playlistName, userId, guildId);
            return new PlaylistOperationResult(true, $"Shuffled playlist '{playlistName}'.", playlist);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to shuffle playlist '{PlaylistName}' for User {UserId}, Guild {GuildId}",
                playlistName, userId, guildId);
            return new PlaylistOperationResult(false, "Failed to shuffle playlist.");
        }
    }

    public async Task<PlaylistOperationResult> SharePlaylistAsync(ulong senderId, ulong guildId, string playlistName,
        ulong recipientId)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var dSenderId = (decimal)senderId;
        var dRecipientId = (decimal)recipientId;
        var dGuildId = (decimal)guildId;

        var originalPlaylist = await context.Playlists
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.UserId == dSenderId && p.GuildId == dGuildId && p.Name == playlistName)
            .ConfigureAwait(false);

        if (originalPlaylist == null) return new PlaylistOperationResult(false, "Original playlist not found.");

        await userService.EnsureUserExistsAsync(context, recipientId).ConfigureAwait(false);

        var recipientPlaylistCount = await context.Playlists
            .CountAsync(p => p.UserId == dRecipientId && p.GuildId == dGuildId).ConfigureAwait(false);

        if (recipientPlaylistCount >= MaxPlaylistsPerUser)
            return new PlaylistOperationResult(false,
                $"Recipient has reached the maximum limit of {MaxPlaylistsPerUser} playlists.");

        var exists = await context.Playlists
            .AnyAsync(p => p.UserId == dRecipientId && p.GuildId == dGuildId && p.Name == playlistName)
            .ConfigureAwait(false);

        if (exists)
            return new PlaylistOperationResult(false, $"Recipient already has a playlist named '{playlistName}'.");

        var sharedPlaylist = new PlaylistEntity
        {
            UserId = dRecipientId,
            GuildId = dGuildId,
            Name = originalPlaylist.Name,
            CreatedAt = DateTime.UtcNow
        };

        context.Playlists.Add(sharedPlaylist);
        await context.SaveChangesAsync().ConfigureAwait(false);

        foreach (var item in originalPlaylist.Items)
            context.PlaylistItems.Add(new PlaylistItemEntity
            {
                PlaylistId = sharedPlaylist.Id,
                TrackId = item.TrackId,
                Position = item.Position
            });

        try
        {
            await context.SaveChangesAsync().ConfigureAwait(false);
            logger.LogInformation(
                "Shared playlist '{PlaylistName}' from User {SenderId} to User {RecipientId} in Guild {GuildId}",
                playlistName, senderId, recipientId, guildId);
            return new PlaylistOperationResult(true, $"Playlist '{playlistName}' shared with <@{recipientId}>.",
                sharedPlaylist);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sharing playlist '{PlaylistName}' from {SenderId} to {RecipientId}",
                playlistName, senderId, recipientId);
            return new PlaylistOperationResult(false, "An error occurred while sharing the playlist.");
        }
    }
}