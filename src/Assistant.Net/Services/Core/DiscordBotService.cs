using Assistant.Net.Options;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Net.Services.Core;

public class DiscordBotService(
    DiscordSocketClient client,
    InteractionService interactionService,
    ILogger<DiscordBotService> logger,
    IOptions<DiscordOptions> options)
    : BackgroundService
{
    private readonly DiscordOptions _discordOptions = options.Value;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        client.Log += LogAsync;
        interactionService.Log += LogAsync;

        client.Ready += ClientReady;

        try
        {
            await client.LoginAsync(TokenType.Bot, _discordOptions.Token).ConfigureAwait(false);
            await client.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to start Discord Bot.");
            throw;
        }

        // Keep the task alive indefinitely until cancellation is requested
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping Discord Bot...");
        await client.LogoutAsync().ConfigureAwait(false);
        await client.StopAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken);
    }

    private Task ClientReady()
    {
        logger.LogInformation("Logged in as {User}", client.CurrentUser);
        return Task.CompletedTask;
    }

    private Task LogAsync(LogMessage msg)
    {
        var severity = msg.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Trace,
            LogSeverity.Debug => LogLevel.Debug,
            _ => LogLevel.Information
        };

        logger.Log(severity, msg.Exception, "[{Source}] {Message}", msg.Source, msg.Message);
        return Task.CompletedTask;
    }
}