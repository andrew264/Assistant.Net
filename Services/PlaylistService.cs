using Assistant.Net.Models;
using Assistant.Net.Models.Playlist;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Assistant.Net.Services;

// --- Result Records for Playlist Operations ---
public record PlaylistOperationResult(bool Success, string Message, PlaylistModel? Playlist = null);

public record PlaylistCreationResult(bool Success, string Message, PlaylistModel? Playlist = null,
    bool LimitReached = false, bool NameExists = false);

public class PlaylistService
{
    private readonly IMongoCollection<PlaylistModel> _playlistCollection;
    private readonly ILogger<PlaylistService> _logger;
    private const int MaxPlaylistsPerUser = 10;
    private const int MaxSongsPerPlaylist = 200;

    public PlaylistService(IMongoDatabase database, ILogger<PlaylistService> logger)
    {
        _playlistCollection = database.GetCollection<PlaylistModel>("playlists");
        _logger = logger;
        EnsureIndexesAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureIndexesAsync()
    {
        // Unique index for (UserId, GuildId, Name) to ensure playlist names are unique per user per guild.
        var indexKeysDefinition = Builders<PlaylistModel>.IndexKeys
            .Ascending(p => p.Id.UserId)
            .Ascending(p => p.Id.GuildId)
            .Ascending(p => p.Name);
        var indexOptions = new CreateIndexOptions { Unique = true, Name = "UserGuildPlaylistNameUnique" };
        var indexModel = new CreateIndexModel<PlaylistModel>(indexKeysDefinition, indexOptions);

        try
        {
            await _playlistCollection.Indexes.CreateOneAsync(indexModel);
            _logger.LogInformation("Ensured unique index on playlists (UserId, GuildId, Name).");
        }
        catch (MongoCommandException ex)
            when (ex.CodeName == "IndexOptionsConflict" || ex.CodeName == "IndexAlreadyExists" ||
                  ex.Message.Contains("already exists with different options"))
        {
            _logger.LogWarning("Playlist unique index (UserId, GuildId, Name) already exists or conflicts. {Message}",
                ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating unique index for playlists (UserId, GuildId, Name).");
        }
    }

    public async Task<PlaylistCreationResult> CreatePlaylistAsync(ulong userId, ulong guildId, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return new PlaylistCreationResult(false, "Playlist name must be between 1 and 100 characters.",
                NameExists: true);
        }

        var filter = Builders<PlaylistModel>.Filter.And(
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.UserId, userId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.GuildId, guildId)
        );

        var userPlaylistCount = await _playlistCollection.CountDocumentsAsync(filter);
        if (userPlaylistCount >= MaxPlaylistsPerUser)
        {
            return new PlaylistCreationResult(false,
                $"You have reached the maximum limit of {MaxPlaylistsPerUser} playlists.", LimitReached: true);
        }

        var nameFilter = Builders<PlaylistModel>.Filter.And(
            filter,
            Builders<PlaylistModel>.Filter.Eq(p => p.Name, name)
        );
        var existingByName = await _playlistCollection.Find(nameFilter).FirstOrDefaultAsync();
        if (existingByName != null)
        {
            return new PlaylistCreationResult(false, "You already have a playlist with that name.", NameExists: true);
        }

        var newPlaylist = new PlaylistModel
        {
            Id = new PlaylistIdKey { UserId = userId, GuildId = guildId },
            Name = name,
            Songs = [],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            await _playlistCollection.InsertOneAsync(newPlaylist);
            _logger.LogInformation("Created playlist '{PlaylistName}' for User {UserId} in Guild {GuildId}", name,
                userId, guildId);
            return new PlaylistCreationResult(true, $"Playlist '{name}' created successfully!", newPlaylist);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogWarning(ex,
                "Duplicate key on playlist insert (likely unique index violation) for {UserId}/{GuildId} - {Name}",
                userId, guildId, name);
            return new PlaylistCreationResult(false,
                "A playlist with this name might have just been created or there was a conflict. Try a different name.",
                NameExists: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating playlist '{PlaylistName}' for User {UserId}, Guild {GuildId}", name,
                userId, guildId);
            return new PlaylistCreationResult(false, "An error occurred while creating the playlist.");
        }
    }

    public async Task<PlaylistModel?> GetPlaylistAsync(ulong userId, ulong guildId, string name)
    {
        var filter = Builders<PlaylistModel>.Filter.And(
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.UserId, userId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.GuildId, guildId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Name, name)
        );
        return await _playlistCollection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<bool> DeletePlaylistAsync(ulong userId, ulong guildId, string name)
    {
        var filter = Builders<PlaylistModel>.Filter.And(
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.UserId, userId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.GuildId, guildId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Name, name)
        );
        var result = await _playlistCollection.DeleteOneAsync(filter);
        if (result.IsAcknowledged && result.DeletedCount > 0)
        {
            _logger.LogInformation("Deleted playlist '{PlaylistName}' for User {UserId} in Guild {GuildId}", name,
                userId, guildId);
            return true;
        }

        _logger.LogWarning(
            "Attempted to delete non-existent playlist '{PlaylistName}' for User {UserId} in Guild {GuildId}", name,
            userId, guildId);
        return false;
    }

    public async Task<PlaylistOperationResult> AddTracksToPlaylistAsync(ulong userId, ulong guildId,
        string playlistName, List<SongModel> songsToAdd)
    {
        var playlist = await GetPlaylistAsync(userId, guildId, playlistName);
        if (playlist == null) return new PlaylistOperationResult(false, "Playlist not found.");

        var initialSongCount = playlist.Songs.Count;
        var spaceAvailable = MaxSongsPerPlaylist - initialSongCount;

        if (spaceAvailable <= 0)
        {
            return new PlaylistOperationResult(false, "Playlist is already full.");
        }

        var actualSongsToAdd = songsToAdd.Take(spaceAvailable).ToList();
        if (actualSongsToAdd.Count == 0)
        {
            return new PlaylistOperationResult(false,
                "No songs provided or playlist capacity would be exceeded (even with one song).");
        }

        var filter = Builders<PlaylistModel>.Filter.And(
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.UserId, userId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.GuildId, guildId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Name, playlistName)
        );
        var update = Builders<PlaylistModel>.Update
            .PushEach(p => p.Songs, actualSongsToAdd)
            .Set(p => p.UpdatedAt, DateTime.UtcNow);

        var result = await _playlistCollection.UpdateOneAsync(filter, update);
        if (result.IsAcknowledged && result.ModifiedCount > 0)
        {
            var message = $"Added {actualSongsToAdd.Count} song(s) to '{playlistName}'.";
            if (songsToAdd.Count > actualSongsToAdd.Count)
            {
                message += $" Could not add {songsToAdd.Count - actualSongsToAdd.Count} song(s) due to playlist capacity.";
            }

            _logger.LogInformation(
                "Added {AddedCount} songs to playlist '{PlaylistName}' for User {UserId}, Guild {GuildId}. Original request was for {RequestedCount}",
                actualSongsToAdd.Count, playlistName, userId, guildId, songsToAdd.Count);
            return new PlaylistOperationResult(true, message, playlist); // Playlist here is pre-update, consider re-fetching if needed
        }

        _logger.LogError(
            "Failed to add songs to playlist '{PlaylistName}' for User {UserId}, Guild {GuildId}. DB result: Ack={Ack}, Matched={Match}, Modified={Mod}",
            playlistName, userId, guildId, result.IsAcknowledged, result.MatchedCount, result.ModifiedCount);
        return new PlaylistOperationResult(false, "Failed to add songs to the playlist.");
    }

    public async Task<(bool Success, string Message, SongModel? RemovedSong)> RemoveSongFromPlaylistAsync(ulong userId,
        ulong guildId, string playlistName, int oneBasedIndex)
    {
        if (oneBasedIndex <= 0) return (false, "Invalid song index. Position must be 1 or greater.", null);

        var playlist = await GetPlaylistAsync(userId, guildId, playlistName);
        if (playlist == null) return (false, "Playlist not found.", null);
        if (oneBasedIndex > playlist.Songs.Count) return (false, "Invalid song index; it's out of bounds.", null);

        var songToRemove = playlist.Songs[oneBasedIndex - 1];
        playlist.Songs.RemoveAt(oneBasedIndex - 1); // Modify in memory

        var filter = Builders<PlaylistModel>.Filter.And(
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.UserId, userId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.GuildId, guildId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Name, playlistName)
        );
        var update = Builders<PlaylistModel>.Update
            .Set(p => p.Songs, playlist.Songs)
            .Set(p => p.UpdatedAt, DateTime.UtcNow);

        var result = await _playlistCollection.UpdateOneAsync(filter, update);
        if (result.IsAcknowledged && result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "Removed song '{SongTitle}' from playlist '{PlaylistName}' for User {UserId}, Guild {GuildId}",
                songToRemove.Title, playlistName, userId, guildId);
            return (true, $"Removed '{songToRemove.Title}' from playlist '{playlistName}'.", songToRemove);
        }

        _logger.LogError(
            "Failed to remove song from playlist '{PlaylistName}' (Index {Index}) for User {UserId}, Guild {GuildId}. DB result: Ack={Ack}, Matched={Match}, Modified={Mod}",
            playlistName, oneBasedIndex, userId, guildId, result.IsAcknowledged, result.MatchedCount,
            result.ModifiedCount);
        return (false, "Failed to remove song from the playlist.", null);
    }

    public async Task<List<PlaylistModel>> GetUserPlaylistsAsync(ulong userId, ulong guildId)
    {
        var filter = Builders<PlaylistModel>.Filter.And(
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.UserId, userId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.GuildId, guildId)
        );
        return await _playlistCollection.Find(filter).SortBy(p => p.Name).ToListAsync();
    }

    public async Task<PlaylistOperationResult> RenamePlaylistAsync(ulong userId, ulong guildId, string oldName,
        string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName.Length > 100)
        {
            return new PlaylistOperationResult(false, "New playlist name must be between 1 and 100 characters.");
        }

        if (oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
        {
            return new PlaylistOperationResult(false, "The new name is the same as the old name.");
        }

        // Check if new_name already exists for this user/guild
        var existingWithNewName = await GetPlaylistAsync(userId, guildId, newName);
        if (existingWithNewName != null)
        {
            return new PlaylistOperationResult(false, $"You already have a playlist named '{newName}'.");
        }

        var filter = Builders<PlaylistModel>.Filter.And(
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.UserId, userId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.GuildId, guildId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Name, oldName)
        );
        var update = Builders<PlaylistModel>.Update
            .Set(p => p.Name, newName)
            .Set(p => p.UpdatedAt, DateTime.UtcNow);

        var result = await _playlistCollection.UpdateOneAsync(filter, update);
        switch (result.IsAcknowledged)
        {
            case true when result.ModifiedCount > 0:
                _logger.LogInformation(
                    "Renamed playlist '{OldName}' to '{NewName}' for User {UserId}, Guild {GuildId}", oldName, newName,
                    userId, guildId);
                return new PlaylistOperationResult(true, $"Renamed playlist '{oldName}' to '{newName}'.");
            case true when result.MatchedCount == 0:
                return new PlaylistOperationResult(false, $"Could not find a playlist named '{oldName}'.");
            default:
                _logger.LogError(
                    "Failed to rename playlist '{OldName}' to '{NewName}' for User {UserId}, Guild {GuildId}. DB result: Ack={Ack}, Matched={Match}, Modified={Mod}",
                    oldName, newName, userId, guildId, result.IsAcknowledged, result.MatchedCount, result.ModifiedCount);
                return new PlaylistOperationResult(false, $"Failed to rename playlist '{oldName}'.");
        }
    }

    public async Task<PlaylistOperationResult> ShufflePlaylistAsync(ulong userId, ulong guildId, string playlistName)
    {
        var playlist = await GetPlaylistAsync(userId, guildId, playlistName);
        if (playlist == null) return new PlaylistOperationResult(false, "Playlist not found.");
        if (playlist.Songs.Count == 0) return new PlaylistOperationResult(false, "Playlist is empty, nothing to shuffle.");

        var random = new Random();
        playlist.Songs = playlist.Songs.OrderBy(_ => random.Next()).ToList();

        var filter = Builders<PlaylistModel>.Filter.And(
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.UserId, userId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.GuildId, guildId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Name, playlistName)
        );
        var update = Builders<PlaylistModel>.Update
            .Set(p => p.Songs, playlist.Songs)
            .Set(p => p.UpdatedAt, DateTime.UtcNow);

        var result = await _playlistCollection.UpdateOneAsync(filter, update);
        if (result.IsAcknowledged && result.ModifiedCount > 0)
        {
            _logger.LogInformation("Shuffled playlist '{PlaylistName}' for User {UserId}, Guild {GuildId}",
                playlistName, userId, guildId);
            return new PlaylistOperationResult(true, $"Shuffled playlist '{playlistName}'.", playlist);
        }

        _logger.LogError(
            "Failed to shuffle playlist '{PlaylistName}' for User {UserId}, Guild {GuildId}. DB result: Ack={Ack}, Matched={Match}, Modified={Mod}",
            playlistName, userId, guildId, result.IsAcknowledged, result.MatchedCount, result.ModifiedCount);
        return new PlaylistOperationResult(false, "Failed to shuffle playlist.");
    }

    public async Task<PlaylistOperationResult> SharePlaylistAsync(ulong senderId, ulong guildId, string playlistName,
        ulong recipientId)
    {
        var originalPlaylist = await GetPlaylistAsync(senderId, guildId, playlistName);
        if (originalPlaylist == null) return new PlaylistOperationResult(false, "Original playlist not found.");

        var recipientFilter = Builders<PlaylistModel>.Filter.And(
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.UserId, recipientId),
            Builders<PlaylistModel>.Filter.Eq(p => p.Id.GuildId, guildId)
        );
        var recipientPlaylistCount = await _playlistCollection.CountDocumentsAsync(recipientFilter);

        if (recipientPlaylistCount >= MaxPlaylistsPerUser)
        {
            return new PlaylistOperationResult(false,
                $"Recipient has reached the maximum limit of {MaxPlaylistsPerUser} playlists.");
        }

        var recipientNameFilter = Builders<PlaylistModel>.Filter.And(
            recipientFilter,
            Builders<PlaylistModel>.Filter.Eq(p => p.Name, playlistName)
        );
        var existingRecipientPlaylist = await _playlistCollection.Find(recipientNameFilter).FirstOrDefaultAsync();
        if (existingRecipientPlaylist != null)
        {
            return new PlaylistOperationResult(false,
                $"Recipient already has a playlist named '{playlistName}'.");
        }

        var sharedPlaylist = new PlaylistModel
        {
            Id = new PlaylistIdKey { UserId = recipientId, GuildId = guildId },
            Name = originalPlaylist.Name,
            Songs = new List<SongModel>(originalPlaylist.Songs.Select(s => new SongModel // Deep copy songs
            {
                Title = s.Title,
                Artist = s.Artist,
                Uri = s.Uri,
                Duration = s.Duration,
                Thumbnail = s.Thumbnail,
                Source = s.Source
            })),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            await _playlistCollection.InsertOneAsync(sharedPlaylist);
            _logger.LogInformation(
                "Shared playlist '{PlaylistName}' from User {SenderId} to User {RecipientId} in Guild {GuildId}",
                playlistName, senderId, recipientId, guildId);
            return new PlaylistOperationResult(true,
                $"Playlist '{playlistName}' shared with <@{recipientId}>.", sharedPlaylist);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogWarning(ex,
                "Duplicate key on shared playlist insert (likely unique index violation) for {RecipientId}/{GuildId} - {PlaylistName}",
                recipientId, guildId, playlistName);
            return new PlaylistOperationResult(false, "A playlist with this name might have just been created for the recipient or there was a conflict.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sharing playlist '{PlaylistName}' from {SenderId} to {RecipientId}",
                playlistName, senderId, recipientId);
            return new PlaylistOperationResult(false, "An error occurred while sharing the playlist.");
        }
    }
}