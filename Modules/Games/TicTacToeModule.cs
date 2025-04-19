using System.Collections.Concurrent;
using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Games;

[Group("game", "Commands related to games.")]
public class TicTacToeModule(ILogger<TicTacToeModule> logger, GameStatsService gameStatsService)
    : InteractionModuleBase<SocketInteractionContext>
{
    // Use ConcurrentDictionary for thread safety
    private static readonly ConcurrentDictionary<string, TicTacToeGame> ActiveGames = new();

    [SlashCommand("tictactoe", "Play a game of Tic Tac Toe.")]
    public async Task StartTicTacToeGameAsync(
        [Summary(description: "The user you want to play against (optional, defaults to Bot).")]
        SocketUser? opponent = null)
    {
        var player1User = Context.User;
        // Default to bot if opponent is null, or if user tries to play themselves
        var player2User = opponent == null || opponent.Id == player1User.Id ? Context.Client.CurrentUser : opponent;

        if (player1User.IsBot && player2User.IsBot)
        {
            await RespondAsync("Two bots can't play Tic Tac Toe against each other!", ephemeral: true);
            return;
        }

        // Ensure opponent is a valid user in the guild context if possible
        if (Context.Guild != null && player2User is SocketGuildUser)
        {
        } // Already a guild user
        else if (Context.Guild != null && player2User is SocketUser && player2User.Id != Context.Client.CurrentUser.Id)
        {
            // Attempt to get the GuildUser representation if it's not the bot
            var guildUser = Context.Guild.GetUser(player2User.Id);
            if (guildUser == null)
            {
                await RespondAsync($"Could not find the specified opponent {player2User.Mention} in this server.",
                    ephemeral: true);
                return;
            }

            player2User = guildUser;
        }


        var gameId = Guid.NewGuid().ToString();

        // Randomly assign X (Player1) and O (Player2)
        SocketUser playerX, playerO;
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

        if (!ActiveGames.TryAdd(gameId, game))
        {
            logger.LogError("Failed to add new Tic Tac Toe game with ID {GameId} to active games.", gameId);
            await RespondAsync("Sorry, couldn't start the game due to an internal error.", ephemeral: true);
            return;
        }

        logger.LogInformation("Started Tic Tac Toe game {GameId}: {PlayerX} (X) vs {PlayerO} (O)", gameId,
            playerX.Username, playerO.Username);

        var initialContent =
            $"## {playerX.Mention} (❌) vs {playerO.Mention} (⭕)\nIt's {game.CurrentPlayer.Mention}'s turn!";
        var components = game.GetMessageComponent();

        await RespondAsync(initialContent, components: components);

        // If the bot starts (is Player X), make its first move immediately
        if (game.CurrentPlayer.IsBot)
        {
            await Task.Delay(500);
            await MakeBotMoveAndUpdateResponse(game, Context.Interaction);
        }
    }

    [ComponentInteraction("assistant:tictactoe:*:*", true)] // Match the pattern
    public async Task HandleTicTacToeButtonAsync(string gameId, string buttonPosition)
    {
        if (!ActiveGames.TryGetValue(gameId, out var game))
        {
            await RespondAsync("This game session has expired or is invalid.", ephemeral: true);
            // Optionally disable components on the original message if possible
            try
            {
                var msg = await Context.Interaction.GetOriginalResponseAsync();
                await msg.ModifyAsync(m => m.Components = new ComponentBuilder().Build());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to disable components on expired game {GameId}", gameId);
            }

            return;
        }

        var user = Context.User;
        if (!game.IsPlayerInGame(user))
        {
            await RespondAsync("You are not part of this game. Start your own with `/game tictactoe`!",
                ephemeral: true);
            return;
        }

        if (!game.IsPlayerTurn(user))
        {
            await RespondAsync($"It's not your turn! Wait for {game.CurrentPlayer.Mention}.", ephemeral: true);
            return;
        }

        if (!int.TryParse(buttonPosition, out var position) || position < 1 || position > 9)
        {
            logger.LogWarning("Invalid button position '{ButtonPosition}' received for game {GameId}", buttonPosition,
                gameId);
            await RespondAsync("Invalid move selection.", ephemeral: true);
            return;
        }

        await DeferAsync(); // Acknowledge interaction immediately

        var (row, col) = TicTacToeGame.IndexToCoords(position);

        if (!game.MakeMove(row, col))
        {
            // This should ideally not happen due to button disabling, but handle just in case
            await FollowupAsync("That spot is already taken!", ephemeral: true);
            return;
        }

        // --- Game State Update & Response ---
        await UpdateGameResponse(game, Context.Interaction);

        // --- Check for Bot's Turn ---
        if (!game.IsGameOver && game.CurrentPlayer.IsBot)
        {
            await Task.Delay(500); // Small delay for bot move
            await MakeBotMoveAndUpdateResponse(game, Context.Interaction);
        }
    }

    private async Task MakeBotMoveAndUpdateResponse(TicTacToeGame game, SocketInteraction interaction)
    {
        if (game.IsGameOver || !game.CurrentPlayer.IsBot) return; // Safety check

        var botMoveCoords = await game.GetBestMoveAsync();
        if (botMoveCoords.HasValue)
        {
            game.MakeMove(botMoveCoords.Value.row, botMoveCoords.Value.col);
            await UpdateGameResponse(game, interaction); // Update response after bot move
        }
        else
        {
            logger.LogError("Bot failed to determine a move in game {GameId} when it should have.", game.GameId);
            // Handle this potentially - maybe end game as error? For now, log and do nothing.
            // Attempt to update the message to indicate an error?
            try
            {
                await interaction.ModifyOriginalResponseAsync(p =>
                {
                    p.Content = "An internal error occurred with the bot's turn.";
                    p.Components = game.GetMessageComponent(); // Show current board state
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to modify response after bot move error in game {GameId}", game.GameId);
            }
        }
    }

    private async Task UpdateGameResponse(TicTacToeGame game, SocketInteraction interaction)
    {
        string messageContent;
        var components = game.GetMessageComponent(); // Gets current board state (disabled if game over)

        if (game.IsGameOver)
        {
            // Record stats
            if (Context.Guild != null)
                await game.RecordStatsIfApplicable(Context.Guild.Id);
            else
                logger.LogWarning("Cannot record Tic Tac Toe stats for game {GameId} outside of a guild.", game.GameId);


            // Remove game from active list
            ActiveGames.TryRemove(game.GameId, out _);
            logger.LogInformation("Tic Tac Toe game {GameId} ended. Result: {Result}", game.GameId, game.Result);


            switch (game.Result)
            {
                case GameResultState.XWins:
                    messageContent =
                        $"## {game.Player1.Mention} (❌) vs {game.Player2.Mention} (⭕)\n**{game.Player1.Mention} wins!**";
                    break;
                case GameResultState.OWins:
                    messageContent =
                        $"## {game.Player1.Mention} (❌) vs {game.Player2.Mention} (⭕)\n**{game.Player2.Mention} wins!**";
                    break;
                case GameResultState.Tie:
                    messageContent =
                        $"## {game.Player1.Mention} (❌) vs {game.Player2.Mention} (⭕)\n**It's a tie!**";
                    break;
                default: // Should not happen
                    messageContent = "Game over, but result is unclear.";
                    logger.LogError("Game {GameId} ended with unclear result state: {Result}", game.GameId,
                        game.Result);
                    break;
            }
        }
        else
        {
            // Game is ongoing
            var currentMarkerEmoji = game.CurrentMarker == PlayerMarker.X ? "❌" : "⭕";
            messageContent =
                $"## {game.Player1.Mention} (❌) vs {game.Player2.Mention} (⭕)\nIt's {game.CurrentPlayer.Mention}'s turn!";
        }

        try
        {
            // Use ModifyOriginalResponseAsync as we Defer'd earlier or are responding to the initial command
            await interaction.ModifyOriginalResponseAsync(p =>
            {
                p.Content = messageContent;
                p.Components = components;
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to modify original response for game {GameId}", game.GameId);
            // Attempt a followup message as a fallback?
            try
            {
                await FollowupAsync("There was an issue updating the game board display.", ephemeral: true);
            }
            catch
            {
            }
        }
    }
}