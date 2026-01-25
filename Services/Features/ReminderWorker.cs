using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Features;

public class ReminderWorker(
    ReminderService reminderService,
    DiscordSocketClient client,
    ILogger<ReminderWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ReminderWorker started. Waiting for Discord connection...");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (client is { ConnectionState: ConnectionState.Connected, LoginState: LoginState.LoggedIn })
                break;
            await Task.Delay(1000, stoppingToken);
        }

        await reminderService.InitializeAsync();

        logger.LogInformation("ReminderWorker processing loop started.");

        while (!stoppingToken.IsCancellationRequested)
            try
            {
                await reminderService.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in ReminderWorker loop.");
                await Task.Delay(5000, stoppingToken);
            }

        logger.LogInformation("ReminderWorker stopping.");
    }
}