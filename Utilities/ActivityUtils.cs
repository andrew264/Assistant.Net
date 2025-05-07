using Discord;
using Discord.WebSocket;

namespace Assistant.Net.Utilities;

public static class ActivityUtils
{
    public static HashSet<string> GetClients(SocketPresence presence)
    {
        var clients = new HashSet<string>();
        if (presence.Status == UserStatus.Offline) return clients;
        if (presence.ActiveClients.Contains(ClientType.Desktop)) clients.Add("Desktop");
        if (presence.ActiveClients.Contains(ClientType.Mobile)) clients.Add("Mobile");
        if (presence.ActiveClients.Contains(ClientType.Web)) clients.Add("Web");

        return clients;
    }

    public static string? SummarizeStatusChange(HashSet<string> beforeClients, string beforeStatus,
        HashSet<string> afterClients,
        string afterStatus)
    {
        var clientsChanged = beforeClients.Equals(afterClients);
        var statusChanged = beforeStatus.Equals(afterStatus);
        var hasBeforeClients = beforeClients.Count != 0;
        var hasAfterClients = afterClients.Count != 0;
        if (!hasBeforeClients && hasAfterClients)
            return afterStatus switch
            {
                "donotdisturb" => $"Now in Do Not Disturb on {FormatClients(afterClients)}",
                "idle" => $"Now idling on {FormatClients(afterClients)}",
                _ => $"Online on {FormatClients(afterClients)}"
            };

        if (!hasAfterClients)
            return hasBeforeClients ? $"Went offline from {string.Join(", ", beforeClients)}." : "Signed off.";

        switch (beforeStatus)
        {
            case "donotdisturb" when afterStatus is "online" or "idle":
                return clientsChanged
                    ? "Disabled Do Not Disturb."
                    : $"Disabled Do Not Disturb, now active on {FormatClients(afterClients)}";
            case "online" or "idle" when afterStatus.Equals("donotdisturb"):
            {
                return clientsChanged
                    ? "Enabled Do Not Disturb."
                    : $"Enabled Do Not Disturb, only active on {FormatClients(afterClients)}";
            }
        }

        if (statusChanged)
        {
            if (beforeClients.SetEquals(afterClients)) return null;
            switch (beforeStatus)
            {
                case "idle":
                    return $"Idling in {FormatClients(afterClients)}";
                case "donotdisturb":
                    return $"Do Not Disturb on {FormatClients(afterClients)}";
                case "online":
                    return $"Online on {FormatClients(afterClients)}";
            }
        }
        else
        {
            return beforeStatus switch
            {
                "online" when afterStatus.Equals("idle") => !clientsChanged
                    ? $"Idling on {FormatClients(afterClients)}"
                    : "Is now Idling",
                "idle" when afterStatus.Equals("online") => clientsChanged
                    ? "No longer idling"
                    : $"Now online on {FormatClients(afterClients)}",
                _ => clientsChanged
                    ? $"Changed status from {beforeStatus} to {afterStatus}"
                    : $"Changed status to {afterStatus}, active on {FormatClients(afterClients)}"
            };
        }

        // Default fallback (shouldn't happen)
        return
            $"Status changed from {beforeStatus} on [{FormatClients(beforeClients)}] to {afterStatus} on [{FormatClients(afterClients)}]";

        string FormatClients(HashSet<string> clients) => string.Join(", ", clients);
    }

    public static string FormatCustomActivity(CustomStatusGame activity, bool withTime, bool withUrl)
    {
        var value = "";
        if (activity.Emote != null)
            if (withUrl && activity.Emote is GuildEmote guildEmote)
                value += $"[{guildEmote.Name}]({guildEmote.Url})";
            else
                value += activity.Emote.Name;

        if (activity.State != null) value += activity.State;
        if (withTime) value += $"\n**{TimeUtils.GetRelativeTime(activity.CreatedAt)}**";
        return value;
    }

    public static Dictionary<string, string> GetAllUserActivities(IReadOnlyCollection<IActivity> activities,
        bool withTime, bool withUrl,
        bool includeAllActivities)
    {
        var resultDictionary = new Dictionary<string, string>();
        foreach (var activity in activities)
            switch (activity)
            {
                case SpotifyGame spotifyGame when withUrl:
                    resultDictionary["Spotify"] =
                        $"Listening to [{RegexPatterns.Bracket().Replace(spotifyGame.TrackTitle, "")}]({spotifyGame.TrackUrl}) by {string.Join(", ", spotifyGame.Artists)}";
                    break;
                case SpotifyGame spotifyGame:
                    resultDictionary["Spotify"] =
                        $"Listening to {RegexPatterns.Bracket().Replace(spotifyGame.TrackTitle, "")} by {string.Join(", ", spotifyGame.Artists)}";
                    break;
                case StreamingGame streamingGame when withUrl:
                    resultDictionary["Streaming"] = $"[{streamingGame.Name}]({streamingGame.Url})";
                    break;
                case StreamingGame streamingGame:
                    resultDictionary["Streaming"] = $"{streamingGame.Name}";
                    break;
                case CustomStatusGame customStatusGame:
                    resultDictionary["Custom Status"] = FormatCustomActivity(customStatusGame, withTime, withUrl);
                    break;
                case RichGame richGame when withTime && richGame.Timestamps.Start != null:
                    resultDictionary["Playing"] =
                        $"{richGame.Name}\n**{TimeUtils.GetRelativeTime(richGame.Timestamps.Start.Value)}**";
                    break;
                case RichGame richGame:
                    resultDictionary["Playing"] = $"{richGame.Name}";
                    break;
                default:
                    if (includeAllActivities)
                        resultDictionary[activity.Type.ToString()] = $"{activity.Name ?? "(Unnamed Activity)"}";
                    break;
            }

        return resultDictionary;
    }
}