using System.Text;
using Assistant.Net.Services.Data;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Utilities;

public static class UserUtils
{
    // Get the top role color of a user
    public static Color? GetTopRoleColor(SocketUser? user)
    {
        if (user is not SocketGuildUser guildUser)
            return null;

        var topRole = guildUser.Roles
            .Where(role => !role.IsEveryone && role.Color != Color.Default)
            .OrderByDescending(role => role.Position)
            .FirstOrDefault();

        return topRole?.Color;
    }

    public static async Task<(MessageComponent? Components, FileAttachment? Attachment, string? ErrorMessage)>
        GenerateAvatarComponentsAsync(IUser targetUser, IHttpClientFactory httpClientFactory, ILogger logger)
    {
        var avatarUrl = targetUser.GetDisplayAvatarUrl(ImageFormat.Auto, 2048) ?? targetUser.GetDefaultAvatarUrl();

        if (string.IsNullOrEmpty(avatarUrl))
            return (null, null, "Could not retrieve avatar URL for this user.");

        var displayUserName = (targetUser as IGuildUser)?.DisplayName ?? targetUser.GlobalName ?? targetUser.Username;

        var fileAttachment = await AttachmentUtils
            .DownloadFileAsAttachmentAsync(avatarUrl, "avatar.png", httpClientFactory, logger).ConfigureAwait(false);

        if (fileAttachment == null)
            return (null, null, $"Could not download avatar for {displayUserName}.");

        var userColor = GetTopRoleColor(targetUser as SocketUser);

        var components = new ComponentBuilderV2(
            new ContainerBuilder()
                .WithAccentColor(userColor)
                .WithTextDisplay(new TextDisplayBuilder($"## {displayUserName}'s Avatar"))
                .WithMediaGallery(["attachment://avatar.png"])
                .WithActionRow(
                    new ActionRowBuilder()
                        .WithButton("Open Original", style: ButtonStyle.Link, url: avatarUrl)
                )
        ).Build();

        return (components, fileAttachment, null);
    }

    public static async Task<(MessageComponent Components, List<FileAttachment> Attachments)>
        BuildUserProfileUpdateComponentAsync(
            SocketUser before,
            SocketUser after,
            IHttpClientFactory httpClientFactory,
            ILogger logger)
    {
        var attachments = new List<FileAttachment>();
        var container = new ContainerBuilder();

        // -- Header --
        container.WithSection(new SectionBuilder()
            .AddComponent(new TextDisplayBuilder("# User Profile Updated"))
            .AddComponent(new TextDisplayBuilder($"{after.Mention}"))
            .WithAccessory(new ThumbnailBuilder
            {
                Media = new UnfurledMediaItemProperties
                    { Url = after.GetDisplayAvatarUrl() ?? after.GetDefaultAvatarUrl() }
            })
        );

        container.WithSeparator();

        // -- Username Change --
        if (before.Username != after.Username)
            container.WithTextDisplay(
                new TextDisplayBuilder($"**Username:** `{before.Username}` → `{after.Username}`"));

        // -- Avatar Change --
        var beforeAvatarUrl = before.GetDisplayAvatarUrl(size: 1024) ?? before.GetDefaultAvatarUrl();
        var afterAvatarUrl = after.GetDisplayAvatarUrl(size: 1024) ?? after.GetDefaultAvatarUrl();

        if (beforeAvatarUrl != afterAvatarUrl)
        {
            container.WithTextDisplay(new TextDisplayBuilder("**New Avatar**"));

            // Attempt download for better embed support
            var afterAttachment = await AttachmentUtils.DownloadFileAsAttachmentAsync(
                afterAvatarUrl, "avatar_after.png", httpClientFactory, logger).ConfigureAwait(false);

            if (afterAttachment.HasValue)
            {
                attachments.Add(afterAttachment.Value);
                container.WithMediaGallery(["attachment://avatar_after.png"]);
            }
            else
            {
                afterAttachment?.Dispose();
                logger.LogWarning("Failed to download avatar for user update {UserId}. Falling back to URLs.",
                    after.Id);
                container.WithMediaGallery([afterAvatarUrl]);
            }
        }

        // -- Footer --
        container
            .WithSeparator()
            .WithTextDisplay(
                new TextDisplayBuilder(
                    $"User ID: {after.Id} | {TimestampTag.FromDateTimeOffset(DateTimeOffset.UtcNow)}"));

        return (new ComponentBuilderV2(container).Build(), attachments);
    }

    public static async Task<MessageComponent> GenerateUserInfoV2Async(IUser targetUser, bool showSensitiveInfo,
        UserService userService, DiscordSocketClient client)
    {
        SocketGuildUser? guildUser = null;
        if (targetUser is SocketGuildUser sgu)
        {
            guildUser = sgu;
        }
        else
        {
            var mutualGuild = client.Guilds.FirstOrDefault(g => g.GetUser(targetUser.Id) != null);
            if (mutualGuild != null)
                guildUser = mutualGuild.GetUser(targetUser.Id);
        }

        var userModel = await userService.GetUserAsync(targetUser.Id).ConfigureAwait(false);

        // -- Data Preparation --
        var displayName = guildUser?.DisplayName ?? targetUser.GlobalName ?? targetUser.Username;
        var avatarUrl = targetUser.GetDisplayAvatarUrl(size: 2048) ?? targetUser.GetDefaultAvatarUrl();
        var accentColor = GetTopRoleColor(guildUser);

        // Timestamps
        var timestampsContent = new StringBuilder();
        timestampsContent.AppendLine($"**Account Created:** {targetUser.CreatedAt.GetRelativeTime()}");
        if (guildUser?.JoinedAt is { } joinedAt)
            timestampsContent.AppendLine($"**Joined Server:** {joinedAt.GetRelativeTime()}");
        if (showSensitiveInfo && userModel?.LastSeen is { } lastSeen)
        {
            var statusFieldName = guildUser?.Status == UserStatus.Offline ? "Last Seen:" : "Online for:";
            timestampsContent.AppendLine($"**{statusFieldName}** {lastSeen.GetRelativeTime()}");
        }

        // Roles
        string? roleString = null;
        var roles = guildUser?.Roles.Where(r => !r.IsEveryone).OrderByDescending(r => r.Position).ToList();
        if (roles is { Count: > 0 })
            roleString = string.Join(" ", roles.Select(r => r.Mention)).Truncate(4000);

        // Activities
        var activities = (guildUser?.Activities ?? targetUser.Activities).ToList();

        // -- Container Construction --
        var container = new ContainerBuilder()
            .WithAccentColor(accentColor);

        // Header
        var headerSection = new SectionBuilder()
            .AddComponent(new TextDisplayBuilder($"# {displayName}"))
            .AddComponent(new TextDisplayBuilder($"@{targetUser.Username} | {targetUser.Mention}"));

        if (!string.IsNullOrWhiteSpace(userModel?.About))
            headerSection.AddComponent(new TextDisplayBuilder(userModel.About));

        headerSection.WithAccessory(new ThumbnailBuilder
        {
            Media = new UnfurledMediaItemProperties { Url = avatarUrl }
        });

        container.WithSection(headerSection);

        // Timestamps
        container.WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(timestampsContent.ToString()));

        // Roles
        if (roleString != null)
            container.WithSeparator()
                .WithTextDisplay(new TextDisplayBuilder($"## Roles ({roles!.Count})"))
                .WithTextDisplay(new TextDisplayBuilder(roleString));

        // Activities
        if (activities.Count > 0)
        {
            container.WithSeparator()
                .WithTextDisplay(new TextDisplayBuilder("## Activities"));

            foreach (var activity in activities)
            {
                var section = CreateActivitySection(activity);
                if (section != null) container.WithSection(section);
            }
        }

        // Footer Links
        if (!string.IsNullOrEmpty(avatarUrl))
            container.WithSeparator()
                .WithActionRow(new ActionRowBuilder()
                    .WithButton("View Avatar", style: ButtonStyle.Link, url: avatarUrl));

        return new ComponentBuilderV2(container).Build();
    }

    private static SectionBuilder? CreateActivitySection(IActivity activity)
    {
        var section = new SectionBuilder();

        switch (activity)
        {
            case SpotifyGame spotify:
                section.AddComponent(new TextDisplayBuilder(
                    $"**Listening to Spotify**\n{spotify.TrackTitle.AsMarkdownLink(spotify.TrackUrl)}\nby {string.Join(", ", spotify.Artists)}"));
                section.WithAccessory(new ThumbnailBuilder
                    { Media = new UnfurledMediaItemProperties { Url = spotify.AlbumArtUrl } });
                return section;

            case RichGame richGame:
                var richGameText = new StringBuilder($"**Playing {richGame.Name}**");
                if (!string.IsNullOrWhiteSpace(richGame.Details)) richGameText.AppendLine($"\n{richGame.Details}");
                if (!string.IsNullOrWhiteSpace(richGame.State)) richGameText.AppendLine($"\n{richGame.State}");
                if (richGame.Timestamps?.Start is { } startTime)
                    richGameText.AppendLine($"\nElapsed: {startTime.GetRelativeTime()}");

                section.AddComponent(new TextDisplayBuilder(richGameText.ToString()));

                var largeAssetUrl = richGame.LargeAsset?.GetImageUrl();
                if (!string.IsNullOrWhiteSpace(largeAssetUrl))
                    section.WithAccessory(new ThumbnailBuilder
                        { Media = new UnfurledMediaItemProperties { Url = largeAssetUrl } });
                return section;

            case StreamingGame streamingGame:
                section.AddComponent(
                    new TextDisplayBuilder($"**Streaming on {streamingGame.Details}**\n{streamingGame.Name}"));
                section.WithAccessory(new ButtonBuilder("Watch Stream", style: ButtonStyle.Link,
                    url: streamingGame.Url));
                return section;

            case CustomStatusGame custom:
                var customStatusContent = new StringBuilder();
                if (custom.Emote is Emote customEmote) customStatusContent.Append($"{customEmote} ");
                if (!string.IsNullOrWhiteSpace(custom.State)) customStatusContent.Append(custom.State);

                if (customStatusContent.Length > 0)
                    section.AddComponent(new TextDisplayBuilder($"**Custom Status:** {customStatusContent}"));
                return section;

            case Game game:
                section.AddComponent(new TextDisplayBuilder($"{game.Type} {game.Name}"));
                return section;

            default:
                return null;
        }
    }
}