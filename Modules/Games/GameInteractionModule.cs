using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Games;

[Group("game", "Commands related to games.")]
public class GameInteractionModule(
    ILogger<GameInteractionModule> logger,
    DiscordSocketClient client,
    GameSessionService gameSessionService
)
    : InteractionModuleBase<SocketInteractionContext>
{
    // --- RPS Command ---
    [SlashCommand("rps", "Play Rock Paper Scissors.")]
    public async Task RpsSlashCommand(
        [Summary(description: "The user you want to play against (optional, defaults to Bot).")]
        IUser? opponent = null)
    {
        var player1 = Context.User;
        var player2 = opponent ?? client.CurrentUser;

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

        await DeferAsync();
        var responseMessage = await GetOriginalResponseAsync();
        var actualMessageId = responseMessage.Id;

        var creationResult = gameSessionService.StartRpsGame(player1, player2, actualMessageId, Context.Guild?.Id ?? 0);

        if (creationResult.Status != GameCreationStatus.Success)
        {
            await FollowupAsync(creationResult.ErrorMessage ?? "Failed to start RPS game.", ephemeral: true);
            return;
        }

        try
        {
            await responseMessage.ModifyAsync(props =>
            {
                props.Content = creationResult.InitialMessageContent;
                props.Components = creationResult.Components;
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to modify initial RPS message {MessageId} to add components.", actualMessageId);
            await FollowupAsync("Failed to set up game buttons.", ephemeral: true);
            return;
        }

        var game = gameSessionService.GetRpsGame(actualMessageId.ToString());
        if (game is { BothPlayersChosen: true })
        {
            var updateResult = await gameSessionService.ProcessRpsChoiceAsync(actualMessageId.ToString(), player1,
                game.GetChoice(player1), Context.Guild?.Id ?? 0);
            if (updateResult.Status == GameUpdateStatus.GameOver)
                await responseMessage.ModifyAsync(props =>
                {
                    props.Content = updateResult.MessageContent;
                    props.Embed = updateResult.Embed;
                    props.Components = updateResult.Components;
                });
        }
    }

    [ComponentInteraction("assistant:rps:*:*", true)]
    public async Task HandleRpsButtonAsync(ulong messageId, string choiceString)
    {
        await DeferAsync();

        if (!Enum.TryParse<RpsChoice>(choiceString, out var choice) || choice == RpsChoice.None)
        {
            logger.LogWarning("Invalid choice '{ChoiceString}' received for RPS game {MessageId}", choiceString,
                messageId);
            await FollowupAsync("Invalid choice selected.", ephemeral: true);
            return;
        }

        var updateResult =
            await gameSessionService.ProcessRpsChoiceAsync(messageId.ToString(), Context.User, choice,
                Context.Guild?.Id ?? 0);

        if (!string.IsNullOrEmpty(updateResult.ErrorMessage))
        {
            await FollowupAsync(updateResult.ErrorMessage, ephemeral: true);
            if (updateResult.Status == GameUpdateStatus.GameNotFound)
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

        try
        {
            await ModifyOriginalResponseAsync(props =>
            {
                props.Content = updateResult.MessageContent;
                props.Embed = updateResult.Embed;
                props.Components = updateResult.Components;
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to modify message for RPS game {MessageId} after choice.", messageId);
            await FollowupAsync(updateResult.MessageContent ?? "Game updated.",
                ephemeral: updateResult.Status != GameUpdateStatus.GameOver &&
                           updateResult.Status != GameUpdateStatus.Success);
        }
    }

    // --- TicTacToe Command ---
    [SlashCommand("tictactoe", "Play a game of Tic Tac Toe.")]
    public async Task StartTicTacToeGameAsync(
        [Summary(description: "The user you want to play against (optional, defaults to Bot).")]
        SocketUser? opponent = null)
    {
        var player1User = Context.User;
        var player2User = opponent;

        if (Context.Guild != null && player2User != null && !player2User.IsBot)
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

        var creationResult = gameSessionService.StartTicTacToeGame(player1User, player2User);

        if (creationResult.Status != GameCreationStatus.Success)
        {
            await RespondAsync(creationResult.ErrorMessage ?? "Failed to start Tic Tac Toe game.", ephemeral: true);
            return;
        }

        await RespondAsync(creationResult.InitialMessageContent, components: creationResult.Components);

        if (creationResult.GameKey != null)
        {
            var game = gameSessionService.GetTicTacToeGame(creationResult.GameKey);
            if (game is { IsGameOver: false, CurrentPlayer.IsBot: true })
            {
                var updateResult = await gameSessionService.ProcessTicTacToeMoveAsync(creationResult.GameKey,
                    game.CurrentPlayer, 1, 1, Context.Guild?.Id ?? 0); // TODO: make the first choice random

                if (updateResult.Status != GameUpdateStatus.Error && !string.IsNullOrEmpty(updateResult.MessageContent))
                {
                    var originalResponse = await GetOriginalResponseAsync();
                    await originalResponse.ModifyAsync(props =>
                    {
                        props.Content = updateResult.MessageContent;
                        props.Components = updateResult.Components;
                    });
                }
            }
        }
    }

    [ComponentInteraction("assistant:tictactoe:*:*", true)]
    public async Task HandleTicTacToeButtonAsync(string gameId, string buttonPosition)
    {
        await DeferAsync();

        if (!int.TryParse(buttonPosition, out var position) || position < 1 || position > 9)
        {
            logger.LogWarning("Invalid button position '{ButtonPosition}' received for game {GameId}", buttonPosition,
                gameId);
            await FollowupAsync("Invalid move selection.", ephemeral: true);
            return;
        }

        var (row, col) = TicTacToeGame.IndexToCoords(position);
        var updateResult =
            await gameSessionService.ProcessTicTacToeMoveAsync(gameId, Context.User, row, col, Context.Guild?.Id ?? 0);

        if (!string.IsNullOrEmpty(updateResult.ErrorMessage))
        {
            await FollowupAsync(updateResult.ErrorMessage, ephemeral: true);
            if (updateResult.Status != GameUpdateStatus.GameNotFound) return;
            try
            {
                await ModifyOriginalResponseAsync(p => p.Components = new ComponentBuilder().Build());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to disable components on expired TTT game {GameId}", gameId);
            }

            return;
        }

        await ModifyOriginalResponseAsync(props =>
        {
            props.Content = updateResult.MessageContent;
            props.Components = updateResult.Components;
        });
    }

    // --- HandCricket Command ---
    [SlashCommand("handcricket", "Play a game of Hand Cricket.")]
    [RequireContext(ContextType.Guild)]
    public async Task StartHandCricketGameAsync(
        [Summary("player1", "The first player (or yourself).")]
        SocketGuildUser player1Param,
        [Summary("player2", "The second player (optional, defaults to you if player1 is someone else).")]
        SocketGuildUser? player2Param = null)
    {
        SocketGuildUser actualPlayer1;
        SocketGuildUser actualPlayer2;

        if (player2Param == null)
        {
            if (player1Param.Id == Context.User.Id)
            {
                await RespondAsync("You need to specify an opponent if you are player 1!", ephemeral: true);
                return;
            }

            actualPlayer1 = player1Param;
            actualPlayer2 = Context.User as SocketGuildUser ??
                            throw new InvalidOperationException("Cannot get command user for HandCricket.");
        }
        else
        {
            actualPlayer1 = player1Param;
            actualPlayer2 = player2Param;
        }

        var creationResult =
            gameSessionService.StartHandCricketGame(actualPlayer1, actualPlayer2, Context.Channel.Id, Context.Guild.Id);

        if (creationResult.Status != GameCreationStatus.Success)
        {
            await RespondAsync(creationResult.ErrorMessage ?? "Failed to start Hand Cricket game.", ephemeral: true);
            return;
        }

        await RespondAsync(creationResult.InitialMessageContent, embed: creationResult.InitialEmbed,
            components: creationResult.Components);
    }

    [ComponentInteraction("assistant:hc:*:*:*", true)]
    public async Task HandleHandCricketInteraction(string gameId, string action, string data)
    {
        await DeferAsync();

        var updateResult =
            await gameSessionService.ProcessHandCricketActionAsync(gameId, Context.User, action, data,
                Context.Guild.Id);

        if (!string.IsNullOrEmpty(updateResult.ErrorMessage))
        {
            await FollowupAsync(updateResult.ErrorMessage, ephemeral: true);
            if (updateResult.Status != GameUpdateStatus.GameNotFound) return;
            try
            {
                await ModifyOriginalResponseAsync(p => p.Components = new ComponentBuilder().Build());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to disable components on expired HC game {GameId}", gameId);
            }

            return;
        }

        await ModifyOriginalResponseAsync(props =>
        {
            props.Content = updateResult.MessageContent;
            props.Embed = updateResult.Embed;
            props.Components = updateResult.Components;
        });
    }
}