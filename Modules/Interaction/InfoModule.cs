using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Assistant.Net.Modules.Interaction;

public class InfoModule : InteractionModuleBase<SocketInteractionContext>
{
    public required HttpClient _httpClient { get; set; }

    private static Color GetTopRoleColor(SocketUser user)
    {
        if (user is not SocketGuildUser guildUser)
            return Color.DarkMagenta;

        var Roles = guildUser.Roles.OrderByDescending(x => x.Position);
        var color = Color.DarkMagenta;
        foreach (var role in Roles)
        {
            if (role.Color.RawValue != 0)
            {
                color = role.Color;
                break;
            }
        }
        return color;
    }

    private static string GetFormatedTime(DateTimeOffset time)
    {
        return $"{new TimestampTag(time, TimestampTagStyles.LongDateTime)}\n{new TimestampTag(time, TimestampTagStyles.Relative)}";
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

    private static Embed GetUserEmbed(SocketUser user)
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
            bool isOwner = guild.OwnerId == guildUser.Id && guildUser.JoinedAt - guild.CreatedAt < TimeSpan.FromSeconds(5);

            if (guildUser.JoinedAt is DateTimeOffset joinedAt)
                embed.AddField(isOwner ? $"Created {guild.Name} on" : $"Joined {guild.Name} on", GetFormatedTime(joinedAt), true);
        }

        embed.AddField("Account created on", GetFormatedTime(user.CreatedAt), true);

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
            roles = roles.Take(roles.Count() - 1);
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
        var viewAvatarComponent = new ComponentBuilder().WithButton("View Avatar", style: ButtonStyle.Link, url: user.GetAvatarUrl(ImageFormat.Auto, size: 2048));
        await RespondAsync(embeds: [GetUserEmbed(user)], components: viewAvatarComponent.Build());
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
        await FollowupAsync(embeds: [GetUserEmbed(socketUser)]);
    }

    [RequireContext(ContextType.Guild)]
    [SlashCommand("serverinfo", "Get information about the server.")]
    public async Task ServerInfoAsync()
    {
        var guild = Context.Guild;
        var embed = new EmbedBuilder
        {
            Color = Color.DarkBlue,
            Author = new EmbedAuthorBuilder
            {
                Name = guild.Name,
                IconUrl = guild.IconUrl ?? ""
            },
            Description = guild.Description ?? "No description provided.",
            ThumbnailUrl = guild.BannerUrl ?? guild.IconUrl ?? ""
        };

        embed.AddField("Owner", guild.Owner.Mention, true);
        embed.AddField("Created on", GetFormatedTime(guild.CreatedAt), true);

        embed.AddField("Members", guild.MemberCount, true);
        embed.AddField("Online", guild.Users.Count(x => x.Status != UserStatus.Offline), true);
        embed.AddField("Bots", guild.Users.Count(x => x.IsBot), true);

        embed.AddField("Text Channels", guild.Channels.Count(x => x is ITextChannel), true);
        embed.AddField("Voice Channels", guild.Channels.Count(x => x is IVoiceChannel), true);
        if (guild.CategoryChannels.Count > 0)
            embed.AddField("Categories", guild.CategoryChannels.Count, true);

        embed.AddField("Roles", guild.Roles.Count - 1, true);
        embed.AddField("Emojis", guild.Emotes.Count, true);
        embed.AddField("Boosts", guild.PremiumSubscriptionCount, true);
        if (guild.PremiumSubscriptionCount > 0)
            embed.AddField("Boost Level", guild.PremiumTier, true);
        embed.AddField("Admins", guild.Users.Count(x => x.GuildPermissions.Administrator), true);

        embed.Footer = new EmbedFooterBuilder
        {
            Text = $"ID: {guild.Id}"
        };

        await RespondAsync(embeds: [embed.Build()]);
    }

    [SlashCommand("botinfo", "Get information about the bot.")]
    public async Task BotInfoAsync()
    {
        var application = await Context.Client.GetApplicationInfoAsync();
        var embed = new EmbedBuilder
        {
            Color = GetTopRoleColor(Context.Client.CurrentUser),
            Author = new EmbedAuthorBuilder
            {
                Name = application.Name,
                IconUrl = application.IconUrl
            },
            Description = application.Description ?? "No description provided.",
            ThumbnailUrl = Context.Client.CurrentUser.GetAvatarUrl() ?? application.IconUrl
        };

        embed.AddField("Created on", GetFormatedTime(application.CreatedAt), true);
        embed.AddField("Uptime", GetFormatedTime(Process.GetCurrentProcess().StartTime), true);
        embed.AddField("Owner", application.Owner.Mention, true);

        embed.AddField("Guilds", Context.Client.Guilds.Count, true);
        embed.AddField("Users", Context.Client.Guilds.Sum(x => x.MemberCount), true);
        embed.AddField("Channels", Context.Client.Guilds.Sum(x => x.Channels.Count), true);

        embed.AddField("Latency", $"{Context.Client.Latency}ms", true);
        embed.AddField("Library Version", DiscordConfig.Version, true);
        embed.AddField("Runtime", RuntimeInformation.FrameworkDescription, true);
        embed.AddField("Memory", $"{GC.GetTotalMemory(forceFullCollection: false) / 1024 / 1024:N0} MB", true);


        embed.Footer = new EmbedFooterBuilder
        {
            Text = $"ID: {application.Id}"
        };

        await RespondAsync(embeds: [embed.Build()]);
    }

    [SlashCommand("avatar", "Get the avatar of a user.")]
    public async Task AvatarAsync([Summary(description: "The user to get the avatar of")] SocketUser? user = null)
    {
        await DeferAsync();
        user ??= Context.User;
        var component = new ComponentBuilder().WithButton("Download", style: ButtonStyle.Link, url: user.GetAvatarUrl(size: 2048));
        using var avatarStream = await _httpClient.GetStreamAsync(user.GetAvatarUrl(ImageFormat.Auto, size: 2048));
        var fileFormat = user.AvatarId.StartsWith("a_") ? "gif" : "png";
        await ModifyOriginalResponseAsync(x =>
        {
            x.Content = $"# {user.GlobalName ?? user.Username}'s Avatar";
            x.Attachments = new[] { new FileAttachment(stream: avatarStream, $"{user.Id}.{fileFormat}") };
            x.Components = component.Build();
        });
    }

    [UserCommand("Show me your face!")]
    public async Task AvatarAsync(IUser user)
    {
        await RespondAsync(text: $"# [{user.GlobalName ?? user.Username}'s Avatar]({user.GetAvatarUrl(size: 2048)})", ephemeral: true);
    }
}