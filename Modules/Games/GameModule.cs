using System.Collections.Concurrent;
using Assistant.Net.Services;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RunMode = Discord.Commands.RunMode;

namespace Assistant.Net.Modules.Games;

public class GameModule(ILogger<GameModule> logger, GameStatsService gameStatsService, DiscordSocketClient client)
    : ModuleBase<SocketCommandContext>
{
    private static readonly ConcurrentDictionary<ulong, RpsGame> ActiveRpsGames = GameInteractionModule.ActiveRpsGames;
    private static readonly TimeSpan GameTimeout = GameInteractionModule.RpsGameTimeout;

    [Command("rps", RunMode = RunMode.Async)]
    [Alias("rockpaperscissors")]
    [Summary("Play Rock Paper Scissors with another user or the bot.")]
    public async Task RpsPrefixCommand([Remainder] IUser? opponent = null)
    {
        var player1 = Context.User;
        var player2 = opponent ?? client.CurrentUser;

        if (player1.Id == player2.Id)
        {
            await ReplyAsync("You can't play against yourself!");
            return;
        }

        if (Context.Guild != null && player2 is IGuildUser)
        {
        }
        else if (Context.Guild != null && !player2.IsBot)
        {
            var guildUser = Context.Guild.GetUser(player2.Id);
            if (guildUser == null)
            {
                await ReplyAsync($"Could not find the specified opponent {player2.Mention} in this server.");
                return;
            }

            player2 = guildUser;
        }

        var game = new RpsGame(player1, player2, gameStatsService, logger);
        var initialMessageContent = $"{player1.Mention} vs {player2.Mention}: Choose your weapon!";

        var responseMessage = await ReplyAsync(initialMessageContent);
        var messageId = responseMessage.Id;

        if (!ActiveRpsGames.TryAdd(messageId, game))
        {
            logger.LogError("Failed to add RPS game with MessageId {MessageId} (prefix command)", messageId);
            await ReplyAsync("Sorry, couldn't start the game due to an internal error.");
            try
            {
                await responseMessage.DeleteAsync();
            }
            catch
            {
                // ignored
            }

            return;
        }

        logger.LogInformation("Started RPS game ({MessageId}) via prefix: {P1} vs {P2}", messageId, player1.Username,
            player2.Username);

        try
        {
            await responseMessage.ModifyAsync(props =>
            {
                props.Content = initialMessageContent;
                props.Components = game.GetButtons(messageId);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to modify initial RPS message {MessageId} (prefix) to add components.",
                messageId);
            ActiveRpsGames.TryRemove(messageId, out _);
            await ReplyAsync("Failed to set up game buttons.");
            try
            {
                await responseMessage.DeleteAsync();
            }
            catch
            {
                // ignored
            }

            return;
        }

        if (game.BothPlayersChosen)
        {
            await Task.Delay(100);
            await HandleRpsGameOverAsync(game, responseMessage, messageId);
        }
        else
        {
            _ = Task.Delay(GameTimeout).ContinueWith(async _ =>
            {
                if (ActiveRpsGames.TryRemove(messageId, out var timedOutGame))
                {
                    logger.LogInformation("RPS game {MessageId} (prefix) timed out.", messageId);
                    try
                    {
                        var currentMessage = await Context.Channel.GetMessageAsync(messageId) as IUserMessage;
                        if (currentMessage != null)
                            await currentMessage.ModifyAsync(props =>
                            {
                                props.Content = "Rock Paper Scissors game timed out.";
                                props.Embed = null;
                                props.Components = timedOutGame.GetButtons(messageId, true);
                            });
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to modify timed-out RPS game message {MessageId} (prefix)",
                            messageId);
                    }
                }
            });
        }
    }

    // --- Helper to handle game over logic (Prefix Command Context) ---
    private async Task HandleRpsGameOverAsync(RpsGame game, IUserMessage message, ulong messageId)
    {
        if (game.BothPlayersChosen && (game.Player1.IsBot || game.Player2.IsBot))
            if (ActiveRpsGames.TryRemove(messageId, out _))
                logger.LogDebug("RPS game {MessageId} (prefix) ended immediately. Result: {Result}", messageId,
                    game.GetResultMessage());

        if (Context.Guild != null)
            await game.RecordStatsIfApplicable(Context.Guild.Id);
        else if (!game.Player1.IsBot && !game.Player2.IsBot)
            logger.LogWarning("Could not record RPS stats for game {MessageId} (prefix) outside of a guild context.",
                messageId);

        try
        {
            await message.ModifyAsync(props =>
            {
                props.Content = $"{game.Player1.Mention} vs {game.Player2.Mention}";
                props.Embed = game.GetResultEmbed();
                props.Components = game.GetButtons(messageId, true);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to modify message with RPS results for game {MessageId} (prefix)", messageId);
            try
            {
                await ReplyAsync($"Game over! {game.GetResultMessage()}");
            }
            catch
            {
                // ignored
            }
        }
    }
    
    // TODO: TicTacToe as message command 
}