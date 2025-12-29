using Assistant.Net.Services.Data;
using Discord;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Games.Logic;

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
    private const PlayerMarker Player1Marker = PlayerMarker.X;
    private const PlayerMarker Player2Marker = PlayerMarker.O;

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
    private PlayerMarker CurrentMarker => GetPlayerMarker(CurrentPlayer);

    private PlayerMarker[,] Board { get; } = new PlayerMarker[BoardSize, BoardSize];
    private int MovesMade { get; set; }
    private bool IsBoardFull => MovesMade >= BoardSize * BoardSize;
    public bool IsGameOver { get; private set; }
    public GameResultState Result { get; private set; } = GameResultState.None;

    public PlayerMarker GetMarkerAt(int row, int col) => Board[row, col];

    private void InitializeBoard()
    {
        for (var i = 0; i < BoardSize; i++)
        for (var j = 0; j < BoardSize; j++)
            Board[i, j] = PlayerMarker.None;
    }

    private PlayerMarker GetPlayerMarker(IUser user)
    {
        return user.Id switch
        {
            var id when id == Player1.Id => Player1Marker,
            var id when id == Player2.Id => Player2Marker,
            _ => PlayerMarker.None
        };
    }

    public bool IsPlayerTurn(IUser user) => !IsGameOver && user.Id == CurrentPlayer.Id;

    public bool IsPlayerInGame(IUser user) => user.Id == Player1.Id || user.Id == Player2.Id;

    private IUser OtherPlayer(IUser user) => user.Id == Player1.Id ? Player2 : Player1;

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

    private bool IsValidMove(int row, int col) =>
        row is >= 0 and < BoardSize &&
        col is >= 0 and < BoardSize &&
        Board[row, col] == PlayerMarker.None;

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
        return await Task.Run(FindBestMoveInternal).ConfigureAwait(false);
    }

    private (int row, int col)? FindBestMoveInternal()
    {
        var botMarker = CurrentMarker; // The marker the bot is currently playing
        var bestScore = botMarker == PlayerMarker.O ? int.MinValue : int.MaxValue; // O maximizes, X minimizes
        (int row, int col)? bestMove = null;
        var availableMoves = GetEmptyCells();

        // Handle first move randomly
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
                if (score <= bestScore) continue;
            }
            else // Bot is X (Minimizing)
            {
                if (score >= bestScore) continue;
            }

            bestScore = score;
            bestMove = move;
        }

        switch (bestMove)
        {
            case null when availableMoves.Count > 0:
                _logger.LogWarning("Minimax couldn't determine best move for Game {GameId}, falling back to random.",
                    GameId);
                bestMove = availableMoves[_random.Next(availableMoves.Count)];
                break;
            case null:
                _logger.LogError("Minimax failed to find a move for Game {GameId} when board is not full.", GameId);
                break;
        }


        return bestMove;
    }

    private static int RunMinimax(PlayerMarker[,] currentBoard, int currentMovesMade, PlayerMarker playerToSimulate)
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

            bestScore = playerToSimulate == PlayerMarker.O ? Math.Max(bestScore, score) : Math.Min(bestScore, score);
        }

        return bestScore;
    }

    private List<(int row, int col)> GetEmptyCells() => GetEmptyCells(Board);

    private static List<(int row, int col)> GetEmptyCells(PlayerMarker[,] boardState)
    {
        var cells = new List<(int row, int col)>();
        for (var i = 0; i < BoardSize; i++)
        for (var j = 0; j < BoardSize; j++)
            if (boardState[i, j] == PlayerMarker.None)
                cells.Add((i, j));

        return cells;
    }


    // --- Game Over Checks ---

    private void CheckForGameOver()
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
    private PlayerMarker CheckWinnerInternal() => CheckWinnerOnBoard(Board);

    // Checks winner on a board state
    private static PlayerMarker CheckWinnerOnBoard(PlayerMarker[,] boardState)
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
            await recordTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record game stats for game {GameId} in guild {GuildId}", GameId, guildId);
        }
    }
}