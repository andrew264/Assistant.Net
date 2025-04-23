using System.Collections.Concurrent;
using Assistant.Net.Modules.Games.HandCricket;
using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Games;

[Group("game", "Commands related to games.")]
public class GameInteractionModule(
    ILogger<GameInteractionModule> logger,
    GameStatsService gameStatsService,
    DiscordSocketClient client)
    : InteractionModuleBase<SocketInteractionContext>
{
    // --- RPS Specific ---
    internal static readonly ConcurrentDictionary<ulong, RpsGame> ActiveRpsGames = new();
    internal static readonly TimeSpan RpsGameTimeout = TimeSpan.FromMinutes(5);

    // --- TicTacToe Specific ---
    internal static readonly ConcurrentDictionary<string, TicTacToeGame> ActiveTicTacToeGames = new();

    // --- HandCricket Specific ---
    internal static readonly ConcurrentDictionary<string, HandCricketGame> ActiveHandCricketGames = new();
    internal static readonly TimeSpan HandCricketTimeout = TimeSpan.FromMinutes(5);


    // --- RPS Command ---
    [SlashCommand("rps", "Play Rock Paper Scissors.")]
    public async Task RpsSlashCommand(
        [Summary(description: "The user you want to play against (optional, defaults to Bot).")]
        IUser? opponent = null)
    {
        var player1 = Context.User;
        var player2 = opponent ?? client.CurrentUser;

        if (player1.Id == player2.Id)
        {
            await RespondAsync("You can't play against yourself!", ephemeral: true);
            return;
        }

        if (Context.Guild != null && !player2.IsBot)
        {
            var guildUser = Context.Guild.GetUser(player2.Id);
            if (guildUser == null)
            {
                await RespondAsync($"Could not find the specified opponent {player2.Mention} in this server.",
                    ephemeral: true);
                return;
            }

            player2 = guildUser;
        }

        var game = new RpsGame(player1, player2, gameStatsService, logger);
        var initialMessageContent = $"{player1.Mention} vs {player2.Mention}: Choose your weapon!";

        await RespondAsync(initialMessageContent);

        var responseMessage = await GetOriginalResponseAsync();
        var messageId = responseMessage.Id;

        if (!ActiveRpsGames.TryAdd(messageId, game))
        {
            logger.LogError("Failed to add RPS game with InteractionMessageId {MessageId}", messageId);
            await FollowupAsync("Sorry, couldn't start the game due to an internal error.", ephemeral: true);
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

        logger.LogInformation("Started RPS game ({MessageId}) via slash: {P1} vs {P2}", messageId, player1.Username,
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
            logger.LogError(ex, "Failed to modify initial RPS message {MessageId} to add components.", messageId);
            ActiveRpsGames.TryRemove(messageId, out _);
            await FollowupAsync("Failed to set up game buttons.", ephemeral: true);
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
            _ = Task.Delay(RpsGameTimeout).ContinueWith(async _ =>
            {
                if (ActiveRpsGames.TryRemove(messageId, out var timedOutGame))
                {
                    logger.LogInformation("RPS game {MessageId} (slash) timed out.", messageId);
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
                        logger.LogWarning(ex, "Failed to modify timed-out RPS game message {MessageId} (slash)",
                            messageId);
                    }
                }
            });
        }
    }

    [ComponentInteraction("assistant:rps:*:*", true)]
    public async Task HandleRpsButtonAsync(ulong messageId, string choiceString)
    {
        if (!ActiveRpsGames.TryGetValue(messageId, out var game))
        {
            await RespondAsync("This Rock Paper Scissors game has ended or is invalid.", ephemeral: true);
            try
            {
                var msg = await Context.Interaction.GetOriginalResponseAsync();
                if (msg != null) await msg.ModifyAsync(p => p.Components = new ComponentBuilder().Build());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to disable components on expired RPS game {MessageId}", messageId);
            }

            return;
        }

        var user = Context.User;

        if (user.Id != game.Player1.Id && user.Id != game.Player2.Id)
        {
            await RespondAsync("This isn't your game!", ephemeral: true);
            return;
        }

        if (game.HasChosen(user))
        {
            await RespondAsync("You have already made your choice!", ephemeral: true);
            return;
        }

        if (!Enum.TryParse<RpsChoice>(choiceString, out var choice) || choice == RpsChoice.None)
        {
            logger.LogWarning("Invalid choice '{ChoiceString}' received for RPS game {MessageId}", choiceString,
                messageId);
            await RespondAsync("Invalid choice selected.", ephemeral: true);
            return;
        }

        await DeferAsync();

        if (!game.MakeChoice(user, choice))
        {
            logger.LogWarning("Failed to make choice for user {User} in RPS game {MessageId}", user.Username,
                messageId);
            await FollowupAsync("Failed to register your choice.", ephemeral: true);
            return;
        }

        var originalMessage = await GetOriginalResponseAsync();
        if (originalMessage == null)
        {
            logger.LogError("Could not retrieve original response message for RPS game {MessageId} via interaction",
                messageId);
            await FollowupAsync("Error updating game display.", ephemeral: true);
            return;
        }

        if (game.BothPlayersChosen)
        {
            await HandleRpsGameOverAsync(game, originalMessage, messageId);
        }
        else
        {
            var waitingFor = user.Id == game.Player1.Id ? game.Player2 : game.Player1;
            try
            {
                await originalMessage.ModifyAsync(props =>
                {
                    props.Content =
                        $"{game.Player1.Mention} vs {game.Player2.Mention}\n{user.Mention} has chosen! Waiting for {waitingFor.Mention}...";
                    props.Components = game.GetButtons(messageId);
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to modify message while waiting for opponent in RPS game {MessageId}",
                    messageId);
                await FollowupAsync($"Registered your choice. Waiting for {waitingFor.Mention}.", ephemeral: true);
            }
        }
    }

    private async Task HandleRpsGameOverAsync(RpsGame game, IUserMessage message, ulong messageId)
    {
        ActiveRpsGames.TryRemove(messageId, out _);
        logger.LogDebug("RPS game {MessageId} (interaction) ended. Result: {Result}", messageId,
            game.GetResultMessage());

        if (Context.Guild != null)
            await game.RecordStatsIfApplicable(Context.Guild.Id);
        else if (!game.Player1.IsBot && !game.Player2.IsBot)
            logger.LogWarning(
                "Could not record RPS stats for game {MessageId} (interaction) outside of a guild context.", messageId);

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
            logger.LogError(ex, "Failed to modify message with RPS results for game {MessageId} (interaction)",
                messageId);
            try
            {
                await FollowupAsync($"Game over! {game.GetResultMessage()}", ephemeral: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    // --- TicTacToe Command ---
    [SlashCommand("tictactoe", "Play a game of Tic Tac Toe.")]
    public async Task StartTicTacToeGameAsync(
        [Summary(description: "The user you want to play against (optional, defaults to Bot).")]
        SocketUser? opponent = null)
    {
        var player1User = Context.User;
        var player2User = opponent == null || opponent.Id == player1User.Id ? Context.Client.CurrentUser : opponent;

        if (player1User.IsBot && player2User.IsBot)
        {
            await RespondAsync("Two bots can't play Tic Tac Toe against each other!", ephemeral: true);
            return;
        }

        if (Context.Guild != null && player2User is SocketGuildUser)
        {
        }
        else if (Context.Guild != null && player2User.Id != Context.Client.CurrentUser.Id)
        {
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

        if (!ActiveTicTacToeGames.TryAdd(gameId, game))
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

        if (game.CurrentPlayer.IsBot)
        {
            await Task.Delay(500);
            await MakeTicTacToeBotMoveAndUpdateResponse(game, Context.Interaction);
        }
    }

    [ComponentInteraction("assistant:tictactoe:*:*", true)]
    public async Task HandleTicTacToeButtonAsync(string gameId, string buttonPosition)
    {
        if (!ActiveTicTacToeGames.TryGetValue(gameId, out var game))
        {
            await RespondAsync("This game session has expired or is invalid.", ephemeral: true);
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

        await DeferAsync();

        var (row, col) = TicTacToeGame.IndexToCoords(position);

        if (!game.MakeMove(row, col))
        {
            await FollowupAsync("That spot is already taken!", ephemeral: true);
            return;
        }

        await UpdateTicTacToeGameResponse(game, Context.Interaction);

        if (game is { IsGameOver: false, CurrentPlayer.IsBot: true })
        {
            await Task.Delay(500);
            await MakeTicTacToeBotMoveAndUpdateResponse(game, Context.Interaction);
        }
    }

    private async Task MakeTicTacToeBotMoveAndUpdateResponse(TicTacToeGame game, SocketInteraction interaction)
    {
        if (game.IsGameOver || !game.CurrentPlayer.IsBot) return;

        var botMoveCoords = await game.GetBestMoveAsync();
        if (botMoveCoords.HasValue)
        {
            game.MakeMove(botMoveCoords.Value.row, botMoveCoords.Value.col);
            await UpdateTicTacToeGameResponse(game, interaction);
        }
        else
        {
            logger.LogError("Bot failed to determine a move in game {GameId} when it should have.", game.GameId);
            try
            {
                await interaction.ModifyOriginalResponseAsync(p =>
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

    private async Task UpdateTicTacToeGameResponse(TicTacToeGame game, SocketInteraction interaction)
    {
        string messageContent;
        var components = game.GetMessageComponent();

        if (game.IsGameOver)
        {
            if (Context.Guild != null)
                await game.RecordStatsIfApplicable(Context.Guild.Id);
            else
                logger.LogWarning("Cannot record Tic Tac Toe stats for game {GameId} outside of a guild.", game.GameId);


            ActiveTicTacToeGames.TryRemove(game.GameId, out _);
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
                case GameResultState.None:
                default:
                    messageContent = "Game over, but result is unclear.";
                    logger.LogError("Game {GameId} ended with unclear result state: {Result}", game.GameId,
                        game.Result);
                    break;
            }
        }
        else
        {
            messageContent =
                $"## {game.Player1.Mention} (❌) vs {game.Player2.Mention} (⭕)\nIt's {game.CurrentPlayer.Mention}'s turn!";
        }

        try
        {
            await interaction.ModifyOriginalResponseAsync(p =>
            {
                p.Content = messageContent;
                p.Components = components;
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to modify original response for game {GameId}", game.GameId);
            try
            {
                await FollowupAsync("There was an issue updating the game board display.", ephemeral: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    // --- HandCricket Command ---
    [SlashCommand("handcricket", "Play a game of Hand Cricket.")]
    [RequireContext(ContextType.Guild)]
    public async Task StartHandCricketGameAsync(
        [Summary("player1", "The first player (or yourself).")]
        SocketGuildUser player1,
        [Summary("player2",
            "The second player (optional, defaults to you if player1 is someone else).")]
        SocketGuildUser? player2 = null)
    {
        SocketGuildUser actualPlayer1;
        SocketGuildUser actualPlayer2;

        if (player2 == null)
        {
            if (player1.Id == Context.User.Id)
            {
                await RespondAsync("You need to specify an opponent if you are player 1!", ephemeral: true);
                return;
            }

            actualPlayer1 = player1;
            actualPlayer2 = Context.User as SocketGuildUser ??
                            throw new InvalidOperationException("Cannot get command user");
        }
        else
        {
            actualPlayer1 = player1;
            actualPlayer2 = player2;
        }


        if (actualPlayer1.Id == actualPlayer2.Id)
        {
            await RespondAsync("You can't play against yourself!", ephemeral: true);
            return;
        }

        if (actualPlayer1.IsBot || actualPlayer2.IsBot)
        {
            await RespondAsync("Bots cannot play Hand Cricket!", ephemeral: true);
            return;
        }

        var game = new HandCricketGame(actualPlayer1, actualPlayer2, Context.Channel.Id, gameStatsService, logger);

        if (!ActiveHandCricketGames.TryAdd(game.GameId, game))
        {
            logger.LogError("[HC New Game] Failed to add game {GameId} to active dictionary.", game.GameId);
            await RespondAsync("Failed to start the game due to a conflict. Please try again.", ephemeral: true);
            return;
        }

        logger.LogInformation("[HC New Game] Started Hand Cricket game {GameId}: {P1} vs {P2} in Channel {ChannelId}",
            game.GameId, actualPlayer1.Username, actualPlayer2.Username, Context.Channel.Id);

        await RespondAsync(game.GetCurrentPrompt(), embed: game.GetEmbed(), components: game.GetComponents());

        _ = Task.Delay(HandCricketTimeout).ContinueWith(_ => Task.FromResult(CheckHandCricketTimeout(game.GameId)));
    }

    [ComponentInteraction("assistant:hc:*:*:*", true)]
    public async Task HandleHandCricketInteraction(string gameId, string action, string data)
    {
        if (!ActiveHandCricketGames.TryGetValue(gameId, out var game))
        {
            await RespondAsync("This Hand Cricket game has ended or is invalid.", ephemeral: true);
            await TryDisableComponents(Context.Interaction);
            return;
        }

        if (Context.User.Id != game.Player1.Id && Context.User.Id != game.Player2.Id)
        {
            await RespondAsync("This isn't your game!", ephemeral: true);
            return;
        }

        var success = false;

        await DeferAsync();

        try
        {
            switch (action)
            {
                case "toss_eo":
                    if (game.CurrentPhase == HandCricketPhase.TossSelectEvenOdd)
                    {
                        var choice = data == "even" ? EvenOddChoice.Even : EvenOddChoice.Odd;
                        success = game.SetTossEvenOddPreference(Context.User, choice);
                    }
                    else
                    {
                        await FollowupAsync("It's not time to choose Even/Odd.", ephemeral: true);
                        return;
                    }

                    break;

                case "toss_num":
                    if (game.CurrentPhase == HandCricketPhase.TossSelectNumber)
                    {
                        if (int.TryParse(data, out var tossNum))
                        {
                            if (!game.SetTossNumber(Context.User, tossNum))
                            {
                                await FollowupAsync(
                                    "You've already selected a number for the toss, or it's not the right time.",
                                    ephemeral: true);
                                return;
                            }

                            success = true;

                            if (game.CurrentTossChoices is { Player1Number: not null, Player2Number: not null })
                                game.ResolveToss();
                        }
                    }
                    else
                    {
                        await FollowupAsync("It's not time to choose a number for the toss.", ephemeral: true);
                        return;
                    }

                    break;

                case "batbowl":
                    if (game.CurrentPhase == HandCricketPhase.TossSelectBatBowl)
                    {
                        if (Context.User.Id != game.TossWinner?.Id)
                        {
                            await FollowupAsync("Only the toss winner can choose.", ephemeral: true);
                            return;
                        }

                        var choseBat = data == "bat";
                        success = game.SetBatOrBowlChoice(Context.User, choseBat);
                    }
                    else
                    {
                        await FollowupAsync("It's not time to choose Bat/Bowl.", ephemeral: true);
                        return;
                    }

                    break;

                case "play_num":
                    if (game.CurrentPhase == HandCricketPhase.Inning1Batting ||
                        game.CurrentPhase == HandCricketPhase.Inning2Batting)
                    {
                        if (int.TryParse(data, out var gameNum))
                        {
                            if (!game.SetGameNumber(Context.User, gameNum))
                            {
                                await FollowupAsync(
                                    "You've already selected a number for this turn, or it's not the right time.",
                                    ephemeral: true);
                                return;
                            }

                            success = true;

                            if (game.BothPlayersSelectedGameNumber())
                            {
                                var (inningOver, gameOver) = game.ResolveTurn();

                                if (gameOver)
                                {
                                    var finalResultMessage = await game.GetResultStringAndRecordStats(Context.Guild.Id);
                                    ActiveHandCricketGames.TryRemove(game.GameId, out _);
                                    logger.LogInformation("[HC Game End] Game {GameId} finished.", game.GameId);

                                    await ModifyOriginalResponseAsync(props =>
                                    {
                                        props.Content = game.GetCurrentPrompt();
                                        props.Embed = game.GetEmbed();
                                        props.Components = game.GetComponents();
                                    });
                                    await Context.Channel.SendMessageAsync(finalResultMessage,
                                        allowedMentions: AllowedMentions.All);
                                    return;
                                }
                            }
                        }
                    }
                    else
                    {
                        await FollowupAsync("It's not time to select a number for the game.", ephemeral: true);
                        return;
                    }

                    break;

                default:
                    logger.LogWarning("[HC Interaction] Unknown action '{Action}' for game {GameId}", action, gameId);
                    await FollowupAsync("Unknown action.", ephemeral: true);
                    return;
            }

            if (success || game.CurrentPhase == HandCricketPhase.GameOver)
                await ModifyOriginalResponseAsync(props =>
                {
                    props.Content = game.GetCurrentPrompt();
                    props.Embed = game.GetEmbed();
                    props.Components = game.GetComponents();
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[HC Interaction] Error processing action '{Action}' for game {GameId}", action,
                gameId);
            await FollowupAsync("An internal error occurred while processing your action.", ephemeral: true);
        }
    }


    private async Task CheckHandCricketTimeout(string gameId)
    {
        if (ActiveHandCricketGames.TryGetValue(gameId, out var game))
        {
            if (DateTime.UtcNow - game.LastInteractionTime >= HandCricketTimeout)
            {
                if (ActiveHandCricketGames.TryRemove(gameId, out _))
                {
                    logger.LogInformation("[HC Timeout] Game {GameId} timed out.", gameId);
                    try
                    {
                        var channel = await client.GetChannelAsync(game.InteractionChannelId) as IMessageChannel;
                        if (channel != null)
                            await channel.SendMessageAsync(
                                $"Hand Cricket game between {game.Player1.Mention} and {game.Player2.Mention} timed out due to inactivity.");
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[HC Timeout] Failed to send timeout message for game {GameId}", gameId);
                    }
                }
            }
            else
            {
                _ = Task.Delay(HandCricketTimeout)
                    .ContinueWith(_ => Task.FromResult(CheckHandCricketTimeout(game.GameId)));
            }
        }
    }

    private async Task TryDisableComponents(SocketInteraction interaction)
    {
        try
        {
            if (interaction is SocketMessageComponent componentInteraction)
                await componentInteraction.ModifyOriginalResponseAsync(props =>
                    props.Components = new ComponentBuilder().Build());
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to disable components on interaction {InteractionId}", interaction.Id);
        }
    }
}