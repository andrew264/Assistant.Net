using Assistant.Net.Services.Games;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Logging;
using RunMode = Discord.Commands.RunMode;

namespace Assistant.Net.Modules.Games.PrefixModules;

public class GameModule(
    ILogger<GameModule> logger,
    GameSessionService gameSessionService
) : ModuleBase<SocketCommandContext>
{
    [Command("rps", RunMode = RunMode.Async)]
    [Alias("rockpaperscissors")]
    [Summary("Play Rock Paper Scissors with another user or the bot.")]
    public async Task RpsPrefixCommand([Remainder] IUser? opponent = null)
    {
        var player1 = Context.User;
        var player2 = opponent;

        if (Context.Guild != null && player2 is { IsBot: false })
        {
            var guildUser = Context.Guild.GetUser(player2.Id);
            if (guildUser == null)
            {
                await ReplyAsync($"Could not find the specified opponent {player2.Mention} in this server.")
                    .ConfigureAwait(false);
                return;
            }

            player2 = guildUser;
        }

        var responseMessage = await ReplyAsync("Starting Rock Paper Scissors...").ConfigureAwait(false);
        var messageId = responseMessage.Id;

        var creationResult = gameSessionService.StartRpsGame(player1, player2, messageId, Context.Guild?.Id ?? 0);

        if (creationResult.Status != GameCreationStatus.Success)
        {
            await responseMessage.ModifyAsync(props =>
                props.Content = creationResult.ErrorMessage ?? "Failed to start RPS game.").ConfigureAwait(false);
            return;
        }

        try
        {
            await responseMessage.ModifyAsync(props =>
            {
                props.Content = "";
                props.Components = creationResult.Component;
                props.Flags = MessageFlags.ComponentsV2;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Prefix RPS] Failed to modify initial message {MessageId} to add components.",
                messageId);
            await responseMessage.ModifyAsync(props => props.Content = "Failed to set up game buttons.")
                .ConfigureAwait(false);
            return;
        }

        var game = gameSessionService.GetRpsGame(messageId.ToString());
        if (game is { BothPlayersChosen: true })
        {
            var updateResult = await gameSessionService.ProcessRpsChoiceAsync(messageId.ToString(), player1,
                game.GetChoice(player1), Context.Guild?.Id ?? 0).ConfigureAwait(false);
            if (updateResult.Status == GameUpdateStatus.GameOver)
                await responseMessage.ModifyAsync(props =>
                {
                    props.Content = "";
                    props.Components = updateResult.Component;
                    props.Flags = MessageFlags.ComponentsV2;
                }).ConfigureAwait(false);
        }
    }

    [Command("tictactoe", RunMode = RunMode.Async)]
    [Alias("ttt")]
    [Summary("Play Tic Tac Toe with another user or the bot.")]
    public async Task TicTacToePrefixCommand([Remainder] IUser? opponent = null)
    {
        var player1User = Context.User;
        var player2User = opponent;

        if (Context.Guild != null && player2User is { IsBot: false })
        {
            var guildUser = Context.Guild.GetUser(player2User.Id);
            if (guildUser == null)
            {
                await ReplyAsync($"Could not find the specified opponent {player2User.Mention} in this server.")
                    .ConfigureAwait(false);
                return;
            }

            player2User = guildUser;
        }

        var creationResult = gameSessionService.StartTicTacToeGame(player1User, player2User);

        if (creationResult.Status != GameCreationStatus.Success)
        {
            await ReplyAsync(creationResult.ErrorMessage ?? "Failed to start Tic Tac Toe game.").ConfigureAwait(false);
            return;
        }

        var responseMessage =
            await ReplyAsync(components: creationResult.Component, flags: MessageFlags.ComponentsV2)
                .ConfigureAwait(false);

        if (creationResult.GameKey != null)
        {
            var game = gameSessionService.GetTicTacToeGame(creationResult.GameKey);
            if (game is { IsGameOver: false, CurrentPlayer.IsBot: true })
            {
                var updateResult = await gameSessionService.ProcessTicTacToeMoveAsync(creationResult.GameKey,
                    game.CurrentPlayer, 1, 1, Context.Guild?.Id ?? 0).ConfigureAwait(false); // TODO: make it random
                if (updateResult.Status != GameUpdateStatus.Error)
                    await responseMessage.ModifyAsync(props =>
                    {
                        props.Content = "";
                        props.Components = updateResult.Component;
                        props.Flags = MessageFlags.ComponentsV2;
                    }).ConfigureAwait(false);
            }
        }
    }

    [Command("handcricket", RunMode = RunMode.Async)]
    [Alias("hc")]
    [Summary("Play Hand Cricket with another user.")]
    [RequireContext(ContextType.Guild)]
    public async Task HandCricketPrefixCommand(IGuildUser player1Param, IGuildUser? player2Param = null)
    {
        IGuildUser actualPlayer1;
        IGuildUser actualPlayer2;

        if (player2Param == null)
        {
            if (player1Param.Id == Context.User.Id)
            {
                await ReplyAsync("You need to specify an opponent!").ConfigureAwait(false);
                return;
            }

            actualPlayer1 = player1Param;
            actualPlayer2 = Context.User as IGuildUser ??
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
            await ReplyAsync(creationResult.ErrorMessage ?? "Failed to start Hand Cricket game.").ConfigureAwait(false);
            return;
        }

        await ReplyAsync(components: creationResult.Component, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }
}