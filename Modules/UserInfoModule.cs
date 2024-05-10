using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Assistant.Net.Modules;

public class UserInfoModule : InteractionModuleBase<SocketInteractionContext>
{
    public required InteractionService Commands { get; set; }

    private static Color GetTopRoleColor(SocketUser user)
    {
        if (user is not SocketGuildUser guildUser)
            return Color.DarkMagenta;

        var topRole = guildUser.Roles.OrderByDescending(x => x.Position).FirstOrDefault();
        return topRole?.Color ?? Color.DarkMagenta;
    }

    private static string GetAvailableClients(SocketUser user)
    {
        var clients = user.ActiveClients.Select(client => client.ToString()).ToList();
        var status = user.Status.ToString();

        return clients.Count > 0
            ? $"{status} on {string.Join(", ", clients)}"
            : status;
    }

    private static string GetThumbnailUrl(SocketUser user)
    {
        string url = user.GetAvatarUrl();

        foreach (IActivity activity in user.Activities)
        {
            if (activity is CustomStatusGame customStatusGame && customStatusGame.Emote is GuildEmote emote)
                url = emote.Url;
            else if (activity is SpotifyGame spotifyGame)
                url = spotifyGame.AlbumArtUrl;
            else if (activity is RichGame richGame && richGame.LargeAsset != null)
                url = richGame.LargeAsset.GetImageUrl();
        }

        return url;
    }

    private static Embed GetEmbed(SocketUser user)
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

        if (user is SocketGuildUser guildUser)
        {
            var guild = guildUser.Guild;
            bool isOwner = guild.OwnerId == guildUser.Id && (guildUser.JoinedAt - guild.CreatedAt) < TimeSpan.FromSeconds(5);

            if (guildUser.JoinedAt is DateTimeOffset joinedAt)
            {
                embed.AddField(isOwner ? $"Created {guild.Name} on" : $"Joined {guild.Name} on",
                    $"{new TimestampTag(joinedAt, TimestampTagStyles.LongDateTime)}\n{new TimestampTag(joinedAt, TimestampTagStyles.Relative)}",
                    true);
            }
        }

        embed.AddField("Account created on",
            $"{new TimestampTag(user.CreatedAt, TimestampTagStyles.LongDateTime)}\n{new TimestampTag(user.CreatedAt, TimestampTagStyles.Relative)}",
            true);

        if (user is SocketGuildUser guildUser1 && guildUser1.Nickname != null)
            embed.AddField("Nickname", guildUser1.Nickname, true);

        embed.AddField("Status", GetAvailableClients(user), true);

        foreach (IActivity activity in user.Activities)
        {
            switch (activity.Type)
            {
                case ActivityType.CustomStatus:
                    embed.AddField("Custom Status", activity.Name, true);
                    break;
                case ActivityType.Listening:
                    if (activity is SpotifyGame spotifyGame)
                        embed.AddField("Spotify",
                            $"Listening to [{spotifyGame.TrackTitle}]({spotifyGame.TrackUrl}) by {spotifyGame.Artists.First()}",
                            true);
                    break;
                default:
                    embed.AddField(activity.Type.ToString(), activity.Name, true);
                    break;
            }
        }

        if (user is SocketGuildUser guildUser2)
        {
            var roles = guildUser2.Roles.OrderByDescending(x => x.Position).Select(x => x.Mention);
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
        user ??= Context.User;
        await RespondAsync(embeds: [GetEmbed(user)]);
    }

    [UserCommand("Who is this cute fella?")]
    public async Task UserInfoAsync(IUser user)
    {
        if (user is not SocketUser socketUser)
        {
            await RespondAsync("I can't get information about this user.", ephemeral: true);
            return;
        }
        await DeferAsync(ephemeral: true);
        await FollowupAsync(embeds: [GetEmbed(socketUser)]);
    }

}