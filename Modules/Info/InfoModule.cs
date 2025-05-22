using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Assistant.Net.Configuration;
using Assistant.Net.Services;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using ContextType = Discord.Interactions.ContextType;

namespace Assistant.Net.Modules.Info;

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
        var embed = new EmbedBuilder()
            .WithTitle("User Information")
            .WithDescription("This is a test embed.")
            .WithColor(Color.Blue)
            .WithCurrentTimestamp();

        await RespondAsync(embed: embed.Build(), ephemeral: true).ConfigureAwait(false);
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
        var mongoDriverVersion = typeof(MongoClient).Assembly.GetName().Version?.ToString() ?? "N/A";

        var embed = new EmbedBuilder()
            .WithDescription(currentUser.Mention)
            .WithColor(Color.Blue)
            .WithAuthor(currentUser.GlobalName ?? currentUser.Username,
                currentUser.GetDisplayAvatarUrl() ?? currentUser.GetDefaultAvatarUrl())
            .AddField("Creator", ownerMention, true)
            .AddField("Servers", client.Guilds.Count, true)
            .AddField("Users", totalUsers, true)
            .AddField("Gateway Ping", $"{client.Latency}ms", true)
            .AddField("Slash Commands", interactionService.SlashCommands.Count, true)
            .AddField("Prefix Commands", commandService.Commands.Count(), true)
            .AddField("Active Voice Connections", audioService.Players.Players.Count(), true)
            .AddField("Uptime", TimestampTag.FromDateTimeOffset(ProcessStartTimeOffset, TimestampTagStyles.Relative),
                true)
            .AddField("Process ID", Environment.ProcessId, true)
            .AddField("Threads", Process.GetCurrentProcess().Threads.Count, true)
            .AddField("Memory Usage", FormatUtils.FormatBytes(Process.GetCurrentProcess().WorkingSet64), true)
            .AddField("App Version", $"v{appVersion}", true)
            .AddField(".NET Version", RuntimeInformation.FrameworkDescription)
            .AddField("Operating System", RuntimeInformation.OSDescription)
            .AddField("Discord.Net Version", $"v{discordNetVersion}", true)
            .AddField("Lavalink4NET Version", $"v{lavalinkVersion}", true)
            .AddField("MongoDB.Driver Version", $"v{mongoDriverVersion}", true)
            .WithTimestamp(DateTimeOffset.UtcNow);

        await FollowupAsync(embed: embed.Build()).ConfigureAwait(false);
    }


    [SlashCommand("avatar", "Shows the avatar of a user or yourself.")]
    public async Task AvatarSlashCommandAsync(
        [Discord.Interactions.Summary("user", "The user to get the avatar from (defaults to you).")]
        IUser? user = null)
    {
        await DeferAsync().ConfigureAwait(false);

        var targetUser = user ?? Context.User;
        var avatarUrl = targetUser.GetDisplayAvatarUrl(ImageFormat.Auto, 2048) ?? targetUser.GetDefaultAvatarUrl();

        if (string.IsNullOrEmpty(avatarUrl))
        {
            await FollowupAsync("Could not retrieve avatar URL for this user.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var displayUserName =
            (targetUser as SocketGuildUser)?.DisplayName ?? targetUser.GlobalName ?? targetUser.Username;

        FileAttachment? fileAttachment = null;
        try
        {
            fileAttachment = await AttachmentUtils
                .DownloadFileAsAttachmentAsync(avatarUrl, "avatar.png", httpClientFactory, logger)
                .ConfigureAwait(false);

            if (fileAttachment == null)
            {
                await FollowupAsync($"Could not download avatar for {displayUserName}.", ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            await FollowupWithFileAsync(
                text: $"# {displayUserName}'s Avatar",
                attachment: fileAttachment.Value
            ).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to download avatar for {User} (URL: {AvatarUrl}) in slash command",
                targetUser.Username, avatarUrl);
            await FollowupAsync($"Failed to download the avatar for {displayUserName}.", ephemeral: true)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending avatar for {User} in slash command", targetUser.Username);
            await FollowupAsync($"An error occurred while fetching the avatar for {displayUserName}.", ephemeral: true)
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

        var embed = new EmbedBuilder()
            .WithTitle($"{displayUserName}'s Avatar")
            .WithImageUrl(avatarUrl)
            .WithColor(UserUtils.GetTopRoleColor(targetSocketUser))
            .Build();

        await RespondAsync(embed: embed, ephemeral: true).ConfigureAwait(false);
    }

    [SlashCommand("guildinfo", "Displays information about the current server.")]
    [Discord.Interactions.RequireContext(ContextType.Guild)]
    public async Task GuildInfoAsync()
    {
        await DeferAsync().ConfigureAwait(false);
        // TODO: Optimize the counting logic.

        var guild = Context.Guild;
        var owner = guild.Owner;

        var embed = new EmbedBuilder()
            .WithTitle(guild.Name)
            .WithColor(new Color(0x5865F2));

        if (!string.IsNullOrWhiteSpace(guild.Description)) embed.WithDescription(guild.Description);

        if (!string.IsNullOrEmpty(guild.IconUrl)) embed.WithThumbnailUrl(guild.IconUrl);

        embed.AddField("Owner", owner?.Mention ?? "None", true);
        embed.AddField("Created on",
            $"{guild.CreatedAt.GetLongDateTime()}\n" +
            $"{guild.CreatedAt.GetRelativeTime()}", true);

        var humanMembers = guild.Users.Where(u => !u.IsBot).ToList();
        var onlineHumanMembers = humanMembers.Count(u => u.Status != UserStatus.Offline);
        var totalBots = guild.Users.Count(u => u.IsBot);

        embed.AddField("Total Members", humanMembers.Count.ToString(), true);
        embed.AddField("Online Members", onlineHumanMembers.ToString(), true);
        embed.AddField("Total Bots", totalBots.ToString(), true);
        embed.AddField("Text Channels", guild.TextChannels.Count.ToString(), true);
        embed.AddField("Voice Channels", guild.VoiceChannels.Count.ToString(), true);
        embed.AddField("Roles", guild.Roles.Count.ToString(), true);
        embed.AddField("Emojis", guild.Emotes.Count.ToString(), true);

        if (guild.PremiumSubscriptionCount > 0)
        {
            embed.AddField("Boosts", guild.PremiumSubscriptionCount.ToString(), true);
            embed.AddField("Boost Tier", guild.PremiumTier.ToString(), true);
        }

        var adminCount = guild.Users.Count(u => u.GuildPermissions.Administrator);
        embed.AddField("Admins", adminCount.ToString(), true);

        var membersInVc = guild.Users.Count(u => u.VoiceChannel != null);
        if (membersInVc > 0) embed.AddField("In Voice Chat", membersInVc.ToString(), true);

        embed.WithFooter($"ID: {guild.Id}", Context.User.GetDisplayAvatarUrl());
        embed.WithTimestamp(DateTimeOffset.UtcNow);

        await FollowupAsync(embed: embed.Build()).ConfigureAwait(false);
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

        var embed = await UserUtils.GenerateUserInfoEmbedAsync(targetUser, showSensitiveInfo, userService, client)
            .ConfigureAwait(false);

        var view = new ComponentBuilder()
            .WithButton("View Avatar", style: ButtonStyle.Link,
                url: targetUser.GetDisplayAvatarUrl(ImageFormat.Auto, 2048) ?? targetUser.GetDefaultAvatarUrl())
            .Build();

        await FollowupAsync(embed: embed, components: view).ConfigureAwait(false);
    }

    [UserCommand("User Info")]
    public async Task GetUserInfoContextMenu(IUser user)
    {
        await DeferAsync(true).ConfigureAwait(false);

        var embed = await UserUtils.GenerateUserInfoEmbedAsync(user, false, userService, client).ConfigureAwait(false);

        var view = new ComponentBuilder()
            .WithButton("View Avatar", style: ButtonStyle.Link,
                url: user.GetDisplayAvatarUrl(ImageFormat.Auto, 2048) ?? user.GetDefaultAvatarUrl())
            .Build();

        await FollowupAsync(embed: embed, components: view, ephemeral: true).ConfigureAwait(false);
    }
}