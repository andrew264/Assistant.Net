using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;

namespace Assistant.Net.Utilities;

public static partial class BracketPattern
{
    [GeneratedRegex(@"[(\[].*?[)\]]")]
    public static partial Regex Get();
}

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

    public static Dictionary<string, string> GetAllUserActivities(SocketUser user, bool withTime, bool withUrl,
        bool includeAllActivities) // TODO: finish this at a later point in time
    {
        var activities = new Dictionary<string, string>();
        if (user is not SocketGuildUser guildUser)
            return activities;
        foreach (var activity in guildUser.Activities)
            if (activity is SpotifyGame spotifyGame)
            {
                if (withUrl)
                    activities["Spotify"] =
                        $"Listening to [{BracketPattern.Get().Replace(spotifyGame.TrackTitle, "")}]({spotifyGame.TrackUrl}) by {string.Join(", ", spotifyGame.Artists)}";
                else
                    activities["Spotify"] =
                        $"Listening to {BracketPattern.Get().Replace(spotifyGame.TrackTitle, "")} by {string.Join(", ", spotifyGame.Artists)}";
            }
            else if (activity is StreamingGame streamingGame)
            {
                if (withUrl)
                    activities["Streaming"] = $"[{streamingGame.Name}]({streamingGame.Url})";
                else
                    activities["Streaming"] = $"{streamingGame.Name}";
            }
            else if (activity is CustomStatusGame customStatusGame)
            {
                activities["Custom Status"] = customStatusGame.Name;
            }
            else if (activity is RichGame richGame)
            {
                if (withTime && richGame.Timestamps.Start != null)
                    activities["Playing"] =
                        $"{richGame.Name}\n**{TimeUtils.GetRelativeTime(richGame.Timestamps.Start.Value)}**";
                else activities["Playing"] = $"{richGame.Name}";
            }
            else
            {
                activities[activity.Type.ToString()] = $"{activity.Name}";
            }

        return activities;
    }
}