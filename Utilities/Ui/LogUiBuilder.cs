using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Utilities.Ui;

public static class LogUiBuilder
{
    public static MessageComponent BuildMessageUpdatedComponent(IMessage before, IMessage after,
        SocketGuildChannel guildChannel)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.Orange)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder("# Message Edit"));
                section.AddComponent(new TextDisplayBuilder($"in <#{guildChannel.Id}>"));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = after.Author.GetDisplayAvatarUrl() ?? after.Author.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"**Before:**\n> {(string.IsNullOrWhiteSpace(before.Content) ? "*(Empty)*" : before.Content.Truncate(1000))}"))
            .WithTextDisplay(new TextDisplayBuilder(
                $"**After:**\n> {(string.IsNullOrWhiteSpace(after.Content) ? "*(Empty)*" : after.Content.Truncate(1000))}"))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {after.Author.Id} | {TimestampTag.FromDateTimeOffset(after.EditedTimestamp ?? after.Timestamp)}"))
            .WithActionRow(row => row.WithButton("Jump to Message", style: ButtonStyle.Link, url: after.GetJumpUrl()));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    public static MessageComponent BuildMessageDeletedComponent(IMessage message, SocketGuildChannel guildChannel)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.Red)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder("# Message Deleted"));
                section.AddComponent(new TextDisplayBuilder($"from <#{guildChannel.Id}>"));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = message.Author.GetDisplayAvatarUrl() ?? message.Author.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"**Content:**\n> {(string.IsNullOrWhiteSpace(message.Content) ? "*(Empty)*" : message.Content.Truncate(2000))}"
            ));

        if (message.Attachments.Count > 0)
        {
            var attachmentsText = string.Join("\n",
                message.Attachments.Select(a => $"📄 [{a.Filename}]({a.Url}) ({FormatUtils.FormatBytes(a.Size)})"));
            container.WithTextDisplay(new TextDisplayBuilder($"**Attachments:**\n{attachmentsText}"));
        }

        container
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {message.Author.Id} | Message ID: {message.Id} | {TimestampTag.FromDateTimeOffset(message.Timestamp)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    public static MessageComponent BuildNicknameChangeComponent(SocketGuildUser before, SocketGuildUser after)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.LightOrange)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder("# Nickname Changed"));
                section.AddComponent(new TextDisplayBuilder($"{after.Mention}"));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = after.GetDisplayAvatarUrl() ?? after.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder($"**Before:** `{before.DisplayName}`"))
            .WithTextDisplay(new TextDisplayBuilder($"**After:** `{after.DisplayName}`"))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {after.Id} | {TimestampTag.FromDateTimeOffset(DateTimeOffset.UtcNow)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    public static async Task<(MessageComponent Components, List<FileAttachment> Attachments)>
        BuildUserProfileUpdateComponentAsync(
            SocketUser before,
            SocketUser after,
            IHttpClientFactory httpClientFactory,
            ILogger logger) =>
        await UserUtils.BuildUserProfileUpdateComponentAsync(before, after, httpClientFactory, logger)
            .ConfigureAwait(false);

    public static MessageComponent BuildVoiceStateUpdateComponent(SocketGuildUser member, string actionDescription)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.DarkGreen)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder("# Voice State Update"));
                section.AddComponent(new TextDisplayBuilder(member.Mention));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = member.GetDisplayAvatarUrl() ?? member.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(actionDescription))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {member.Id} | {TimestampTag.FromDateTimeOffset(DateTimeOffset.UtcNow)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    public static MessageComponent BuildGuildEventComponent(SocketGuildUser user, string title, Color color)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(color)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"# {user.Username} {title}"));
                section.AddComponent(new TextDisplayBuilder(user.Mention));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl() }
                });
            });

        if (title.Equals("Joined", StringComparison.OrdinalIgnoreCase))
            container.WithTextDisplay(new TextDisplayBuilder(
                $"**Account Created:** {TimestampTag.FromDateTimeOffset(user.CreatedAt, TimestampTagStyles.Relative)}"));

        container
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {user.Id} | {TimestampTag.FromDateTimeOffset(user.JoinedAt ?? DateTimeOffset.UtcNow)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    public static MessageComponent BuildBanEventComponent(SocketUser user, string banReason)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.DarkRed)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"# {user.Username} Banned"));
                section.AddComponent(new TextDisplayBuilder(user.Mention));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder($"**Reason:** {banReason}"))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {user.Id} | {TimestampTag.FromDateTimeOffset(DateTimeOffset.UtcNow)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    public static MessageComponent BuildUnbanEventComponent(SocketUser user)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.DarkGreen)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"# {user.Username} Unbanned"));
                section.AddComponent(new TextDisplayBuilder(user.Mention));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {user.Id} | {TimestampTag.FromDateTimeOffset(DateTimeOffset.UtcNow)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }
}