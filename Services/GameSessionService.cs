using System.Collections.Concurrent;
using Assistant.Net.Modules.Games;
using Assistant.Net.Modules.Games.HandCricket;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services;

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
    string? InitialMessageContent = null,
    Embed? InitialEmbed = null,
    MessageComponent? Components = null,
    string? GameKey = null
);

public record GameUpdateResult(
    GameUpdateStatus Status,
    string? MessageContent = null,
    Embed? Embed = null,
    MessageComponent? Components = null,
    string? ErrorMessage = null
);

public class GameSessionService(
    ILogger<GameSessionService> logger,
    GameStatsService gameStatsService,
    DiscordSocketClient client)
{
    private static readonly TimeSpan RpsGameTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TicTacToeGameTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan HandCricketTimeout = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, HandCricketGame> _activeHandCricketGames = new();

    private readonly ConcurrentDictionary<string, RpsGame> _activeRpsGames = new();
    private readonly ConcurrentDictionary<string, TicTacToeGame> _activeTicTacToeGames = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _gameTimeoutTokens = new();

    private void StartTimeoutTask<TGame>(string gameKey, TimeSpan timeout,
        ConcurrentDictionary<string, TGame> gameCollection, string gameNameForLog) where TGame : class
    {
        var cts = new CancellationTokenSource();
        if (!_gameTimeoutTokens.TryAdd(gameKey, cts))
        {
            if (_gameTimeoutTokens.TryRemove(gameKey, out var oldCts))
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            _gameTimeoutTokens.TryAdd(gameKey, cts);
        }

        _ = Task.Delay(timeout, cts.Token).ContinueWith(task =>
        {
            if (task.IsCanceled) return;

            if (gameCollection.TryRemove(gameKey, out _))
                logger.LogInformation("{GameName} game {GameKey} timed out and was removed from active sessions.",
                    gameNameForLog, gameKey);
            if (_gameTimeoutTokens.TryRemove(gameKey, out var removedCts)) removedCts.Dispose();
        }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.NotOnCanceled);
    }


    private void CancelTimeoutTask(string gameKey)
    {
        if (_gameTimeoutTokens.TryRemove(gameKey, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public (string Message, MessageComponent Components) GetRpsTimeoutDisplay(ulong messageId)
    {
        var dummyGame = new RpsGame(client.CurrentUser, client.CurrentUser, gameStatsService, logger);
        return ("Rock Paper Scissors game timed out.", dummyGame.GetButtons(messageId, true));
    }

    public (string Message, MessageComponent Components) GetTicTacToeTimeoutDisplay()
    {
        var cb = new ComponentBuilder();
        for (var i = 0; i < 3; i++) // 3 rows
        for (var j = 0; j < 3; j++) // 3 buttons per row
            cb.WithButton("\u200b", $"dummy_disabled_{i}_{j}", ButtonStyle.Secondary, disabled: true, row: i);

        return ("Tic Tac Toe game timed out.", cb.Build());
    }

    public (string Message, Embed? Embed, MessageComponent Components) GetHandCricketTimeoutDisplay(
        HandCricketGame game) =>
        ($"Hand Cricket game between {game.Player1.Mention} and {game.Player2.Mention} timed out due to inactivity.",
            null,
            new ComponentBuilder().Build());


    // --- RPS Game Management ---
    public GameCreationResult StartRpsGame(IUser player1, IUser? player2Input, ulong messageId, ulong guildId)
    {
        var player2 = player2Input ?? client.CurrentUser;

        if (player1.Id == player2.Id)
            return new GameCreationResult(GameCreationStatus.PlayersInvalid, "You can't play against yourself!");

        var game = new RpsGame(player1, player2, gameStatsService, logger);
        var gameKey = messageId.ToString();

        if (!_activeRpsGames.TryAdd(gameKey, game))
        {
            logger.LogError("[RPS] Failed to add game with MessageId {MessageId} to active games.", messageId);
            return new GameCreationResult(GameCreationStatus.InternalError,
                "Sorry, couldn't start the game due to an internal conflict.");
        }

        logger.LogInformation("[RPS] Started game ({MessageId}): {P1} vs {P2}", messageId, player1.Username,
            player2.Username);

        var initialMessageContent = $"{player1.Mention} vs {player2.Mention}: Choose your weapon!";
        var components = game.GetButtons(messageId);

        if (game.BothPlayersChosen)
        {
            logger.LogDebug("[RPS] Game {MessageId} involves bot(s) and choices are made, will resolve quickly.",
                messageId);
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                if (_activeRpsGames.TryGetValue(gameKey, out var immediateGame) && immediateGame.BothPlayersChosen)
                    await ProcessRpsEndOfGame(gameKey, immediateGame, guildId);
            });
        }
        else
        {
            StartTimeoutTask(gameKey, RpsGameTimeout, _activeRpsGames, "RPS");
        }

        return new GameCreationResult(GameCreationStatus.Success, InitialMessageContent: initialMessageContent,
            Components: components, GameKey: gameKey);
    }

    private async Task ProcessRpsEndOfGame(string gameKey, RpsGame game, ulong guildId)
    {
        if (_activeRpsGames.TryRemove(gameKey, out _))
        {
            CancelTimeoutTask(gameKey);
            logger.LogDebug("[RPS] Game {GameKey} ended. Result: {Result}", gameKey, game.GetResultMessage());

            if (guildId != 0)
                await game.RecordStatsIfApplicable(guildId);
            else if (!game.Player1.IsBot && !game.Player2.IsBot)
                logger.LogWarning("[RPS] Could not record stats for game {GameKey} (no valid GuildId provided).",
                    gameKey);
        }
    }


    public async Task<GameUpdateResult> ProcessRpsChoiceAsync(string gameKey, IUser user, RpsChoice choice,
        ulong guildId)
    {
        if (!_activeRpsGames.TryGetValue(gameKey, out var game))
            return new GameUpdateResult(GameUpdateStatus.GameNotFound,
                ErrorMessage: "This Rock Paper Scissors game has ended or is invalid.");

        if (user.Id != game.Player1.Id && user.Id != game.Player2.Id)
            return new GameUpdateResult(GameUpdateStatus.NotPlayerInGame, ErrorMessage: "This isn't your game!");

        if (game.HasChosen(user))
            return new GameUpdateResult(GameUpdateStatus.AlreadyChosen,
                ErrorMessage: "You have already made your choice!");

        if (choice == RpsChoice.None)
        {
            logger.LogWarning("[RPS] Invalid choice '{ChoiceString}' received for game {GameKey}", choice, gameKey);
            return new GameUpdateResult(GameUpdateStatus.InvalidMove, ErrorMessage: "Invalid choice selected.");
        }

        if (!game.MakeChoice(user, choice))
        {
            logger.LogWarning("[RPS] Failed to make choice for user {User} in game {GameKey}", user.Username, gameKey);
            return new GameUpdateResult(GameUpdateStatus.Error, ErrorMessage: "Failed to register your choice.");
        }

        var messageId = ulong.Parse(gameKey);

        if (game.BothPlayersChosen)
        {
            await ProcessRpsEndOfGame(gameKey, game, guildId);
            return new GameUpdateResult(
                GameUpdateStatus.GameOver,
                $"{game.Player1.Mention} vs {game.Player2.Mention}",
                game.GetResultEmbed(),
                game.GetButtons(messageId, true)
            );
        }

        var waitingFor = user.Id == game.Player1.Id ? game.Player2 : game.Player1;
        return new GameUpdateResult(
            GameUpdateStatus.Success,
            $"{game.Player1.Mention} vs {game.Player2.Mention}\n{user.Mention} has chosen! Waiting for {waitingFor.Mention}...",
            Components: game.GetButtons(messageId)
        );
    }

    public bool IsRpsGameActive(string gameKey) => _activeRpsGames.ContainsKey(gameKey);
    public RpsGame? GetRpsGame(string gameKey) => _activeRpsGames.GetValueOrDefault(gameKey);


    // --- Tic Tac Toe Game Management ---
    public GameCreationResult StartTicTacToeGame(IUser player1User, IUser? opponent)
    {
        var player2User = opponent == null || opponent.Id == player1User.Id ? client.CurrentUser : opponent;

        if (player1User.IsBot && player2User.IsBot)
            return new GameCreationResult(GameCreationStatus.PlayersInvalid,
                "Two bots can't play Tic Tac Toe against each other!");

        var gameId = Guid.NewGuid().ToString();
        IUser playerX, playerO;
        // Randomly assign X and O
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

        if (!_activeTicTacToeGames.TryAdd(gameId, game))
        {
            logger.LogError("[TTT] Failed to add new game with ID {GameId} to active games.", gameId);
            return new GameCreationResult(GameCreationStatus.InternalError,
                "Sorry, couldn't start the game due to an internal error.");
        }

        logger.LogInformation("[TTT] Started game {GameId}: {PlayerX} (X) vs {PlayerO} (O)", gameId, playerX.Username,
            playerO.Username);
        StartTimeoutTask(gameId, TicTacToeGameTimeout, _activeTicTacToeGames, "TicTacToe");

        var initialContent =
            $"## {playerX.Mention} (❌) vs {playerO.Mention} (⭕)\nIt's {game.CurrentPlayer.Mention}'s turn!";
        var components = game.GetMessageComponent();

        return new GameCreationResult(GameCreationStatus.Success, InitialMessageContent: initialContent,
            Components: components, GameKey: gameId);
    }

    public async Task<GameUpdateResult> ProcessTicTacToeMoveAsync(string gameId, IUser user, int row, int col,
        ulong guildId)
    {
        if (!_activeTicTacToeGames.TryGetValue(gameId, out var game))
            return new GameUpdateResult(GameUpdateStatus.GameNotFound,
                ErrorMessage: "This game session has expired or is invalid.");

        if (!game.IsPlayerInGame(user))
            return new GameUpdateResult(GameUpdateStatus.NotPlayerInGame,
                ErrorMessage: "You are not part of this game.");
        if (!game.IsPlayerTurn(user))
            return new GameUpdateResult(GameUpdateStatus.NotPlayerTurn,
                ErrorMessage: $"It's not your turn! Wait for {game.CurrentPlayer.Mention}.");

        if (!game.MakeMove(row, col))
            return new GameUpdateResult(GameUpdateStatus.InvalidMove, ErrorMessage: "That spot is already taken!");

        // Bot move logic
        if (game is { IsGameOver: false, CurrentPlayer.IsBot: true })
        {
            var botMoveCoords = await game.GetBestMoveAsync();
            if (botMoveCoords.HasValue)
                game.MakeMove(botMoveCoords.Value.row, botMoveCoords.Value.col);
            else
                logger.LogError("[TTT] Bot failed to determine a move in game {GameId} when it should have.",
                    game.GameId);
        }

        string messageContent;
        var components = game.GetMessageComponent(); // Get updated components

        if (game.IsGameOver)
        {
            if (guildId != 0) await game.RecordStatsIfApplicable(guildId);
            else if (!game.Player1.IsBot && !game.Player2.IsBot)
                logger.LogWarning("[TTT] Cannot record stats for game {GameId} (no valid GuildId).", game.GameId);

            _activeTicTacToeGames.TryRemove(game.GameId, out _);
            CancelTimeoutTask(game.GameId);
            logger.LogInformation("[TTT] Game {GameId} ended. Result: {Result}", game.GameId, game.Result);

            messageContent = game.Result switch
            {
                GameResultState.XWins =>
                    $"## {game.Player1.Mention} (❌) vs {game.Player2.Mention} (⭕)\n**{game.Player1.Mention} wins!**",
                GameResultState.OWins =>
                    $"## {game.Player1.Mention} (❌) vs {game.Player2.Mention} (⭕)\n**{game.Player2.Mention} wins!**",
                GameResultState.Tie => $"## {game.Player1.Mention} (❌) vs {game.Player2.Mention} (⭕)\n**It's a tie!**",
                _ => "Game over, but result is unclear."
            };
            return new GameUpdateResult(GameUpdateStatus.GameOver, messageContent, Components: components);
        }

        messageContent =
            $"## {game.Player1.Mention} (❌) vs {game.Player2.Mention} (⭕)\nIt's {game.CurrentPlayer.Mention}'s turn!";
        return new GameUpdateResult(GameUpdateStatus.Success, messageContent, Components: components);
    }

    public bool IsTicTacToeGameActive(string gameKey) => _activeTicTacToeGames.ContainsKey(gameKey);
    public TicTacToeGame? GetTicTacToeGame(string gameKey) => _activeTicTacToeGames.GetValueOrDefault(gameKey);


    // --- Hand Cricket Game Management ---
    public GameCreationResult StartHandCricketGame(IUser player1, IUser player2, ulong channelId, ulong guildId)
    {
        if (player1.Id == player2.Id)
            return new GameCreationResult(GameCreationStatus.PlayersInvalid, "You can't play against yourself!");
        if (player1.IsBot || player2.IsBot)
            return new GameCreationResult(GameCreationStatus.PlayersInvalid, "Bots cannot play Hand Cricket!");

        var game = new HandCricketGame(player1, player2, channelId, gameStatsService, logger);

        if (!_activeHandCricketGames.TryAdd(game.GameId, game))
        {
            logger.LogError("[HC] Failed to add game {GameId} to active dictionary.", game.GameId);
            return new GameCreationResult(GameCreationStatus.InternalError,
                "Failed to start the game due to a conflict. Please try again.");
        }

        logger.LogInformation("[HC] Started game {GameId}: {P1} vs {P2} in Channel {ChannelId}", game.GameId,
            player1.Username, player2.Username, channelId);
        StartTimeoutTask(game.GameId, HandCricketTimeout, _activeHandCricketGames, "HandCricket");

        return new GameCreationResult(
            GameCreationStatus.Success,
            InitialMessageContent: game.GetCurrentPrompt(),
            InitialEmbed: game.GetEmbed(),
            Components: game.GetComponents(),
            GameKey: game.GameId
        );
    }

    public async Task<GameUpdateResult> ProcessHandCricketActionAsync(string gameId, IUser user, string action,
        string data, ulong guildId)
    {
        if (!_activeHandCricketGames.TryGetValue(gameId, out var game))
            return new GameUpdateResult(GameUpdateStatus.GameNotFound,
                ErrorMessage: "This Hand Cricket game has ended or is invalid.");

        if (user.Id != game.Player1.Id && user.Id != game.Player2.Id)
            return new GameUpdateResult(GameUpdateStatus.NotPlayerInGame, ErrorMessage: "This isn't your game!");

        // Refresh timeout on interaction
        CancelTimeoutTask(gameId);
        StartTimeoutTask(gameId, HandCricketTimeout, _activeHandCricketGames, "HandCricket");

        string? userVisibleErrorMessage = null;

        // --- HandCricket Specific Logic based on `action` and `data` ---
        switch (action)
        {
            case "toss_eo":
                if (game.CurrentPhase == HandCricketPhase.TossSelectEvenOdd)
                {
                    var choice = data == "even" ? EvenOddChoice.Even : EvenOddChoice.Odd;
                    game.SetTossEvenOddPreference(user, choice);
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
                        if (!game.SetTossNumber(user, tossNum))
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
                    if (user.Id != game.TossWinner?.Id)
                    {
                        userVisibleErrorMessage = "Only the toss winner can choose.";
                    }
                    else
                    {
                        var choseBat = data == "bat";
                        game.SetBatOrBowlChoice(user, choseBat);
                    }
                }
                else
                {
                    userVisibleErrorMessage = "It's not time to choose Bat/Bowl.";
                }

                break;
            case "play_num":
                if (game.CurrentPhase == HandCricketPhase.Inning1Batting ||
                    game.CurrentPhase == HandCricketPhase.Inning2Batting)
                {
                    if (int.TryParse(data, out var gameNum))
                    {
                        if (!game.SetGameNumber(user, gameNum))
                        {
                            userVisibleErrorMessage =
                                "You've already selected a number for this turn, or it's not the right time/valid number.";
                        }
                        else
                        {
                            if (game.BothPlayersSelectedGameNumber())
                            {
                                var (_, gameOver) = game.ResolveTurn();
                                if (gameOver)
                                {
                                    var finalResultMessage = await game.GetResultStringAndRecordStats(guildId);
                                    _activeHandCricketGames.TryRemove(game.GameId, out _);
                                    CancelTimeoutTask(game.GameId);
                                    logger.LogInformation("[HC] Game {GameId} finished.", game.GameId);

                                    return new GameUpdateResult(GameUpdateStatus.GameOver,
                                        game.GetCurrentPrompt() + $"\n\n**{finalResultMessage}**",
                                        game.GetEmbed(),
                                        game.GetComponents());
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

        if (!string.IsNullOrEmpty(userVisibleErrorMessage))
            return new GameUpdateResult(GameUpdateStatus.Error, ErrorMessage: userVisibleErrorMessage);

        return new GameUpdateResult(GameUpdateStatus.Success,
            game.GetCurrentPrompt(),
            game.GetEmbed(),
            game.GetComponents());
    }

    public bool IsHandCricketGameActive(string gameKey) => _activeHandCricketGames.ContainsKey(gameKey);
    public HandCricketGame? GetHandCricketGame(string gameKey) => _activeHandCricketGames.GetValueOrDefault(gameKey);
}