using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Assistant.Net.Modules;

public class UserInfoModule : InteractionModuleBase<SocketInteractionContext>
{
    public InteractionService Commands { get; set; }

    private readonly InteractionHandler _handler;

    public UserInfoModule(InteractionHandler handler)
    {
        _handler = handler;
    }

    private static Color GetTopRoleColor(SocketUser user)
    {
        if (user is not SocketGuildUser)
        {
            return Color.DarkMagenta;
        }
        var gUser = user as SocketGuildUser;
        if (gUser == null)
        {
            return Color.DarkMagenta;
        }
        var topRole = (gUser.Roles.OrderByDescending(x => x.Position).FirstOrDefault());
        if (topRole == null)
        {
            return Color.DarkMagenta;
        }
        return topRole.Color;
    }

    private static string GetAvailableClients(SocketUser user)
    {
        var clients = new List<string>();
        for (int i = 0; i < user.ActiveClients.Count; i++)
        {
            clients.Add(user.ActiveClients.ElementAt(i).ToString());
        }
        var status = user.Status.ToString();
        if (clients.Count > 0)
        {
            return status + " on " + string.Join(", ", clients);
        }
        return status;
    }

    private static string GetThumbnailUrl(SocketUser user)
    {
        string url = user.GetAvatarUrl();

        foreach (IActivity activity in user.Activities)
        {
            if (activity.Type == ActivityType.CustomStatus)
            {
                if (activity is not CustomStatusGame activityType)
                    continue;
                if (activityType.Emote is GuildEmote)
                {
                    if (activityType.Emote is not GuildEmote emote)
                        continue;
                    url = emote.Url;
                }
            }
            else if (activity.Type == ActivityType.Listening)
            {
                if (activity is not SpotifyGame activityType)
                    continue;
                url = activityType.AlbumArtUrl;
            }
            else if (activity.Type == ActivityType.Playing)
            {
                if (activity is not RichGame activityType)
                    continue;
                if (activityType.LargeAsset != null)
                {
                    url = activityType.LargeAsset.GetImageUrl();
                }
            }
        }

        return url;
    }

    private Embed getEmbed(SocketUser user)
    {

        var embed = new EmbedBuilder
        {
            Color = GetTopRoleColor(user),
            Author = new EmbedAuthorBuilder
            {
                Name = user.Username,
                IconUrl = user.GetAvatarUrl()
            },
            ThumbnailUrl = GetThumbnailUrl(user)
        };

        if (user is SocketGuildUser gUser)
        {
            var guild = gUser.Guild;
            bool isOwner = guild.OwnerId == gUser.Id && (gUser.JoinedAt - guild.CreatedAt) < TimeSpan.FromSeconds(5);
            if (gUser.JoinedAt is DateTimeOffset joinedAt)
            {
                embed.AddField(isOwner ? $"Created {guild.Name} on" : $"Joined {guild.Name} on",
                    new TimestampTag(joinedAt, TimestampTagStyles.LongDateTime).ToString() + "\n" + new TimestampTag(joinedAt, TimestampTagStyles.Relative).ToString()
                    , true);
            }

        }
        embed.AddField("Account created on",
                new TimestampTag(user.CreatedAt, TimestampTagStyles.LongDateTime).ToString() + "\n" + new TimestampTag(user.CreatedAt, TimestampTagStyles.Relative).ToString(),
                true);

        if (user is SocketGuildUser user1 && user1.Nickname != null)
        {
            embed.AddField("Nickname", user1.Nickname, true);
        }

        embed.AddField("Status", GetAvailableClients(user), true);

        foreach (IActivity activity in user.Activities)
        {
            if (activity.Type is ActivityType.CustomStatus)
            {
                embed.AddField("Custom Status", activity.Name, true);
            }
            else if (activity.Type is ActivityType.Listening)
            {
                SpotifyGame? activityType = activity as SpotifyGame;
                if (activityType == null)
                    continue;
                embed.AddField("Spotify",
                    $"Listening to [{activityType.TrackTitle}]({activityType.TrackUrl}) by {activityType.Artists.First()}",
                    true);
            }
            else
            {
                embed.AddField(activity.Type.ToString(), activity.Name, true);
            }
        }

        if (user is SocketGuildUser user2)
        {
            IEnumerable<string>? roles = user2.Roles.OrderByDescending(x => x.Position).Select(x => x.Mention);
            if (roles.Any())
                embed.AddField($"Roles [{roles.Count()}]", string.Join(", ", roles), true);
        }

        embed.Footer = new EmbedFooterBuilder
        {
            Text = $"ID: {user.Id}"
        };

        return embed.Build();
    }

    [SlashCommand("userinfo", "Get information about a user.")]
    public async Task UserInfoAsync([Summary(description: "The user to get information about")] SocketUser? user = null)
    {
        if (user == null)
        {
            user = Context.User;
        }
        await RespondAsync(embeds: [getEmbed(user)]);
    }

}