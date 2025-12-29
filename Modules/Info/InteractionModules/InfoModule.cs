using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Assistant.Net.Configuration;
using Assistant.Net.Services.Data;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET;
using Microsoft.Extensions.Logging;
using Npgsql;
using ContextType = Discord.Interactions.ContextType;

namespace Assistant.Net.Modules.Info.InteractionModules;

public class InfoModule(
    DiscordSocketClient client,
    InteractionService interactionService,
    CommandService commandService,
    Config config,
    IAudioService audioService,
    UserService userService,
    IHttpClientFactory httpClientFactory,
    ILogger<InfoModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    private static readonly DateTimeOffset ProcessStartTimeOffset = new(Process.GetCurrentProcess().StartTime);

    [SlashCommand("ping", "Check the bot's latency.")]
    public async Task PingAsync()
    {
        var latency = client.Latency;
        await RespondAsync($"Pong! Latency: {latency}ms", ephemeral: true).ConfigureAwait(false);
    }

    [SlashCommand("info", "Get information about the bot.")]
    public async Task InfoAsync()
    {
        var container = new ContainerBuilder()
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"## About {client.CurrentUser.Username}"));
                section.AddComponent(new TextDisplayBuilder(
                    "This is a multipurpose Discord bot built with Discord.Net and the .NET platform."));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = client.CurrentUser.GetDisplayAvatarUrl() ?? client.CurrentUser.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                "Use `/botinfo` for detailed statistics or visit the [GitHub Repo](https://github.com/a-k-s-h-a-y/Assistant.Net) for more info."));

        var components = new ComponentBuilderV2().WithContainer(container).Build();

        await RespondAsync(components: components, ephemeral: true, flags: MessageFlags.ComponentsV2)
            .ConfigureAwait(false);
    }

    [SlashCommand("botinfo", "Get detailed information about the bot.")]
    public async Task BotInfoAsync()
    {
        await DeferAsync().ConfigureAwait(false);

        var currentUser = client.CurrentUser;
        var ownerMention = config.Client.OwnerId.HasValue ? $"<@{config.Client.OwnerId.Value}>" : "Not Configured";
        var totalUsers = client.Guilds.SelectMany(g => g.Users).DistinctBy(u => u.Id).Count();

        var discordNetVersion = typeof(DiscordSocketClient).Assembly.GetName().Version?.ToString() ?? "Unknown";
        var appVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "N/A";
        var lavalinkVersion = typeof(IAudioService).Assembly.GetName().Version?.ToString() ?? "N/A";
        var npgsqlVersion = typeof(NpgsqlConnection).Assembly.GetName().Version?.ToString() ?? "N/A";

        var container = new ContainerBuilder()
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"## {currentUser.GlobalName ?? currentUser.Username}"));
                section.AddComponent(new TextDisplayBuilder(currentUser.Mention));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = currentUser.GetDisplayAvatarUrl() ?? currentUser.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder("### 📊 Core Stats"))
            .WithTextDisplay(new TextDisplayBuilder(
                $"**Creator:** {ownerMention}\n" +
                $"**Servers:** {client.Guilds.Count}\n" +
                $"**Total Unique Users:** {totalUsers}\n" +
                $"**Slash Commands:** {interactionService.SlashCommands.Count}\n" +
                $"**Prefix Commands:** {commandService.Commands.Count()}"))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder("### ⚙️ Performance"))
            .WithTextDisplay(new TextDisplayBuilder(
                $"**Gateway Ping:** {client.Latency}ms\n" +
                $"**Voice Connections:** {audioService.Players.Players.Count()}\n" +
                $"**Uptime:** {ProcessStartTimeOffset.GetRelativeTime()}\n" +
                $"**Memory Usage:** {FormatUtils.FormatBytes(Process.GetCurrentProcess().WorkingSet64)}\n" +
                $"**Threads:** {Process.GetCurrentProcess().Threads.Count}"))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder("### 📦 Application Info"))
            .WithTextDisplay(new TextDisplayBuilder(
                $"**App Version:** v{appVersion}\n" +
                $"**Discord.Net:** v{discordNetVersion}\n" +
                $"**Lavalink4NET:** v{lavalinkVersion}\n" +
                $"**Npgsql:** v{npgsqlVersion}\n" +
                $"**.NET Version:** {RuntimeInformation.FrameworkDescription}\n" +
                $"**Operating System:** {RuntimeInformation.OSDescription}"))
            .WithSeparator()
            .WithTextDisplay(
                new TextDisplayBuilder($"*Generated at {TimestampTag.FromDateTime(DateTime.UtcNow)}*"));

        var components = new ComponentBuilderV2().WithContainer(container).Build();

        await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }


    [SlashCommand("avatar", "Shows the avatar of a user or yourself.")]
    public async Task AvatarSlashCommandAsync(
        [Discord.Interactions.Summary("user", "The user to get the avatar from (defaults to you).")]
        IUser? user = null)
    {
        await DeferAsync().ConfigureAwait(false);
        var targetUser = user ?? Context.User;

        FileAttachment? fileAttachment = null;
        try
        {
            var (components, attachment, errorMessage) =
                await UserUtils.GenerateAvatarComponentsAsync(targetUser, httpClientFactory, logger)
                    .ConfigureAwait(false);
            fileAttachment = attachment;

            if (errorMessage != null || components == null || !fileAttachment.HasValue)
            {
                await FollowupAsync(errorMessage ?? "An unknown error occurred while fetching the avatar.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            await FollowupWithFileAsync(fileAttachment.Value, components: components,
                flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing avatar slash command for {User}", targetUser.Username);
            await FollowupAsync("An unexpected error occurred while fetching the avatar.", ephemeral: true)
                .ConfigureAwait(false);
        }
        finally
        {
            fileAttachment?.Dispose();
        }
    }

    [UserCommand("View Avatar")]
    public async Task ViewAvatarUserCommand(IUser user)
    {
        var targetSocketUser = user as SocketUser ?? client.GetUser(user.Id);
        var avatarUrl = targetSocketUser.GetDisplayAvatarUrl(ImageFormat.Auto, 2048) ??
                        targetSocketUser.GetDefaultAvatarUrl();

        if (string.IsNullOrEmpty(avatarUrl))
        {
            await RespondAsync("This user does not seem to have an avatar set or it's inaccessible.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        var displayUserName = (targetSocketUser as IGuildUser)?.DisplayName ??
                              targetSocketUser.GlobalName ?? targetSocketUser.Username;

        var container = new ContainerBuilder()
            .WithAccentColor(UserUtils.GetTopRoleColor(targetSocketUser))
            .WithTextDisplay(new TextDisplayBuilder($"## {displayUserName}'s Avatar"))
            .WithMediaGallery([avatarUrl])
            .WithActionRow(row => row.WithButton("Open Original", style: ButtonStyle.Link, url: avatarUrl));

        var components = new ComponentBuilderV2().WithContainer(container).Build();

        await RespondAsync(components: components, ephemeral: true, flags: MessageFlags.ComponentsV2)
            .ConfigureAwait(false);
    }

    [SlashCommand("guildinfo", "Displays information about the current server.")]
    [Discord.Interactions.RequireContext(ContextType.Guild)]
    public async Task GuildInfoAsync()
    {
        await DeferAsync().ConfigureAwait(false);
        var guild = Context.Guild;
        var owner = guild.Owner;

        var humanMembers = guild.Users.Where(u => !u.IsBot).ToList();
        var onlineHumanMembers = humanMembers.Count(u => u.Status != UserStatus.Offline);
        var totalBots = guild.Users.Count(u => u.IsBot);

        var container = new ContainerBuilder();

        var headerSection = new SectionBuilder()
            .AddComponent(new TextDisplayBuilder($"## {guild.Name}"));

        if (!string.IsNullOrWhiteSpace(guild.Description))
            headerSection.AddComponent(new TextDisplayBuilder(guild.Description));

        if (!string.IsNullOrEmpty(guild.IconUrl))
            headerSection.WithAccessory(new ThumbnailBuilder
                { Media = new UnfurledMediaItemProperties { Url = guild.IconUrl } });

        container.WithSection(headerSection);

        container.WithSeparator();
        container.WithTextDisplay(new TextDisplayBuilder("### 📜 General"));
        container.WithTextDisplay(new TextDisplayBuilder(
            $"**Owner:** {owner?.Mention ?? "Unknown"}\n" +
            $"**Created:** {guild.CreatedAt.GetRelativeTime()}\n" +
            $"**ID:** `{guild.Id}`"));

        container.WithSeparator();
        container.WithTextDisplay(new TextDisplayBuilder("### 👥 Members & Channels"));
        container.WithTextDisplay(new TextDisplayBuilder(
            $"**Members:** {humanMembers.Count} ({onlineHumanMembers} online)\n" +
            $"**Bots:** {totalBots}\n" +
            $"**Text Channels:** {guild.TextChannels.Count}\n" +
            $"**Voice Channels:** {guild.VoiceChannels.Count}"));

        container.WithSeparator();
        container.WithTextDisplay(new TextDisplayBuilder("### 🎨 Assets & Roles"));
        container.WithTextDisplay(new TextDisplayBuilder(
            $"**Roles:** {guild.Roles.Count}\n" +
            $"**Emojis:** {guild.Emotes.Count}"));

        if (guild.PremiumSubscriptionCount > 0)
        {
            container.WithSeparator();
            container.WithTextDisplay(new TextDisplayBuilder("### ✨ Boosts"));
            container.WithTextDisplay(new TextDisplayBuilder(
                $"**Boost Tier:** {guild.PremiumTier}\n" +
                $"**Boosts:** {guild.PremiumSubscriptionCount}"));
        }

        var components = new ComponentBuilderV2().WithContainer(container).Build();
        await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [SlashCommand("userinfo", "Get information about a user.")]
    public async Task UserInfoSlashCommand(
        [Discord.Interactions.Summary("user", "The user to get information about (defaults to you).")]
        IUser? user = null)
    {
        await DeferAsync().ConfigureAwait(false);

        var targetUser = user ?? Context.User;

        var showSensitiveInfo = false;
        if (Context.User is SocketGuildUser requestingGuildUser)
            showSensitiveInfo = requestingGuildUser.GuildPermissions.Administrator;

        var components = await UserUtils.GenerateUserInfoV2Async(targetUser, showSensitiveInfo, userService, client)
            .ConfigureAwait(false);

        await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [UserCommand("User Info")]
    public async Task GetUserInfoContextMenu(IUser user)
    {
        await DeferAsync(true).ConfigureAwait(false);

        var components =
            await UserUtils.GenerateUserInfoV2Async(user, false, userService, client).ConfigureAwait(false);

        await FollowupAsync(components: components, ephemeral: true, flags: MessageFlags.ComponentsV2)
            .ConfigureAwait(false);
    }
}