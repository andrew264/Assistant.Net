using System.Collections.Concurrent;
using Assistant.Net.Modules.Games.HandCricket;
using Assistant.Net.Services;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RunMode = Discord.Commands.RunMode;
using static Assistant.Net.Modules.Games.GameInteractionModule;

namespace Assistant.Net.Modules.Games;

public class GameModule(ILogger<GameModule> logger, GameStatsService gameStatsService, DiscordSocketClient client)
    : ModuleBase<SocketCommandContext>
{
    private static readonly ConcurrentDictionary<ulong, RpsGame> ActiveRpsGames = GameInteractionModule.ActiveRpsGames;
    private static readonly TimeSpan GameTimeout = RpsGameTimeout;

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

    // --- TicTacToe Prefix Command ---
    [Command("tictactoe", RunMode = RunMode.Async)]
    [Alias("ttt")]
    [Summary("Play Tic Tac Toe with another user or the bot.")]
    public async Task TicTacToePrefixCommand([Remainder] IUser? opponent = null)
    {
        var player1User = Context.User;
        var player2User = opponent == null || opponent.Id == player1User.Id ? client.CurrentUser : opponent;

        if (player1User.IsBot && player2User.IsBot)
        {
            await ReplyAsync("Two bots can't play Tic Tac Toe against each other!");
            return;
        }

        if (Context.Guild != null && player2User is IGuildUser)
        {
        }
        else if (Context.Guild != null && !player2User.IsBot)
        {
            var guildUser = Context.Guild.GetUser(player2User.Id);
            if (guildUser == null)
            {
                await ReplyAsync($"Could not find the specified opponent {player2User.Mention} in this server.");
                return;
            }

            player2User = guildUser;
        }

        var gameId = Guid.NewGuid().ToString();

        // Randomly assign X and O
        IUser playerX, playerO;
        if (new Random().Next(0, 2) == 0)
        {
            playerX = player1User;
            playerO = player2User;
        }
        else
        {
            playerX = player2User;
            playerO = player1User;
        }

        var game = new TicTacToeGame(playerX, playerO, gameId, gameStatsService, logger);

        if (!ActiveTicTacToeGames.TryAdd(gameId, game))
        {
            logger.LogError("Failed to add new Tic Tac Toe game with ID {GameId} to active games (prefix).", gameId);
            await ReplyAsync("Sorry, couldn't start the game due to an internal error.");
            return;
        }

        logger.LogInformation("Started Tic Tac Toe game {GameId} (prefix): {PlayerX} (X) vs {PlayerO} (O)", gameId,
            playerX.Username, playerO.Username);

        var initialContent =
            $"## {playerX.Mention} (❌) vs {playerO.Mention} (⭕)\nIt's {game.CurrentPlayer.Mention}'s turn!";
        var components = game.GetMessageComponent();

        var responseMessage = await ReplyAsync(initialContent, components: components);

        if (game.CurrentPlayer.IsBot)
        {
            await Task.Delay(500);
            await MakeTicTacToeBotMoveAndUpdateMessage(game, responseMessage);
        }
    }

    // --- Helper to handle Bot's move and update the message (Prefix Command Context) ---
    private async Task MakeTicTacToeBotMoveAndUpdateMessage(TicTacToeGame game, IUserMessage message)
    {
        if (game.IsGameOver || !game.CurrentPlayer.IsBot) return;

        var botMoveCoords = await game.GetBestMoveAsync();
        if (botMoveCoords.HasValue)
        {
            if (game.MakeMove(botMoveCoords.Value.row, botMoveCoords.Value.col))
                await UpdateGameMessage(game, message);
            else
                logger.LogError("Bot failed to make valid move {Move} in game {GameId}", botMoveCoords, game.GameId);
        }
        else
        {
            logger.LogError("Bot failed to determine a move in game {GameId} when it should have.", game.GameId);
            try
            {
                await message.ModifyAsync(p =>
                {
                    p.Content = "An internal error occurred with the bot's turn.";
                    p.Components = game.GetMessageComponent();
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to modify response after bot move error in game {GameId}", game.GameId);
            }
        }
    }

    // --- Helper to update the game message (Prefix Command Context) ---
    private async Task UpdateGameMessage(TicTacToeGame game, IUserMessage message)
    {
        string messageContent;
        var components = game.GetMessageComponent();

        if (game.IsGameOver)
        {
            if (Context.Guild != null)
                await game.RecordStatsIfApplicable(Context.Guild.Id);
            else if (!game.Player1.IsBot && !game.Player2.IsBot)
                logger.LogWarning("Cannot record Tic Tac Toe stats for game {GameId} (prefix) outside of a guild.",
                    game.GameId);

            ActiveTicTacToeGames.TryRemove(game.GameId, out _);
            logger.LogInformation("Tic Tac Toe game {GameId} (prefix) ended. Result: {Result}", game.GameId,
                game.Result);

            messageContent = game.Result switch
            {
                GameResultState.XWins =>
                    $"## {game.Player1.Mention} (❌) vs {game.Player2.Mention} (⭕)\n**{game.Player1.Mention} wins!**",
                GameResultState.OWins =>
                    $"## {game.Player1.Mention} (❌) vs {game.Player2.Mention} (⭕)\n**{game.Player2.Mention} wins!**",
                GameResultState.Tie => $"## {game.Player1.Mention} (❌) vs {game.Player2.Mention} (⭕)\n**It's a tie!**",
                _ => "Game over, but result is unclear." // Fallback
            };
        }
        else
        {
            messageContent =
                $"## {game.Player1.Mention} (❌) vs {game.Player2.Mention} (⭕)\nIt's {game.CurrentPlayer.Mention}'s turn!";
        }

        try
        {
            await message.ModifyAsync(p =>
            {
                p.Content = messageContent;
                p.Components = components;
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to modify game message {MessageId} for game {GameId} (prefix)", message.Id,
                game.GameId);
        }
    }

    // --- Hand Cricket Prefix Command ---
    [Command("handcricket", RunMode = RunMode.Async)]
    [Alias("hc")]
    [Summary("Play Hand Cricket with another user.")]
    [RequireContext(ContextType.Guild)]
    public async Task HandCricketPrefixCommand(IGuildUser player1, IGuildUser? player2 = null)
    {
        // Determine players correctly
        IGuildUser actualPlayer1;
        IGuildUser actualPlayer2;

        if (player2 == null)
        {
            if (player1.Id == Context.User.Id)
            {
                await ReplyAsync("You need to specify an opponent!");
                return;
            }

            actualPlayer1 = player1;
            actualPlayer2 = Context.User as IGuildUser ??
                            throw new InvalidOperationException("Cannot get command user");
        }
        else
        {
            actualPlayer1 = player1;
            actualPlayer2 = player2;
        }


        if (actualPlayer1.Id == actualPlayer2.Id)
        {
            await ReplyAsync("You can't play against yourself!");
            return;
        }

        if (actualPlayer1.IsBot || actualPlayer2.IsBot)
        {
            await ReplyAsync("Bots cannot play Hand Cricket!");
            return;
        }

        var game = new HandCricketGame(actualPlayer1, actualPlayer2, Context.Channel.Id, gameStatsService, logger);

        if (!ActiveHandCricketGames.TryAdd(game.GameId, game))
        {
            logger.LogError("[HC New Game - Prefix] Failed to add game {GameId} to active dictionary.", game.GameId);
            await ReplyAsync("Failed to start the game due to a conflict. Please try again.");
            return;
        }

        logger.LogInformation(
            "[HC New Game - Prefix] Started Hand Cricket game {GameId}: {P1} vs {P2} in Channel {ChannelId}",
            game.GameId, actualPlayer1.Username, actualPlayer2.Username, Context.Channel.Id);

        // Send the initial message. Button clicks will be handled by the ComponentInteraction handler in GameInteractionModule
        await ReplyAsync(game.GetCurrentPrompt(), embed: game.GetEmbed(), components: game.GetComponents());

        // Start timeout task (optional but recommended) - Use the same timeout logic as slash commands
        _ = Task.Delay(HandCricketTimeout).ContinueWith(async _ => await CheckHandCricketTimeoutPrefix(game.GameId));
    }

    // --- Helper for Prefix Command Timeout ---
    private async Task CheckHandCricketTimeoutPrefix(string gameId)
    {
        if (ActiveHandCricketGames.TryGetValue(gameId, out var game))
        {
            if (DateTime.UtcNow - game.LastInteractionTime >= HandCricketTimeout)
            {
                if (ActiveHandCricketGames.TryRemove(gameId, out _))
                {
                    logger.LogInformation("[HC Timeout - Prefix] Game {GameId} timed out.", gameId);
                    try
                    {
                        if (await client.GetChannelAsync(game.InteractionChannelId) is IMessageChannel channel)
                            await channel.SendMessageAsync(
                                $"Hand Cricket game between {game.Player1.Mention} and {game.Player2.Mention} timed out due to inactivity.");
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[HC Timeout - Prefix] Failed to send timeout message for game {GameId}",
                            gameId);
                    }
                }
            }
            else
            {
                _ = Task.Delay(HandCricketTimeout)
                    .ContinueWith(async _ => await CheckHandCricketTimeoutPrefix(game.GameId));
            }
        }
    }
}