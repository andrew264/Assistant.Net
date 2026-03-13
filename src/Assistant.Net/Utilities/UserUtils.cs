using System.Text;
using Assistant.Net.Services.Data;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Utilities;

public static class UserUtils
{
    public static Color? GetTopRoleColor(SocketUser? user)
    {
        if (user is not SocketGuildUser guildUser)
            return null;

        var topRole = guildUser.Roles
            .Where(role => !role.IsEveryone && role.Colors.PrimaryColor != Color.Default)
            .OrderByDescending(role => role.Position)
            .FirstOrDefault();

        return topRole?.Colors.PrimaryColor;
    }

    public static async Task<(MessageComponent? Components, FileAttachment? Attachment, string? ErrorMessage)>
        GenerateAvatarComponentsAsync(IUser targetUser, IHttpClientFactory httpClientFactory, ILogger logger)
    {
        var avatarUrl = targetUser.GetDisplayAvatarUrl(size: 2048) ?? targetUser.GetDefaultAvatarUrl();

        if (string.IsNullOrEmpty(avatarUrl))
            return (null, null, "Could not retrieve avatar URL for this user.");

        var displayUserName = (targetUser as IGuildUser)?.DisplayName ?? targetUser.GlobalName ?? targetUser.Username;

        var fileName = Path.GetFileName(new Uri(avatarUrl).AbsolutePath);
        var fileAttachment = await AttachmentUtils
            .DownloadFileAsAttachmentAsync(avatarUrl, fileName, httpClientFactory, logger).ConfigureAwait(false);

        if (fileAttachment == null)
            return (null, null, $"Could not download avatar for {displayUserName}.");

        var userColor = GetTopRoleColor(targetUser as SocketUser);

        var components = new ComponentBuilderV2(
            new ContainerBuilder()
                .WithAccentColor(userColor)
                .WithTextDisplay(new TextDisplayBuilder($"## {displayUserName}'s Avatar"))
                .WithMediaGallery([$"attachment://{fileName}"])
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

        container.WithTextDisplay(new TextDisplayBuilder($"{after.Mention}'s Profile Updated"));

        container.WithSeparator();

        if (before.Username != after.Username)
            container.WithTextDisplay(
                new TextDisplayBuilder($"**Username:** `{before.Username}` → `{after.Username}`"));

        var beforeAvatarUrl = before.GetDisplayAvatarUrl(size: 1024);
        var afterAvatarUrl = after.GetDisplayAvatarUrl(size: 1024);

        if (beforeAvatarUrl != afterAvatarUrl)
        {
            container.WithTextDisplay(new TextDisplayBuilder("**New Avatar**"));
            var fileName = Path.GetFileName(new Uri(afterAvatarUrl).AbsolutePath);
            var afterAttachment = await AttachmentUtils
                .DownloadFileAsAttachmentAsync(afterAvatarUrl, fileName, httpClientFactory, logger)
                .ConfigureAwait(false);

            if (afterAttachment.HasValue)
            {
                attachments.Add(afterAttachment.Value);
                container.WithMediaGallery([$"attachment://{fileName}"]);
            }
            else
            {
                afterAttachment?.Dispose();
                logger.LogWarning("Failed to download avatar for user update {UserId}. Falling back to URLs.",
                    after.Id);
                container.WithMediaGallery([afterAvatarUrl]);
            }
        }

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

        var displayName = guildUser?.DisplayName ?? targetUser.GlobalName ?? targetUser.Username;
        var avatarUrl = targetUser.GetDisplayAvatarUrl(size: 2048) ?? targetUser.GetDefaultAvatarUrl();
        var accentColor = GetTopRoleColor(guildUser);

        var timestampsContent = new StringBuilder();
        timestampsContent.AppendLine($"**Account Created:** {targetUser.CreatedAt.GetRelativeTime()}");
        if (guildUser?.JoinedAt is { } joinedAt)
            timestampsContent.AppendLine($"**Joined Server:** {joinedAt.GetRelativeTime()}");
        if (showSensitiveInfo && userModel?.LastSeen is { } lastSeen)
        {
            var statusFieldName = guildUser?.Status == UserStatus.Offline ? "Last Seen:" : "Online for:";
            timestampsContent.AppendLine($"**{statusFieldName}** {lastSeen.GetRelativeTime()}");
        }

        string? roleString = null;
        var roles = guildUser?.Roles.Where(r => !r.IsEveryone).OrderByDescending(r => r.Position).ToList();
        if (roles is { Count: > 0 })
            roleString = string.Join(" ", roles.Select(r => r.Mention)).Truncate(4000);

        var activities = (guildUser?.Activities ?? targetUser.Activities).ToList();

        var container = new ContainerBuilder()
            .WithAccentColor(accentColor);

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

        container.WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(timestampsContent.ToString()));

        if (roleString != null)
            container.WithSeparator()
                .WithTextDisplay(new TextDisplayBuilder($"## Roles ({roles!.Count})"))
                .WithTextDisplay(new TextDisplayBuilder(roleString));

        if (activities.Count > 0)
        {
            container.WithSeparator()
                .WithTextDisplay(new TextDisplayBuilder("## Activities"));

            foreach (var activity in activities) AddActivityToContainer(container, activity);
        }

        if (!string.IsNullOrEmpty(avatarUrl))
            container.WithSeparator()
                .WithActionRow(new ActionRowBuilder()
                    .WithButton("View Avatar", style: ButtonStyle.Link, url: avatarUrl));

        return new ComponentBuilderV2(container).Build();
    }

    private static void AddActivityToContainer(ContainerBuilder container, IActivity activity)
    {
        switch (activity)
        {
            case SpotifyGame spotify:
                var spotifyText =
                    $"**Listening to Spotify**\n{spotify.TrackTitle.AsMarkdownLink(spotify.TrackUrl)}\nby {string.Join(", ", spotify.Artists)}";
                if (!string.IsNullOrWhiteSpace(spotify.AlbumArtUrl))
                    container.WithSection(new SectionBuilder()
                        .AddComponent(new TextDisplayBuilder(spotifyText))
                        .WithAccessory(new ThumbnailBuilder
                            { Media = new UnfurledMediaItemProperties { Url = spotify.AlbumArtUrl } }));
                else
                    container.WithTextDisplay(new TextDisplayBuilder(spotifyText));
                break;

            case RichGame richGame:
                var richGameText = new StringBuilder($"**Playing {richGame.Name}**");
                if (!string.IsNullOrWhiteSpace(richGame.Details)) richGameText.AppendLine($"\n{richGame.Details}");
                if (!string.IsNullOrWhiteSpace(richGame.State)) richGameText.AppendLine($"\n{richGame.State}");
                if (richGame.Timestamps?.Start is { } startTime)
                    richGameText.AppendLine($"\nElapsed: {startTime.GetRelativeTime()}");

                var largeAssetUrl = richGame.LargeAsset?.GetImageUrl();
                if (!string.IsNullOrWhiteSpace(largeAssetUrl))
                    container.WithSection(new SectionBuilder()
                        .AddComponent(new TextDisplayBuilder(richGameText.ToString()))
                        .WithAccessory(new ThumbnailBuilder
                            { Media = new UnfurledMediaItemProperties { Url = largeAssetUrl } }));
                else
                    container.WithTextDisplay(new TextDisplayBuilder(richGameText.ToString()));
                break;

            case StreamingGame streamingGame:
                container.WithSection(new SectionBuilder()
                    .AddComponent(
                        new TextDisplayBuilder($"**Streaming on {streamingGame.Details}**\n{streamingGame.Name}"))
                    .WithAccessory(new ButtonBuilder("Watch Stream", style: ButtonStyle.Link, url: streamingGame.Url)));
                break;

            case CustomStatusGame custom:
                var customStatusContent = new StringBuilder();
                if (custom.Emote is Emote customEmote) customStatusContent.Append($"{customEmote} ");
                if (!string.IsNullOrWhiteSpace(custom.State)) customStatusContent.Append(custom.State);

                if (customStatusContent.Length > 0)
                    container.WithTextDisplay(new TextDisplayBuilder($"**Custom Status:** {customStatusContent}"));
                break;

            case Game game:
                container.WithTextDisplay(new TextDisplayBuilder($"{game.Type} {game.Name}"));
                break;
        }
    }
}