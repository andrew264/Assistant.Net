using System.Text;
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

    public static async Task<MessageComponent> GenerateUserInfoV2Async(IUser targetUser, bool showSensitiveInfo,
        UserService userService, DiscordSocketClient client)
    {
        var componentBuilder = new ComponentBuilderV2();
        var mainContainer = new ContainerBuilder();

        SocketGuildUser? guildUser = null;
        if (targetUser is SocketGuildUser sgu)
        {
            guildUser = sgu;
            mainContainer.WithAccentColor(GetTopRoleColor(guildUser));
        }
        else
        {
            var mutualGuild = client.Guilds.FirstOrDefault(g => g.GetUser(targetUser.Id) != null);
            if (mutualGuild != null)
            {
                guildUser = mutualGuild.GetUser(targetUser.Id);
                if (guildUser != null)
                    mainContainer.WithAccentColor(GetTopRoleColor(guildUser));
            }
        }

        if (mainContainer.AccentColor == null)
            mainContainer.WithAccentColor(Color.Default);

        var userModel = await userService.GetUserAsync(targetUser.Id).ConfigureAwait(false);

        // --- Header Section ---
        var headerSection = new SectionBuilder();
        var displayName = guildUser?.DisplayName ?? targetUser.GlobalName ?? targetUser.Username;
        headerSection.AddComponent(new TextDisplayBuilder($"# {displayName}"));
        headerSection.AddComponent(new TextDisplayBuilder($"@{targetUser.Username} | {targetUser.Mention}"));

        if (!string.IsNullOrWhiteSpace(userModel?.About))
            headerSection.AddComponent(new TextDisplayBuilder(userModel.About));

        headerSection.WithAccessory(new ThumbnailBuilder
        {
            Media = new UnfurledMediaItemProperties
                { Url = targetUser.GetDisplayAvatarUrl() ?? targetUser.GetDefaultAvatarUrl() }
        });
        mainContainer.AddComponent(headerSection);

        // --- Timestamps Section ---
        mainContainer.AddComponent(new SeparatorBuilder());
        var timestampsContent = new StringBuilder();
        timestampsContent.AppendLine(
            $"**Account Created:** {TimestampTag.FromDateTimeOffset(targetUser.CreatedAt, TimestampTagStyles.Relative)}");

        if (guildUser?.JoinedAt is { } joinedAt)
            timestampsContent.AppendLine(
                $"**Joined Server:** {TimestampTag.FromDateTimeOffset(joinedAt, TimestampTagStyles.Relative)}");

        if (showSensitiveInfo && userModel?.LastSeen is { } lastSeen)
        {
            var statusFieldName = guildUser?.Status == UserStatus.Offline ? "Last Seen:" : "Online for:";
            timestampsContent.AppendLine(
                $"**{statusFieldName}** {TimestampTag.FromDateTime(lastSeen, TimestampTagStyles.Relative)}");
        }

        mainContainer.AddComponent(new TextDisplayBuilder(timestampsContent.ToString()));

        // --- Roles Section ---
        var roles = guildUser?.Roles.Where(r => !r.IsEveryone).OrderByDescending(r => r.Position).ToList();
        if (roles is { Count: > 0 })
        {
            mainContainer.AddComponent(new SeparatorBuilder());
            mainContainer.AddComponent(new TextDisplayBuilder($"## Roles ({roles.Count})"));
            var roleString = string.Join(" ", roles.Select(r => r.Mention));
            mainContainer.AddComponent(new TextDisplayBuilder(roleString.Truncate(4000)));
        }

        // --- Activities Section ---
        var activities = guildUser?.Activities ?? targetUser.Activities;
        if (activities is { Count: > 0 })
        {
            mainContainer.AddComponent(new SeparatorBuilder());
            mainContainer.AddComponent(new TextDisplayBuilder("## Activities"));

            foreach (var activity in activities)
            {
                var activitySection = new SectionBuilder();
                switch (activity)
                {
                    case SpotifyGame spotify:
                        activitySection.AddComponent(new TextDisplayBuilder(
                            $"**Listening to Spotify**\n{spotify.TrackTitle.AsMarkdownLink(spotify.TrackUrl)}\nby {string.Join(", ", spotify.Artists)}"));
                        activitySection.WithAccessory(new ThumbnailBuilder
                            { Media = new UnfurledMediaItemProperties { Url = spotify.AlbumArtUrl } });
                        mainContainer.AddComponent(activitySection);
                        break;
                    case RichGame richGame:
                        var richGameText = new StringBuilder($"**Playing {richGame.Name}**");
                        if (!string.IsNullOrWhiteSpace(richGame.Details))
                            richGameText.AppendLine($"\n{richGame.Details}");
                        if (!string.IsNullOrWhiteSpace(richGame.State))
                            richGameText.AppendLine($"\n{richGame.State}");
                        if (richGame.Timestamps?.Start is { } startTime)
                            richGameText.AppendLine(
                                $"\nElapsed: {TimestampTag.FromDateTimeOffset(startTime, TimestampTagStyles.Relative)}");
                        activitySection.AddComponent(new TextDisplayBuilder(richGameText.ToString()));
                        if (richGame.LargeAsset?.GetImageUrl() is { } largeAssetUrl)
                            activitySection.WithAccessory(new ThumbnailBuilder
                                { Media = new UnfurledMediaItemProperties { Url = largeAssetUrl } });
                        mainContainer.AddComponent(activitySection);
                        break;
                    case StreamingGame streamingGame:
                        activitySection.AddComponent(
                            new TextDisplayBuilder($"**Streaming on {streamingGame.Details}**\n{streamingGame.Name}"));
                        activitySection.WithAccessory(new ButtonBuilder("Watch Stream", style: ButtonStyle.Link,
                            url: streamingGame.Url));
                        mainContainer.AddComponent(activitySection);
                        break;
                    case CustomStatusGame custom:
                        var customStatusContent = new StringBuilder();
                        if (custom.Emote is Emote customEmote)
                            customStatusContent.Append($"{customEmote} ");
                        if (!string.IsNullOrWhiteSpace(custom.State))
                            customStatusContent.Append(custom.State);

                        if (customStatusContent.Length > 0)
                            mainContainer.AddComponent(
                                new TextDisplayBuilder($"**Custom Status:** {customStatusContent}"));
                        break;
                }
            }
        }

        // --- Footer Links ---
        var linksRow = new ActionRowBuilder();
        var hasLinks = false;

        var avatarUrl = targetUser.GetDisplayAvatarUrl(size: 2048) ?? targetUser.GetDefaultAvatarUrl();
        if (!string.IsNullOrEmpty(avatarUrl))
        {
            linksRow.WithButton("View Avatar", style: ButtonStyle.Link, url: avatarUrl);
            hasLinks = true;
        }

        // TODO: figure this out one day
        // var bannerUrl = targetUser.GetBannerUrl();
        // if (!string.IsNullOrEmpty(bannerUrl))
        // {
        //     linksRow.WithButton("View Banner", style: ButtonStyle.Link, url: bannerUrl);
        //     hasLinks = true;
        // }

        if (hasLinks)
        {
            mainContainer.AddComponent(new SeparatorBuilder());
            mainContainer.AddComponent(linksRow);
        }

        componentBuilder.AddComponent(mainContainer);
        return componentBuilder.Build();
    }
}