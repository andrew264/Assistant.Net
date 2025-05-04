using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;

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
        var url = user.GetAvatarUrl();

        return user.Activities.Aggregate(url, (current, activity) => activity switch
        {
            CustomStatusGame { Emote: GuildEmote emote } => emote.Url,
            SpotifyGame spotifyGames => spotifyGames.AlbumArtUrl,
            StreamingGame streamingGame => streamingGame.Url,
            RichGame { LargeAsset: not null } richGame => richGame.LargeAsset.GetImageUrl(),
            _ => current
        });
    }
}