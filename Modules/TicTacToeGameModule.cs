using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Text;

namespace Assistant.Net.Modules;

public class TicTacToeGameModule : InteractionModuleBase<SocketInteractionContext>
{
    public required InteractionService Commands { get; set; }
    private static readonly Dictionary<string, TicTacToeGame> GameStates = [];

    [SlashCommand("tictactoe", "Play a Game of Tic Tac Toe")]
    public async Task TicTacToeGameAsync(
               [Summary(description: "Select your Opponent")] SocketUser? opponent = null)
    {
        var player1 = Context.User;
        var player2 = opponent ?? Context.Client.CurrentUser;

        if (player1 == player2)
        {
            await RespondAsync("You can't play with yourself", ephemeral: true);
            return;
        }
        string gameId = Context.Interaction.Id.ToString();

        // randomly swap players
        if (new Random().Next(0, 2) == 1)
            (player1, player2) = (player2, player1);

        var game = new TicTacToeGame(player1, player2, gameId);
        GameStates[gameId] = game;


        await RespondAsync($"It is now {game.CurrentPlayer.Mention}'s turn", components: game.GetMessageComponent());
    }

    [ComponentInteraction("assistant:tictactoe:*:*")]
    public async Task TicTacToeButtonAsync(string gameId, string buttonName)
    {
        if (!GameStates.TryGetValue(gameId, out var game))
        {
            await RespondAsync("Game not found", ephemeral: true);
            return;
        }

        if (!game.IsPlayerInGame(Context.User))
        {
            await RespondAsync("You can start your own game with `/tictactoe` command", ephemeral: true);
            return;
        }

        if (!int.TryParse(buttonName, out int choice) || choice < 1 || choice > 9)
        {
            await RespondAsync("Invalid move", ephemeral: true);
            return;
        }

        if (game.CurrentPlayer != Context.User)
        {
            await RespondAsync("It's not your turn", ephemeral: true);
            return;
        }

        game.MakeMove(choice);
        Console.WriteLine(game);

        await DeferAsync();
        var winner = game.CheckWinner();

        if (winner != null || game.IsBoardFull)
        {
            string resultMessage = winner != null
                ? $"{winner.Mention} won! against {game.OtherPlayer(winner).Mention}"
                : $"{game.Player1.Mention} and {game.Player2.Mention} tied!";

            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = resultMessage;
                x.Components = game.GetMessageComponent(disabled: true);
            });

            GameStates.Remove(gameId);
        }
        else
        {
            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = $"It is now {game.CurrentPlayer.Mention}'s turn";
                x.Components = game.GetMessageComponent();
            });
        }
    }
}

public class TicTacToeGame(IUser player1, IUser player2, string gameId)
{
    private const int X = -1;
    private const int O = 1;

    public IUser Player1 { get; } = player1;
    public IUser Player2 { get; } = player2;
    public IUser CurrentPlayer { get; private set; } = player1;
    public int[,] Board { get; } = new int[3, 3];
    public int Moves { get; private set; }
    public bool IsBoardFull => Moves == 9;


    private readonly string _gameId = gameId;

    public bool IsPlayerInGame(IUser user)
    {
        return user == Player1 || user == Player2;
    }

    public MessageComponent GetMessageComponent(bool disabled = false)
    {
        var componentBuilder = new ComponentBuilder();

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                int position = i * 3 + j + 1;
                int value = Board[i, j];

                AddButton(componentBuilder, value, position, i, disabled);
            }
        }

        return componentBuilder.Build();
    }

    private void AddButton(ComponentBuilder componentBuilder, int value, int position, int row, bool disabled)
    {
        string emoji = value switch
        {
            0 => "\u200b",
            X => "❌",
            O => "⭕",
            _ => throw new InvalidOperationException("Invalid board value")
        };

        ButtonStyle style = value switch
        {
            0 => ButtonStyle.Secondary,
            X => ButtonStyle.Primary,
            O => ButtonStyle.Success,
            _ => throw new InvalidOperationException("Invalid board value")
        };

        bool _disabled = value != 0 || disabled;

        componentBuilder.WithButton(emoji, $"assistant:tictactoe:{_gameId}:{position}", style, row: row, disabled: _disabled);
    }

    public void MakeMove(int choice)
    {
        int row = (choice - 1) / 3;
        int col = (choice - 1) % 3;

        if (Board[row, col] != 0)
            throw new InvalidOperationException("Cell is already occupied");

        Board[row, col] = CurrentPlayer == Player1 ? X : O;
        CurrentPlayer = OtherPlayer(CurrentPlayer);
        Moves++;
    }

    public IUser OtherPlayer(IUser user)
    {
        return user == Player1 ? Player2 : Player1;
    }

    public IUser? CheckWinner()
    {
        // Check rows
        for (int i = 0; i < 3; i++)
        {
            int sum = Board[i, 0] + Board[i, 1] + Board[i, 2];
            if (sum == 3)
                return Player2;
            else if (sum == -3)
                return Player1;
        }

        // Check columns
        for (int i = 0; i < 3; i++)
        {
            int sum = Board[0, i] + Board[1, i] + Board[2, i];
            if (sum == 3)
                return Player2;
            else if (sum == -3)
                return Player1;
        }

        // Check diagonals
        int diag1Sum = Board[0, 0] + Board[1, 1] + Board[2, 2];
        int diag2Sum = Board[0, 2] + Board[1, 1] + Board[2, 0];

        if (diag1Sum == 3 || diag2Sum == 3)
            return Player2;
        else if (diag1Sum == -3 || diag2Sum == -3)
            return Player1;

        return null;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Player 1: {Player1.Username}");
        sb.AppendLine($"Player 2: {Player2.Username}");
        sb.AppendLine("-------------");

        for (int i = 0; i < 3; i++)
        {
            sb.Append("| ");
            for (int j = 0; j < 3; j++)
            {
                string value = Board[i, j] switch
                {
                    0 => "  ",
                    X => "X ",
                    O => "O ",
                    _ => throw new InvalidOperationException("Invalid board value")
                };
                sb.Append(value + "| ");
            }
            sb.AppendLine();
        }
        sb.AppendLine("-------------");
        return sb.ToString();
    }
}
