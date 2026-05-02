namespace Assistant.Net.Services.Games.Logic;

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

    private const PlayerMarker Player1Marker = PlayerMarker.X;
    private const PlayerMarker Player2Marker = PlayerMarker.O;

    private readonly Random _random = new();

    public TicTacToeGame(ulong player1Id, string player1Name, bool p1IsBot, ulong player2Id, string player2Name,
        bool p2IsBot, string gameId)
    {
        Player1Id = player1Id;
        Player1Name = player1Name;
        P1IsBot = p1IsBot;

        Player2Id = player2Id;
        Player2Name = player2Name;
        P2IsBot = p2IsBot;

        GameId = gameId;

        CurrentPlayerId = Player1Id;
        InitializeBoard();
    }

    public string GameId { get; }

    public ulong Player1Id { get; }
    public string Player1Name { get; }
    public bool P1IsBot { get; }

    public ulong Player2Id { get; }
    public string Player2Name { get; }
    public bool P2IsBot { get; }

    public ulong CurrentPlayerId { get; private set; }
    public bool CurrentPlayerIsBot => CurrentPlayerId == Player1Id ? P1IsBot : P2IsBot;
    public string CurrentPlayerMention => $"<@{CurrentPlayerId}>";

    private PlayerMarker CurrentMarker => GetPlayerMarker(CurrentPlayerId);

    private PlayerMarker[,] Board { get; } = new PlayerMarker[BoardSize, BoardSize];
    private int MovesMade { get; set; }
    private bool IsBoardFull => MovesMade >= BoardSize * BoardSize;
    public bool IsGameOver { get; private set; }
    public GameResultState Result { get; private set; } = GameResultState.None;
    public bool IsBotGuaranteedWin { get; private set; }
    public string? BotTaunt { get; private set; }


    public PlayerMarker GetMarkerAt(int row, int col) => Board[row, col];

    private void InitializeBoard()
    {
        for (var i = 0; i < BoardSize; i++)
        for (var j = 0; j < BoardSize; j++)
            Board[i, j] = PlayerMarker.None;
    }

    private PlayerMarker GetPlayerMarker(ulong userId)
    {
        return userId switch
        {
            var id when id == Player1Id => Player1Marker,
            var id when id == Player2Id => Player2Marker,
            _ => PlayerMarker.None
        };
    }

    public bool IsPlayerTurn(ulong userId) => !IsGameOver && userId == CurrentPlayerId;

    public bool IsPlayerInGame(ulong userId) => userId == Player1Id || userId == Player2Id;

    private ulong OtherPlayerId(ulong userId) => userId == Player1Id ? Player2Id : Player1Id;

    public bool MakeMove(int row, int col)
    {
        if (IsGameOver || !IsValidMove(row, col))
            return false;

        Board[row, col] = CurrentMarker;
        MovesMade++;
        CheckForGameOver();

        if (!IsGameOver)
            CurrentPlayerId = OtherPlayerId(CurrentPlayerId);

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

    public async Task<(int row, int col)?> GetBestMoveAsync()
    {
        if (!CurrentPlayerIsBot || IsGameOver)
            return null;

        var result = await Task.Run(FindBestMoveInternal).ConfigureAwait(false);

        if (result?.score is not { } score || MovesMade < 3) return result?.move;

        var botMarker = GetPlayerMarker(CurrentPlayerId);
        var botWinScore = botMarker == PlayerMarker.O ? 1 : -1;

        if (Math.Sign(score) != Math.Sign(botWinScore) || IsBotGuaranteedWin) return result.Value.move;

        IsBotGuaranteedWin = true;
        BotTaunt = GetRandomTaunt();
        return result.Value.move;
    }

    private string GetRandomTaunt()
    {
        string[] taunts =
        [
            "I've analyzed 14,000,605 futures. You lose in all of them. 🤖",
            "You've activated my trap card! 🃏",
            "Checkmate! Wait, wrong game... you still lost though!",
            "I'm about to end your whole career.",
            "Resistance is futile. 🛸",
            "Omae wa mou shindeiru.",
            "I've already won, you just don't know it yet.",
            "Well, get Forked!"
        ];
        return taunts[_random.Next(taunts.Length)];
    }

    private ((int row, int col) move, int? score)? FindBestMoveInternal()
    {
        var botMarker = CurrentMarker;
        var availableMoves = GetEmptyCells(Board);

        if (availableMoves.Count == BoardSize * BoardSize)
            return (availableMoves[_random.Next(availableMoves.Count)], null);

        var isMaximizing = botMarker == PlayerMarker.O;
        var bestScore = isMaximizing ? int.MinValue : int.MaxValue;
        var nextPlayer = isMaximizing ? PlayerMarker.X : PlayerMarker.O;

        var bestMoves = new List<(int row, int col)>();

        foreach (var move in availableMoves)
        {
            Board[move.row, move.col] = botMarker;
            var score = RunMinimax(Board, MovesMade + 1, nextPlayer, 1, int.MinValue, int.MaxValue);
            Board[move.row, move.col] = PlayerMarker.None;

            if (isMaximizing)
            {
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMoves.Clear();
                    bestMoves.Add(move);
                }
                else if (score == bestScore)
                {
                    bestMoves.Add(move);
                }
            }
            else
            {
                if (score < bestScore)
                {
                    bestScore = score;
                    bestMoves.Clear();
                    bestMoves.Add(move);
                }
                else if (score == bestScore)
                {
                    bestMoves.Add(move);
                }
            }
        }

        if (bestMoves.Count > 0)
            return (bestMoves[_random.Next(bestMoves.Count)], bestScore);

        if (availableMoves.Count > 0)
            return (availableMoves[_random.Next(availableMoves.Count)], null);

        return null;
    }

    private static int RunMinimax(
        PlayerMarker[,] board,
        int movesMade,
        PlayerMarker playerToSimulate,
        int depth,
        int alpha,
        int beta)
    {
        var winner = CheckWinnerOnBoard(board);
        if (winner != PlayerMarker.None)
            return (int)winner * (10 - depth);

        if (movesMade == BoardSize * BoardSize)
            return 0;

        var isMaximizing = playerToSimulate == PlayerMarker.O;
        var bestScore = isMaximizing ? int.MinValue : int.MaxValue;
        var nextPlayer = isMaximizing ? PlayerMarker.X : PlayerMarker.O;
        var availableMoves = GetEmptyCells(board);

        foreach (var move in availableMoves)
        {
            board[move.row, move.col] = playerToSimulate;

            var score = RunMinimax(board, movesMade + 1, nextPlayer, depth + 1, alpha, beta);

            board[move.row, move.col] = PlayerMarker.None;

            if (isMaximizing)
            {
                bestScore = Math.Max(bestScore, score);
                alpha = Math.Max(alpha, bestScore);
            }
            else
            {
                bestScore = Math.Min(bestScore, score);
                beta = Math.Min(beta, bestScore);
            }

            if (beta <= alpha) break;
        }

        return bestScore;
    }

    private static List<(int row, int col)> GetEmptyCells(PlayerMarker[,] boardState)
    {
        var cells = new List<(int row, int col)>(BoardSize * BoardSize);
        for (var i = 0; i < BoardSize; i++)
        for (var j = 0; j < BoardSize; j++)
            if (boardState[i, j] == PlayerMarker.None)
                cells.Add((i, j));

        return cells;
    }

    private void CheckForGameOver()
    {
        if (IsGameOver)
            return;

        var winner = CheckWinnerOnBoard(Board);

        if (winner != PlayerMarker.None)
        {
            IsGameOver = true;
            Result = winner == Player1Marker ? GameResultState.XWins : GameResultState.OWins;
        }
        else if (IsBoardFull ||
                 (!HasAnyWinningPath(Board, PlayerMarker.X) && !HasAnyWinningPath(Board, PlayerMarker.O)))
        {
            IsGameOver = true;
            Result = GameResultState.Tie;
        }
    }

    private static bool HasAnyWinningPath(PlayerMarker[,] board, PlayerMarker playerMarker)
    {
        var opponentMarker = playerMarker == PlayerMarker.X ? PlayerMarker.O : PlayerMarker.X;

        // rows
        for (var i = 0; i < BoardSize; i++)
            if (board[i, 0] != opponentMarker && board[i, 1] != opponentMarker && board[i, 2] != opponentMarker)
                return true;

        // columns
        for (var i = 0; i < BoardSize; i++)
            if (board[0, i] != opponentMarker && board[1, i] != opponentMarker && board[2, i] != opponentMarker)
                return true;

        // diagonals
        if (board[0, 0] != opponentMarker && board[1, 1] != opponentMarker && board[2, 2] != opponentMarker)
            return true;
        if (board[0, 2] != opponentMarker && board[1, 1] != opponentMarker && board[2, 0] != opponentMarker)
            return true;

        return false;
    }

    private static PlayerMarker CheckWinnerOnBoard(PlayerMarker[,] board)
    {
        for (var i = 0; i < BoardSize; i++)
        {
            var rowFirst = board[i, 0];
            if (rowFirst != PlayerMarker.None && rowFirst == board[i, 1] && rowFirst == board[i, 2])
                return rowFirst;

            var colFirst = board[0, i];
            if (colFirst != PlayerMarker.None && colFirst == board[1, i] && colFirst == board[2, i])
                return colFirst;
        }

        var center = board[1, 1];
        if (center == PlayerMarker.None) return PlayerMarker.None;
        if ((center == board[0, 0] && center == board[2, 2]) ||
            (center == board[0, 2] && center == board[2, 0])) return center;

        return PlayerMarker.None;
    }
}