using Assistant.Net.Services.GuildFeatures;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Background;

public class ReminderWorker(
    ReminderService reminderService,
    DiscordSocketClient client,
    ILogger<ReminderWorker> logger)
    : BackgroundService
{
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ReminderWorker started.");

        await Task.Delay(5000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (client is { ConnectionState: ConnectionState.Connected, LoginState: LoginState.LoggedIn })
                {
                    await reminderService.ProcessDueRemindersAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ReminderWorker loop.");
            }

            await Task.Delay(_checkInterval, stoppingToken).ConfigureAwait(false);
        }

        logger.LogInformation("ReminderWorker stopping.");
    }
}