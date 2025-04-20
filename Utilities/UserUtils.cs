using Discord;
using Discord.WebSocket;
using Assistant.Net.Utilities;

namespace Assistant.Net.Utilities;

public static class UserUtils
{
    // Get the top role color of a user
    public static Color GetTopRoleColor(SocketUser user)
    {
        if (user is not SocketGuildUser guildUser)
            return Color.Default;

        var topRole = guildUser.Roles
            .Where(role => role.IsEveryone == false)
            .OrderByDescending(role => role.Position)
            .FirstOrDefault();

        return topRole?.Color ?? Color.Default;
    }

    // Get available clients
    public static string GetAvailableClients(SocketUser user)
    {
        var clients = user.ActiveClients.Select(client => client.ToString()).ToList();
        var status = user.Status.ToString();

        return clients.Count > 0
            ? $"{status} on {string.Join(", ", clients)}"
            : status;
    }

    // Get user activities
    public static string GetUserActivities(SocketUser user)
    {
        var activities = user.Activities.Select(activity => activity.ToString()).ToList();
        return activities.Count > 0
            ? string.Join(", ", activities)
            : "No activities";
    }

    // Get user thumbnail url
    public static string GetUserThumbnailUrl(SocketUser user)
    {
        string url = user.GetAvatarUrl(format: ImageFormat.Auto, size: 128);

        foreach (IActivity activity in user.Activities)
        {
            if (activity is CustomStatusGame customStatusGame && customStatusGame.Emote is GuildEmote emote)
                url = emote.Url;
            else if (activity is SpotifyGame spotifyGames)
                url = spotifyGames.AlbumArtUrl;
            else if (activity is StreamingGame streamingGame)
                url = streamingGame.Url;
            else if (activity is RichGame richGame && richGame.LargeAsset != null)
                url = richGame.LargeAsset.GetImageUrl();
        }

        return url;
    }

    // Get user details and return as an embed
    public static Task<Embed> GetUserEmbed(SocketUser user)
    {
        var embed = new EmbedBuilder
        {
            Color = GetTopRoleColor(user),
            Author = new EmbedAuthorBuilder
            {
                Name = user.Username,
                IconUrl = user.GetAvatarUrl(format: ImageFormat.Auto, size: 128)
            },
            ThumbnailUrl = GetUserThumbnailUrl(user),
            // Description = To-Do
        };

        if (user is SocketGuildUser guildUser)
        {
            var guild = guildUser.Guild;
            bool isOwner = guildUser.Guild.OwnerId == guildUser.Id;

            if (guildUser.JoinedAt is DateTimeOffset joinedAt)
                embed.AddField(isOwner ? $"Created {guild.Name} on" : $"Joined {guild.Name} on", TimeUtils.GetLongDateTime(joinedAt), true);

            if (guildUser.Nickname != null)
                embed.AddField("Nickname", guildUser.Nickname, true);

            var roles = guildUser.Roles
                .OrderByDescending(x => x.Position)
                .Take(guildUser.Roles.Count - 1)
                .Select(x => x.Mention)
                .ToList();

            if (roles.Count > 0)
                embed.AddField($"Roles [{roles.Count}]", string.Join(", ", roles), true);
        }

        embed.AddField("Account created on", TimeUtils.GetLongDateTime(user.CreatedAt), true);
        embed.AddField("Status", GetAvailableClients(user), true);

        foreach (var activity in user.Activities)
        {
            switch (activity.Type)
            {
                case ActivityType.CustomStatus:
                    embed.AddField("Custom Status", activity.Name, true);
                    break;
                case ActivityType.Listening when activity is SpotifyGame spotify:
                    embed.AddField(
                        "Spotify",
                        $"Listening to [{spotify.TrackTitle}]({spotify.TrackUrl}) by {spotify.Artists.First()}",
                        true
                    );
                    break;
                case ActivityType.Streaming when activity is StreamingGame streaming:
                    embed.AddField(
                        "Streaming",
                        $"[{streaming.Name}]({streaming.Url})",
                        true
                    );
                    break;
            }
        }

        return Task.FromResult(embed.Build());
    }
}
