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
            .WithTextDisplay(new TextDisplayBuilder("# Message Edit"))
            .WithTextDisplay(new TextDisplayBuilder($"in <#{guildChannel.Id}>"))
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
            .WithTextDisplay(new TextDisplayBuilder("# Message Deleted"))
            .WithTextDisplay(new TextDisplayBuilder($"from <#{guildChannel.Id}>"))
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
            .WithTextDisplay(new TextDisplayBuilder("# Nickname Changed"))
            .WithTextDisplay(new TextDisplayBuilder($"{after.Mention}"))
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
            .WithTextDisplay(new TextDisplayBuilder("# Voice State Update"))
            .WithTextDisplay(new TextDisplayBuilder(member.Mention))
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
            .WithTextDisplay(new TextDisplayBuilder($"# {user.Username} {title}"))
            .WithTextDisplay(new TextDisplayBuilder(user.Mention));

        if (title.Equals("Joined", StringComparison.OrdinalIgnoreCase))
            container.WithTextDisplay(
                new TextDisplayBuilder($"**Account Created:** {user.CreatedAt.GetRelativeTime()}"));

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
            .WithTextDisplay(new TextDisplayBuilder($"# {user.Username} Banned"))
            .WithTextDisplay(new TextDisplayBuilder(user.Mention))
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
            .WithTextDisplay(new TextDisplayBuilder($"# {user.Username} Unbanned"))
            .WithTextDisplay(new TextDisplayBuilder(user.Mention))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {user.Id} | {TimestampTag.FromDateTimeOffset(DateTimeOffset.UtcNow)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }
}