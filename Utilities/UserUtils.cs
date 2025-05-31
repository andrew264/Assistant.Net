using Assistant.Net.Services.User;
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
    private static string GetAvailableClients(SocketUser user)
    {
        var clients = user.ActiveClients.Select(client => client.ToString()).ToList();
        var status = user.Status.ToString();

        return clients.Count > 0
            ? $"{status} on {string.Join(", ", clients)}"
            : status;
    }

    // Get user thumbnail url
    private static string GetUserThumbnailUrl(SocketUser user)
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

    public static async Task<Embed> GenerateUserInfoEmbedAsync(IUser targetUser, bool showSensitiveInfo,
        UserService userService, DiscordSocketClient client)
    {
        var embedBuilder = new EmbedBuilder();

        SocketGuildUser? guildUser = null;
        switch (targetUser)
        {
            case SocketGuildUser sgu:
                guildUser = sgu;
                embedBuilder.WithColor(GetTopRoleColor(guildUser));
                break;
            case not null when client.Guilds.Any(g => g.GetUser(targetUser.Id) != null):
            {
                var mutualGuild = client.Guilds.FirstOrDefault(g => g.GetUser(targetUser.Id) != null);
                if (mutualGuild != null)
                {
                    guildUser = mutualGuild.GetUser(targetUser.Id);
                    if (guildUser != null) embedBuilder.WithColor(GetTopRoleColor(guildUser));
                }

                break;
            }
        }

        if (embedBuilder.Color == null) embedBuilder.WithColor(Color.Default);


        if (targetUser != null)
        {
            var userModel = await userService.GetUserAsync(targetUser.Id).ConfigureAwait(false);
            var about = userModel?.About;
            var lastSeenTimestamp = userModel?.LastSeen;

            embedBuilder.Description = !string.IsNullOrWhiteSpace(about)
                ? $"{targetUser.Mention}: {about}"
                : targetUser.Mention;

            embedBuilder.WithAuthor(targetUser.GlobalName ?? targetUser.Username,
                targetUser.GetDisplayAvatarUrl() ?? targetUser.GetDefaultAvatarUrl());
            embedBuilder.WithThumbnailUrl(
                GetUserThumbnailUrl(targetUser as SocketUser ?? client.GetUser(targetUser.Id)));

            if (guildUser != null)
            {
                var joinedAt = guildUser.JoinedAt;
                if (joinedAt.HasValue)
                {
                    var isOwner = guildUser.Id == guildUser.Guild.OwnerId;
                    var joinFieldName =
                        isOwner ? $"Created {guildUser.Guild.Name} on" : $"Joined {guildUser.Guild.Name} on";
                    embedBuilder.AddField(joinFieldName,
                        $"{joinedAt.Value.GetLongDateTime()}\n{joinedAt.Value.GetRelativeTime()}", true);
                }

                embedBuilder.AddField("Account created on",
                    $"{targetUser.CreatedAt.GetLongDateTime()}\n{targetUser.CreatedAt.GetRelativeTime()}", true);

                if (!string.IsNullOrWhiteSpace(guildUser.Nickname))
                    embedBuilder.AddField("Nickname", guildUser.Nickname, true);

                if (guildUser.Status != UserStatus.Offline)
                    embedBuilder.AddField("Available Clients", GetAvailableClients(guildUser), true);

                if (showSensitiveInfo && lastSeenTimestamp.HasValue)
                {
                    var statusFieldName = guildUser.Status == UserStatus.Offline ? "Last Seen" : "Online for";
                    embedBuilder.AddField(statusFieldName,
                        TimestampTag.FromDateTime(lastSeenTimestamp.Value, TimestampTagStyles.Relative), true);
                }

                // Activities
                var activities = ActivityUtils.GetAllUserActivities(guildUser.Activities, true, true, true);
                foreach (var (actType, actName) in activities)
                    if (!string.IsNullOrWhiteSpace(actName))
                        embedBuilder.AddField(actType, actName.Truncate(1024), true);

                var roles = guildUser.Roles.Where(r => !r.IsEveryone).OrderByDescending(r => r.Position).ToList();
                if (roles.Count > 0)
                {
                    var roleString = string.Join(" ", roles.Select(r => r.Mention));
                    embedBuilder.AddField($"Roles [{roles.Count}]", roleString.Truncate(1024));
                }
            }
            else
            {
                embedBuilder.AddField("Account created on",
                    $"{targetUser.CreatedAt.GetLongDateTime()}\n{targetUser.CreatedAt.GetRelativeTime()}");
                if (targetUser is SocketUser socketTargetUser)
                {
                    if (socketTargetUser.Status != UserStatus.Offline)
                        embedBuilder.AddField("Status", socketTargetUser.Status.ToString(), true);
                    var activities = ActivityUtils.GetAllUserActivities(socketTargetUser.Activities, true, true, true);
                    foreach (var (actType, actName) in activities)
                        if (!string.IsNullOrWhiteSpace(actName))
                            embedBuilder.AddField(actType, actName.Truncate(1024), true);
                }
            }
        }

        if (targetUser != null) embedBuilder.WithFooter($"ID: {targetUser.Id}");
        embedBuilder.WithTimestamp(DateTimeOffset.UtcNow);

        return embedBuilder.Build();
    }
}