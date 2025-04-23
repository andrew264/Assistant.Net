using Assistant.Net.Services;
using Discord;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Games;

public enum PlayerMarker
{
    None = 0,
    X = -1,
    O = 1
}

public enum GameResultState
{
    None,
    XWins,
    OWins,
    Tie
}

public class TicTacToeGame
{
    private const int BoardSize = 3;

    // Player1 is always X, Player2 is always O
    public const PlayerMarker Player1Marker = PlayerMarker.X;
    public const PlayerMarker Player2Marker = PlayerMarker.O;

    private readonly GameStatsService? _gameStatsService;
    private readonly ILogger _logger;
    private readonly Random _random = new();

    public TicTacToeGame(IUser player1, IUser player2, string gameId, GameStatsService? gameStatsService,
        ILogger logger)
    {
        Player1 = player1;
        Player2 = player2;
        GameId = gameId;
        _gameStatsService = gameStatsService;
        _logger = logger;

        CurrentPlayer = Player1;
        InitializeBoard();
    }

    public string GameId { get; }
    public IUser Player1 { get; }
    public IUser Player2 { get; }
    public IUser CurrentPlayer { get; private set; }
    public PlayerMarker CurrentMarker => GetPlayerMarker(CurrentPlayer);

    public PlayerMarker[,] Board { get; } = new PlayerMarker[BoardSize, BoardSize];
    public int MovesMade { get; private set; }
    public bool IsBoardFull => MovesMade >= BoardSize * BoardSize;
    public bool IsGameOver { get; private set; }
    public GameResultState Result { get; private set; } = GameResultState.None;

    private void InitializeBoard()
    {
        for (var i = 0; i < BoardSize; i++)
        for (var j = 0; j < BoardSize; j++)
            Board[i, j] = PlayerMarker.None;
    }

    public PlayerMarker GetPlayerMarker(IUser user)
    {
        return user.Id switch
        {
            var id when id == Player1.Id => Player1Marker,
            var id when id == Player2.Id => Player2Marker,
            _ => PlayerMarker.None
        };
    }

    public IUser GetUserFromMarker(PlayerMarker marker)
    {
        return marker switch
        {
            PlayerMarker.X => Player1,
            PlayerMarker.O => Player2,
            _ => throw new ArgumentException("Invalid marker", nameof(marker))
        };
    }

    public bool IsPlayerTurn(IUser user)
    {
        return !IsGameOver && user.Id == CurrentPlayer.Id;
    }

    public bool IsPlayerInGame(IUser user)
    {
        return user.Id == Player1.Id || user.Id == Player2.Id;
    }

    public IUser OtherPlayer(IUser user)
    {
        return user.Id == Player1.Id ? Player2 : Player1;
    }

    public bool MakeMove(int row, int col)
    {
        if (IsGameOver || !IsValidMove(row, col))
            return false;

        Board[row, col] = CurrentMarker;
        MovesMade++;
        CheckForGameOver();

        if (!IsGameOver)
            CurrentPlayer = OtherPlayer(CurrentPlayer);

        return true;
    }

    private bool IsValidMove(int row, int col)
    {
        return row is >= 0 and < BoardSize &&
               col is >= 0 and < BoardSize &&
               Board[row, col] == PlayerMarker.None;
    }

    public static (int row, int col) IndexToCoords(int index)
    {
        if (index is < 1 or > 9)
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be between 1 and 9.");

        return ((index - 1) / BoardSize, (index - 1) % BoardSize);
    }

    // --- Minimax Implementation ---

    public async Task<(int row, int col)?> GetBestMoveAsync()
    {
        if (!CurrentPlayer.IsBot || IsGameOver)
            return null;

        // Use Task.Run to avoid blocking the gateway thread if minimax takes time,
        // although for Tic Tac Toe it's very fast.
        return await Task.Run(FindBestMoveInternal);
    }

    private (int row, int col)? FindBestMoveInternal()
    {
        var botMarker = CurrentMarker; // The marker the bot is currently playing
        var bestScore = botMarker == PlayerMarker.O ? int.MinValue : int.MaxValue; // O maximizes, X minimizes
        (int row, int col)? bestMove = null;
        var availableMoves = GetEmptyCells();

        // Handle first move randomly for variety and speed
        if (availableMoves.Count == BoardSize * BoardSize) return availableMoves[_random.Next(availableMoves.Count)];

        foreach (var move in availableMoves)
        {
            Board[move.row, move.col] = botMarker;
            MovesMade++; // Temporarily increment

            var score = RunMinimax(Board, MovesMade,
                botMarker == PlayerMarker.O ? PlayerMarker.X : PlayerMarker.O); // Score from opponent's perspective

            Board[move.row, move.col] = PlayerMarker.None; // Undo move
            MovesMade--; // Decrement back

            if (botMarker == PlayerMarker.O) // Bot is O (Maximizing)
            {
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                }
            }
            else // Bot is X (Minimizing)
            {
                if (score < bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                }
            }
        }

        if (!bestMove.HasValue && availableMoves.Count > 0)
        {
            _logger.LogWarning("Minimax couldn't determine best move for Game {GameId}, falling back to random.",
                GameId);
            bestMove = availableMoves[_random.Next(availableMoves.Count)]; // Fallback if something goes wrong
        }
        else if (!bestMove.HasValue)
        {
            _logger.LogError("Minimax failed to find a move for Game {GameId} when board is not full.", GameId);
        }


        return bestMove;
    }

    private int RunMinimax(PlayerMarker[,] currentBoard, int currentMovesMade, PlayerMarker playerToSimulate)
    {
        var winner = CheckWinnerOnBoard(currentBoard);
        if (winner != PlayerMarker.None) return (int)winner;
        if (currentMovesMade == BoardSize * BoardSize) return 0;

        var bestScore = playerToSimulate == PlayerMarker.O ? int.MinValue : int.MaxValue;
        var availableMoves = GetEmptyCells(currentBoard);

        foreach (var move in availableMoves)
        {
            currentBoard[move.row, move.col] = playerToSimulate;
            var score = RunMinimax(currentBoard, currentMovesMade + 1,
                playerToSimulate == PlayerMarker.O ? PlayerMarker.X : PlayerMarker.O);
            currentBoard[move.row, move.col] = PlayerMarker.None;

            if (playerToSimulate == PlayerMarker.O)
                bestScore = Math.Max(bestScore, score);
            else
                bestScore = Math.Min(bestScore, score);
        }

        return bestScore;
    }

    private List<(int row, int col)> GetEmptyCells()
    {
        return GetEmptyCells(Board);
    }

    private List<(int row, int col)> GetEmptyCells(PlayerMarker[,] boardState)
    {
        var cells = new List<(int row, int col)>();
        for (var i = 0; i < BoardSize; i++)
        for (var j = 0; j < BoardSize; j++)
            if (boardState[i, j] == PlayerMarker.None)
                cells.Add((i, j));

        return cells;
    }


    // --- Game Over Checks ---

    public void CheckForGameOver()
    {
        if (IsGameOver)
            return;

        var winner = CheckWinnerInternal();

        if (winner != PlayerMarker.None)
        {
            IsGameOver = true;
            Result = winner == Player1Marker ? GameResultState.XWins : GameResultState.OWins;
        }
        else if (IsBoardFull)
        {
            IsGameOver = true;
            Result = GameResultState.Tie;
        }
    }

    // Checks winner on the *current* game board
    private PlayerMarker CheckWinnerInternal()
    {
        return CheckWinnerOnBoard(Board);
    }

    // Checks winner on a board state
    private PlayerMarker CheckWinnerOnBoard(PlayerMarker[,] boardState)
    {
        // Check rows and columns
        for (var i = 0; i < BoardSize; i++)
        {
            if (boardState[i, 0] != PlayerMarker.None && boardState[i, 0] == boardState[i, 1] &&
                boardState[i, 1] == boardState[i, 2]) return boardState[i, 0];
            if (boardState[0, i] != PlayerMarker.None && boardState[0, i] == boardState[1, i] &&
                boardState[1, i] == boardState[2, i]) return boardState[0, i];
        }

        // Check diagonals
        if (boardState[0, 0] != PlayerMarker.None && boardState[0, 0] == boardState[1, 1] &&
            boardState[1, 1] == boardState[2, 2]) return boardState[0, 0];
        if (boardState[0, 2] != PlayerMarker.None && boardState[0, 2] == boardState[1, 1] &&
            boardState[1, 1] == boardState[2, 0]) return boardState[0, 2];

        // No winner found
        return PlayerMarker.None;
    }

    // --- Stat Recording ---
    public async Task RecordStatsIfApplicable(ulong guildId)
    {
        if (!IsGameOver || _gameStatsService == null || Player1.IsBot || Player2.IsBot)
            return;

        // Ensure Player1 is X, Player2 is O
        var playerXId = Player1.Id;
        var playerOId = Player2.Id;

        try
        {
            var recordTask = Result switch
            {
                // Player X (Player1) wins
                GameResultState.XWins => _gameStatsService.RecordGameResultAsync(playerXId, playerOId, guildId,
                    GameStatsService.TicTacToeGameName),
                // Player O (Player2) wins
                GameResultState.OWins => _gameStatsService.RecordGameResultAsync(playerOId, playerXId, guildId,
                    GameStatsService.TicTacToeGameName),
                // Tie
                GameResultState.Tie => _gameStatsService.RecordGameResultAsync(playerXId, playerOId, guildId,
                    GameStatsService.TicTacToeGameName, true),
                _ => Task.CompletedTask
            };
            await recordTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record game stats for game {GameId} in guild {GuildId}", GameId, guildId);
        }
    }

    // --- Message Component Generation ---
    public MessageComponent GetMessageComponent()
    {
        var builder = new ComponentBuilder();
        var disableAll = IsGameOver;

        for (var row = 0; row < BoardSize; row++)
        for (var col = 0; col < BoardSize; col++)
        {
            var index = row * BoardSize + col + 1;
            var marker = Board[row, col];

            var label = marker switch
            {
                PlayerMarker.None => "\u200b",
                PlayerMarker.X => "❌",
                PlayerMarker.O => "⭕",
                _ => "?"
            };

            var style = marker switch
            {
                PlayerMarker.X => ButtonStyle.Primary,
                PlayerMarker.O => ButtonStyle.Success,
                _ => ButtonStyle.Secondary
            };

            // Disable button if game over OR cell is already taken
            var disabled = disableAll || marker != PlayerMarker.None;

            builder.WithButton(label, $"assistant:tictactoe:{GameId}:{index}", style, row: row, disabled: disabled);
        }

        return builder.Build();
    }
}