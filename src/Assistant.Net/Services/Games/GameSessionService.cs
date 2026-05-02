using System.Collections.Concurrent;
using Assistant.Net.Services.Data;
using Assistant.Net.Services.Games.Logic;
using Assistant.Net.Services.Games.Models;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Games;

public enum GameCreationStatus
{
    Success,
    PlayersInvalid,
    InternalError
}

public enum GameUpdateStatus
{
    Success,
    GameNotFound,
    NotPlayerTurn,
    InvalidMove,
    AlreadyChosen,
    NotPlayerInGame,
    Error,
    GameOver
}

public record GameCreationResult(
    GameCreationStatus Status,
    string? ErrorMessage = null,
    MessageComponent? Component = null,
    string? GameKey = null
);

public record GameUpdateResult(
    GameUpdateStatus Status,
    MessageComponent? Component = null,
    string? ErrorMessage = null
);

public class GameSessionService(
    ILogger<GameSessionService> logger,
    GameStatsService gameStatsService,
    DiscordSocketClient client)
{
    private readonly ConcurrentDictionary<string, GameSessionTracker<HandCricketGame>> _activeHandCricketGames = new();
    private readonly ConcurrentDictionary<string, GameSessionTracker<RpsGame>> _activeRpsGames = new();
    private readonly ConcurrentDictionary<string, GameSessionTracker<TicTacToeGame>> _activeTicTacToeGames = new();

    // --- RPS ---

    public GameCreationResult StartRpsGame(IUser player1, IUser? player2Input, ulong channelId, ulong messageId,
        ulong guildId)
    {
        var player2 = player2Input ?? client.CurrentUser;

        if (player1.Id == player2.Id)
            return new GameCreationResult(GameCreationStatus.PlayersInvalid, "You can't play against yourself!");

        var game = new RpsGame(player1.Id, player1.Username, player1.IsBot, player2.Id, player2.Username,
            player2.IsBot);
        var tracker = new GameSessionTracker<RpsGame>(game, channelId, messageId);
        var gameKey = messageId.ToString();

        if (!_activeRpsGames.TryAdd(gameKey, tracker))
        {
            logger.LogError("[RPS] Failed to add game with MessageId {MessageId} to active games.", messageId);
            return new GameCreationResult(GameCreationStatus.InternalError,
                "Sorry, couldn't start the game due to an internal conflict.");
        }

        logger.LogInformation("[RPS] Started game ({MessageId}): {P1} vs {P2}", messageId, player1.Username,
            player2.Username);

        var component = GameUiFactory.BuildRpsGameComponent(messageId, game);

        if (game.BothPlayersChosen)
        {
            logger.LogDebug("[RPS] Game {MessageId} involves bot(s) and choices are made, will resolve quickly.",
                messageId);
            _ = Task.Run(async () =>
            {
                await Task.Delay(100).ConfigureAwait(false);
                if (_activeRpsGames.TryGetValue(gameKey, out var immediateTracker) &&
                    immediateTracker.Game.BothPlayersChosen)
                    await ProcessRpsEndOfGame(gameKey, immediateTracker, guildId).ConfigureAwait(false);
            });
        }

        return new GameCreationResult(GameCreationStatus.Success, Component: component, GameKey: gameKey);
    }

    private async Task ProcessRpsEndOfGame(string gameKey, GameSessionTracker<RpsGame> tracker, ulong guildId)
    {
        if (_activeRpsGames.TryRemove(gameKey, out _))
        {
            var game = tracker.Game;
            logger.LogDebug("[RPS] Game {GameKey} ended. Result: {Result}", gameKey, game.GetResultMessage());

            if (guildId != 0 && game is { P1IsBot: false, P2IsBot: false })
            {
                var winnerId = game.GetWinnerId();
                try
                {
                    if (winnerId == game.Player1Id)
                        await gameStatsService
                            .RecordGameResultAsync(game.Player1Id, game.Player2Id, guildId,
                                GameStatsService.RpsGameName).ConfigureAwait(false);
                    else if (winnerId == game.Player2Id)
                        await gameStatsService
                            .RecordGameResultAsync(game.Player2Id, game.Player1Id, guildId,
                                GameStatsService.RpsGameName).ConfigureAwait(false);
                    else
                        await gameStatsService.RecordGameResultAsync(game.Player1Id, game.Player2Id, guildId,
                            GameStatsService.RpsGameName, true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to record RPS game stats for Guild {GuildId} ({P1} vs {P2})", guildId,
                        game.Player1Name, game.Player2Name);
                }
            }
        }
    }

    public async Task<GameUpdateResult> ProcessRpsChoiceAsync(string gameKey, IUser user, RpsChoice choice,
        ulong guildId)
    {
        if (!_activeRpsGames.TryGetValue(gameKey, out var tracker))
            return new GameUpdateResult(GameUpdateStatus.GameNotFound,
                ErrorMessage: "This Rock Paper Scissors game has ended or is invalid.");

        var game = tracker.Game;
        tracker.RecordInteraction();

        if (user.Id != game.Player1Id && user.Id != game.Player2Id)
            return new GameUpdateResult(GameUpdateStatus.NotPlayerInGame, ErrorMessage: "This isn't your game!");

        if (game.HasChosen(user.Id))
            return new GameUpdateResult(GameUpdateStatus.AlreadyChosen,
                ErrorMessage: "You have already made your choice!");

        if (choice == RpsChoice.None)
        {
            logger.LogWarning("[RPS] Invalid choice '{ChoiceString}' received for game {GameKey}", choice, gameKey);
            return new GameUpdateResult(GameUpdateStatus.InvalidMove, ErrorMessage: "Invalid choice selected.");
        }

        if (!game.MakeChoice(user.Id, choice))
        {
            logger.LogWarning("[RPS] Failed to make choice for user {User} in game {GameKey}", user.Username, gameKey);
            return new GameUpdateResult(GameUpdateStatus.Error, ErrorMessage: "Failed to register your choice.");
        }

        var messageId = ulong.Parse(gameKey);

        if (!game.BothPlayersChosen)
            return new GameUpdateResult(GameUpdateStatus.Success, GameUiFactory.BuildRpsGameComponent(messageId, game));

        await ProcessRpsEndOfGame(gameKey, tracker, guildId).ConfigureAwait(false);
        return new GameUpdateResult(GameUpdateStatus.GameOver, GameUiFactory.BuildRpsGameComponent(messageId, game));
    }

    public bool IsRpsGameActive(string gameKey) => _activeRpsGames.ContainsKey(gameKey);
    public RpsGame? GetRpsGame(string gameKey) => _activeRpsGames.GetValueOrDefault(gameKey)?.Game;

    // --- Tic Tac Toe ---

    public GameCreationResult StartTicTacToeGame(IUser player1User, IUser? opponent, ulong channelId, ulong messageId)
    {
        var player2User = opponent == null || opponent.Id == player1User.Id ? client.CurrentUser : opponent;

        if (player1User.IsBot && player2User.IsBot)
            return new GameCreationResult(GameCreationStatus.PlayersInvalid,
                "Two bots can't play Tic Tac Toe against each other!");

        var gameId = Guid.NewGuid().ToString();
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

        var game = new TicTacToeGame(playerX.Id, playerX.Username, playerX.IsBot, playerO.Id, playerO.Username,
            playerO.IsBot, gameId);
        var tracker = new GameSessionTracker<TicTacToeGame>(game, channelId, messageId);

        if (!_activeTicTacToeGames.TryAdd(gameId, tracker))
        {
            logger.LogError("[TTT] Failed to add new game with ID {GameId} to active games.", gameId);
            return new GameCreationResult(GameCreationStatus.InternalError,
                "Sorry, couldn't start the game due to an internal error.");
        }

        logger.LogInformation("[TTT] Started game {GameId}: {PlayerX} (X) vs {PlayerO} (O)", gameId, playerX.Username,
            playerO.Username);

        var component = GameUiFactory.BuildTicTacToeComponent(game);
        return new GameCreationResult(GameCreationStatus.Success, Component: component, GameKey: gameId);
    }

    public async Task<GameUpdateResult> ProcessTicTacToeMoveAsync(string gameId, IUser user, int row, int col,
        ulong guildId)
    {
        if (!_activeTicTacToeGames.TryGetValue(gameId, out var tracker))
            return new GameUpdateResult(GameUpdateStatus.GameNotFound,
                ErrorMessage: "This game session has expired or is invalid.");

        var game = tracker.Game;
        tracker.RecordInteraction();

        if (!game.IsPlayerInGame(user.Id))
            return new GameUpdateResult(GameUpdateStatus.NotPlayerInGame,
                ErrorMessage: "You are not part of this game.");
        if (!game.IsPlayerTurn(user.Id))
            return new GameUpdateResult(GameUpdateStatus.NotPlayerTurn,
                ErrorMessage: $"It's not your turn! Wait for {game.CurrentPlayerMention}.");

        if (!game.MakeMove(row, col))
            return new GameUpdateResult(GameUpdateStatus.InvalidMove, ErrorMessage: "That spot is already taken!");

        if (game is { IsGameOver: false, CurrentPlayerIsBot: true })
        {
            var botMoveCoords = await game.GetBestMoveAsync().ConfigureAwait(false);
            if (botMoveCoords.HasValue)
                game.MakeMove(botMoveCoords.Value.row, botMoveCoords.Value.col);
            else
                logger.LogError("[TTT] Bot failed to determine a move in game {GameId} when it should have.",
                    game.GameId);
        }

        var component = GameUiFactory.BuildTicTacToeComponent(game);

        if (!game.IsGameOver)
            return new GameUpdateResult(GameUpdateStatus.Success, component);

        _activeTicTacToeGames.TryRemove(game.GameId, out _);
        await ProcessTicTacToeEndOfGame(game, guildId).ConfigureAwait(false);
        logger.LogInformation("[TTT] Game {GameId} ended. Result: {Result}", game.GameId, game.Result);

        return new GameUpdateResult(GameUpdateStatus.GameOver, component);
    }

    private async Task ProcessTicTacToeEndOfGame(TicTacToeGame game, ulong guildId)
    {
        if (guildId == 0 || game.P1IsBot || game.P2IsBot) return;

        try
        {
            var recordTask = game.Result switch
            {
                GameResultState.XWins => gameStatsService.RecordGameResultAsync(game.Player1Id, game.Player2Id, guildId,
                    GameStatsService.TicTacToeGameName),
                GameResultState.OWins => gameStatsService.RecordGameResultAsync(game.Player2Id, game.Player1Id, guildId,
                    GameStatsService.TicTacToeGameName),
                GameResultState.Tie => gameStatsService.RecordGameResultAsync(game.Player1Id, game.Player2Id, guildId,
                    GameStatsService.TicTacToeGameName, true),
                _ => Task.CompletedTask
            };
            await recordTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record game stats for TTT game {GameId} in guild {GuildId}", game.GameId,
                guildId);
        }
    }

    public bool IsTicTacToeGameActive(string gameKey) => _activeTicTacToeGames.ContainsKey(gameKey);
    public TicTacToeGame? GetTicTacToeGame(string gameKey) => _activeTicTacToeGames.GetValueOrDefault(gameKey)?.Game;

    // --- Hand Cricket ---

    public GameCreationResult StartHandCricketGame(IUser player1, IUser player2, ulong channelId, ulong messageId)
    {
        if (player1.Id == player2.Id)
            return new GameCreationResult(GameCreationStatus.PlayersInvalid, "You can't play against yourself!");
        if (player1.IsBot || player2.IsBot)
            return new GameCreationResult(GameCreationStatus.PlayersInvalid, "Bots cannot play Hand Cricket!");

        var game = new HandCricketGame(player1.Id, player1.Username, player2.Id, player2.Username);
        var tracker = new GameSessionTracker<HandCricketGame>(game, channelId, messageId);

        if (!_activeHandCricketGames.TryAdd(game.GameId, tracker))
        {
            logger.LogError("[HC] Failed to add game {GameId} to active dictionary.", game.GameId);
            return new GameCreationResult(GameCreationStatus.InternalError,
                "Failed to start the game due to a conflict. Please try again.");
        }

        logger.LogInformation("[HC] Started game {GameId}: {P1} vs {P2} in Channel {ChannelId}", game.GameId,
            player1.Username, player2.Username, channelId);

        return new GameCreationResult(
            GameCreationStatus.Success,
            Component: GameUiFactory.BuildHandCricketComponent(game),
            GameKey: game.GameId
        );
    }

    public async Task<GameUpdateResult> ProcessHandCricketActionAsync(string gameId, IUser user, string action,
        string data, ulong guildId)
    {
        if (!_activeHandCricketGames.TryGetValue(gameId, out var tracker))
            return new GameUpdateResult(GameUpdateStatus.GameNotFound,
                ErrorMessage: "This Hand Cricket game has ended or is invalid.");

        var game = tracker.Game;
        tracker.RecordInteraction();

        if (user.Id != game.Player1Id && user.Id != game.Player2Id)
            return new GameUpdateResult(GameUpdateStatus.NotPlayerInGame, ErrorMessage: "This isn't your game!");

        string? userVisibleErrorMessage = null;

        switch (action)
        {
            case "toss_eo":
                if (game.CurrentPhase == HandCricketPhase.TossSelectEvenOdd)
                {
                    var choice = data == "even" ? EvenOddChoice.Even : EvenOddChoice.Odd;
                    game.SetTossEvenOddPreference(user.Id, choice);
                }
                else
                {
                    userVisibleErrorMessage = "It's not time to choose Even/Odd.";
                }

                break;
            case "toss_num":
                if (game.CurrentPhase == HandCricketPhase.TossSelectNumber)
                {
                    if (int.TryParse(data, out var tossNum))
                    {
                        if (!game.SetTossNumber(user.Id, tossNum))
                        {
                            userVisibleErrorMessage =
                                "You've already selected a number for the toss, or it's not the right time/valid number.";
                        }
                        else
                        {
                            if (game.CurrentTossChoices is { Player1Number: not null, Player2Number: not null })
                                game.ResolveToss();
                        }
                    }
                    else
                    {
                        userVisibleErrorMessage = "Invalid number format for toss.";
                    }
                }
                else
                {
                    userVisibleErrorMessage = "It's not time to choose a number for the toss.";
                }

                break;
            case "batbowl":
                if (game.CurrentPhase == HandCricketPhase.TossSelectBatBowl)
                {
                    if (user.Id != game.TossWinnerId)
                    {
                        userVisibleErrorMessage = "Only the toss winner can choose.";
                    }
                    else
                    {
                        var choseBat = data == "bat";
                        game.SetBatOrBowlChoice(user.Id, choseBat);
                    }
                }
                else
                {
                    userVisibleErrorMessage = "It's not time to choose Bat/Bowl.";
                }

                break;
            case "play_num":
                if (game.CurrentPhase is HandCricketPhase.Inning1Batting or HandCricketPhase.Inning2Batting)
                {
                    if (int.TryParse(data, out var gameNum))
                    {
                        if (!game.SetGameNumber(user.Id, gameNum))
                        {
                            userVisibleErrorMessage =
                                "You've already selected a number for this turn, or it's not the right time/valid number.";
                        }
                        else
                        {
                            if (game.BothPlayersSelectedGameNumber())
                            {
                                var gameOver = game.ResolveTurn();
                                if (gameOver)
                                {
                                    _activeHandCricketGames.TryRemove(game.GameId, out _);
                                    await ProcessHandCricketEndOfGame(game, guildId).ConfigureAwait(false);
                                    logger.LogInformation("[HC] Game {GameId} finished.", game.GameId);

                                    return new GameUpdateResult(GameUpdateStatus.GameOver,
                                        GameUiFactory.BuildHandCricketComponent(game));
                                }
                            }
                        }
                    }
                    else
                    {
                        userVisibleErrorMessage = "Invalid number format for play.";
                    }
                }
                else
                {
                    userVisibleErrorMessage = "It's not time to select a number for the game.";
                }

                break;
            default:
                logger.LogWarning("[HC] Unknown action '{Action}' for game {GameId}", action, gameId);
                userVisibleErrorMessage = "Unknown action.";
                break;
        }

        return !string.IsNullOrEmpty(userVisibleErrorMessage)
            ? new GameUpdateResult(GameUpdateStatus.Error, ErrorMessage: userVisibleErrorMessage)
            : new GameUpdateResult(GameUpdateStatus.Success, GameUiFactory.BuildHandCricketComponent(game));
    }

    private async Task ProcessHandCricketEndOfGame(HandCricketGame game, ulong guildId)
    {
        if (guildId == 0) return;

        ulong winnerId;
        ulong loserId;
        var isTie = false;

        if (game.Player1Score > game.Player2Score)
        {
            winnerId = game.Player1Id;
            loserId = game.Player2Id;
        }
        else if (game.Player2Score > game.Player1Score)
        {
            winnerId = game.Player2Id;
            loserId = game.Player1Id;
        }
        else
        {
            winnerId = game.Player1Id;
            loserId = game.Player2Id;
            isTie = true;
        }

        try
        {
            await gameStatsService
                .RecordGameResultAsync(winnerId, loserId, guildId, GameStatsService.HandCricketGameName, isTie)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record game stats for HC game {GameId} in guild {GuildId}", game.GameId,
                guildId);
        }
    }

    public bool IsHandCricketGameActive(string gameKey) => _activeHandCricketGames.ContainsKey(gameKey);

    public HandCricketGame? GetHandCricketGame(string gameKey) =>
        _activeHandCricketGames.GetValueOrDefault(gameKey)?.Game;

    // --- Sweeping Mechanisms ---

    public List<GameSessionTracker<RpsGame>> GetAndRemoveExpiredRpsGames(TimeSpan timeout)
    {
        var expired = new List<GameSessionTracker<RpsGame>>();
        var cutoffTime = DateTimeOffset.UtcNow - timeout;

        foreach (var kvp in _activeRpsGames)
            if (kvp.Value.LastInteractionTime < cutoffTime)
                if (_activeRpsGames.TryRemove(kvp.Key, out var tracker))
                    expired.Add(tracker);

        return expired;
    }

    public List<GameSessionTracker<TicTacToeGame>> GetAndRemoveExpiredTicTacToeGames(TimeSpan timeout)
    {
        var expired = new List<GameSessionTracker<TicTacToeGame>>();
        var cutoffTime = DateTimeOffset.UtcNow - timeout;

        foreach (var kvp in _activeTicTacToeGames)
            if (kvp.Value.LastInteractionTime < cutoffTime)
                if (_activeTicTacToeGames.TryRemove(kvp.Key, out var tracker))
                    expired.Add(tracker);

        return expired;
    }

    public List<GameSessionTracker<HandCricketGame>> GetAndRemoveExpiredHandCricketGames(TimeSpan timeout)
    {
        var expired = new List<GameSessionTracker<HandCricketGame>>();
        var cutoffTime = DateTimeOffset.UtcNow - timeout;

        foreach (var kvp in _activeHandCricketGames)
            if (kvp.Value.LastInteractionTime < cutoffTime)
                if (_activeHandCricketGames.TryRemove(kvp.Key, out var tracker))
                    expired.Add(tracker);

        return expired;
    }
}