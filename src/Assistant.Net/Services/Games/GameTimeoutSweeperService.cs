using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Games;

public class GameTimeoutSweeperService(
    GameSessionService gameSessionService,
    DiscordSocketClient client,
    ILogger<GameTimeoutSweeperService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan RpsTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TttTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HcTimeout = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("GameTimeoutSweeperService started.");

        // Wait until client is ready
        while (!stoppingToken.IsCancellationRequested && client.ConnectionState != ConnectionState.Connected)
            await Task.Delay(1000, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
            try
            {
                await Task.Delay(SweepInterval, stoppingToken).ConfigureAwait(false);
                await SweepGamesAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during game timeout sweep.");
            }

        logger.LogInformation("GameTimeoutSweeperService stopped.");
    }

    private async Task SweepGamesAsync()
    {
        var expiredRps = gameSessionService.GetAndRemoveExpiredRpsGames(RpsTimeout);
        foreach (var rps in expiredRps)
        {
            logger.LogInformation("[RPS] Game in channel {ChannelId} timed out.", rps.ChannelId);
            await UpdateDiscordMessageAsync(rps.ChannelId, rps.MessageId, GameUiFactory.GetRpsTimeoutDisplay())
                .ConfigureAwait(false);
        }

        var expiredTtt = gameSessionService.GetAndRemoveExpiredTicTacToeGames(TttTimeout);
        foreach (var ttt in expiredTtt)
        {
            logger.LogInformation("[TTT] Game in channel {ChannelId} timed out.", ttt.ChannelId);
            await UpdateDiscordMessageAsync(ttt.ChannelId, ttt.MessageId, GameUiFactory.GetTicTacToeTimeoutDisplay())
                .ConfigureAwait(false);
        }

        var expiredHc = gameSessionService.GetAndRemoveExpiredHandCricketGames(HcTimeout);
        foreach (var hc in expiredHc)
        {
            logger.LogInformation("[HC] Game in channel {ChannelId} timed out.", hc.ChannelId);
            var p1Mention = $"<@{hc.Game.Player1Id}>";
            var p2Mention = $"<@{hc.Game.Player2Id}>";
            await UpdateDiscordMessageAsync(hc.ChannelId, hc.MessageId,
                GameUiFactory.GetHandCricketTimeoutDisplay(p1Mention, p2Mention)).ConfigureAwait(false);
        }
    }

    private async Task UpdateDiscordMessageAsync(ulong channelId, ulong messageId, MessageComponent newComponent)
    {
        try
        {
            if (client.GetChannel(channelId) is not ITextChannel channel) return;

            var message = await channel.GetMessageAsync(messageId).ConfigureAwait(false);
            if (message is IUserMessage userMessage)
                await userMessage.ModifyAsync(props =>
                {
                    props.Components = newComponent;
                    props.Flags = MessageFlags.ComponentsV2;
                }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to update timeout message {MessageId} in channel {ChannelId}.", messageId,
                channelId);
        }
    }
}