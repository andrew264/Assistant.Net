using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Assistant.Net.Configuration;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET;
using MongoDB.Driver;

namespace Assistant.Net.Modules.Info;

public class InfoModule(
    DiscordSocketClient client,
    InteractionService interactionService,
    CommandService commandService,
    Config config,
    IAudioService audioService)
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
}