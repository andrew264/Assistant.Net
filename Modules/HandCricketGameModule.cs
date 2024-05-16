using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Text;

namespace Assistant.Net.Modules;

public class HandCricketGameModule : InteractionModuleBase<SocketInteractionContext>
{
    public required InteractionService Commands { get; set; }
    private static readonly Dictionary<string, HandCricketGame> GameStates = [];

    [SlashCommand("handcricket", "Play a Game of Hand Cricket")]
    public async Task HandCricketGameAsync(
                      [Summary(description: "Select your Opponent")] SocketUser opponent)
    {
        var player1 = Context.User;
        var player2 = opponent;

        if (player1 == player2)
        {
            await RespondAsync("You can't play with yourself", ephemeral: true);
            return;
        }
        string gameId = Context.Interaction.Id.ToString();

        var game = new HandCricketGame(player1, player2, gameId);
        GameStates[gameId] = game;

        await RespondAsync($"### {player1.Mention} vs {player2.Mention}\n- Choose **ODD** or **EVEN**", components: game.GetOddEvenComponent());
    }

    [ComponentInteraction("assistant:handcricket:*:odd_even:*")]
    public async Task HandCricketOddEvenAsync(string gameId, string choice)
    {
        if (!GameStates.TryGetValue(gameId, out var game))
        {
            await RespondAsync("Game not found", ephemeral: true);
            return;
        }

        if (!game.IsPlayerInGame(Context.User))
        {
            await RespondAsync("You can start your own game with `/handcricket` command", ephemeral: true);
            return;
        }

        if (choice != "odd" && choice != "even")
        {
            await RespondAsync("Invalid choice", ephemeral: true);
            return;
        }

        if (game.Phase != GamePhase.OddEven)
        {
            await RespondAsync("The choices have already been made", ephemeral: true);
            return;
        }

        game.SetPlayerChoiceEvenOdd(Context.User, choice == "odd" ? HandCricketChoice.Odd : HandCricketChoice.Even);

        await DeferAsync();
        await ModifyOriginalResponseAsync(x =>
        {
            x.Content = $"### {Context.User.Mention} has chosen {choice.ToUpper()}";
            x.Components = null;
        });
        await FollowupAsync($"### {game.Player1.Mention} vs {game.Player2.Mention}\n- Select a Number (Toss)", components: game.GetTossComponent());
    }

    [ComponentInteraction("assistant:handcricket:*:toss:*")]
    public async Task HandCricketTossAsync(string gameId, string choice)
    {
        if (!GameStates.TryGetValue(gameId, out var game))
        {
            await RespondAsync("Game not found", ephemeral: true);
            return;
        }

        if (!game.IsPlayerInGame(Context.User))
        {
            await RespondAsync("You can start your own game with `/handcricket` command", ephemeral: true);
            return;
        }

        if (game.Phase != GamePhase.Toss)
        {
            await RespondAsync("The toss has already been made", ephemeral: true);
            return;
        }

        if (game.HasPlayerMadeToss(Context.User))
        {
            await RespondAsync("You have already made your choice", ephemeral: true);
            return;
        }

        if (!int.TryParse(choice, out int toss) || toss < 1 || toss > 6)
        {
            await RespondAsync("Invalid choice", ephemeral: true);
            return;
        }

        game.SetPlayerToss(Context.User, toss);

        await DeferAsync();

        if (!game.IsTossComplete())
        {
            await ModifyOriginalResponseAsync(x =>
                {
                    x.Content = $"### {Context.User.Mention} has chosen.";
                }
                );
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine("### Toss Results");
            sb.AppendLine($"- {game.Player1.Mention} chose **{game.Player1Toss}**");
            sb.AppendLine($"- {game.Player2.Mention} chose **{game.Player2Toss}**");
            var total = game.Player1Toss + game.Player2Toss;
            var isEven = total % 2 == 0;
            sb.AppendLine($"- Total: **{total}** (**{(isEven ? "Even" : "Odd")}**)");
            sb.AppendLine($"### {game.TossWinner!.Mention} has won the toss!");

            await ModifyOriginalResponseAsync(x =>
                {
                    x.Content = sb.ToString();
                    x.Components = null;
                });

            await FollowupAsync($"### {game.TossWinner.Mention}\n- Choose to **BAT** or **BOWL**", components: game.GetBatOrBowlComponent());
        }
    }

    [ComponentInteraction("assistant:handcricket:*:bat_or_bowl:*")]
    public async Task HandCricketBatOrBowlAsync(string gameId, string choice)
    {
        if (!GameStates.TryGetValue(gameId, out var game))
        {
            await RespondAsync("Game not found", ephemeral: true);
            return;
        }

        if (!game.IsPlayerInGame(Context.User))
        {
            await RespondAsync("You can start your own game with `/handcricket` command", ephemeral: true);
            return;
        }

        if (choice != "bat" && choice != "bowl")
        {
            await RespondAsync("Invalid choice", ephemeral: true);
            return;
        }

        if (game.Phase != GamePhase.ChooseToBatOrBowl)
        {
            await RespondAsync("The choice has already been made", ephemeral: true);
            return;
        }

        if (Context.User != game.TossWinner)
        {
            await RespondAsync("You are not the toss winner", ephemeral: true);
            return;
        }

        var Choice = choice == "bat" ? HandCricketPhase.Batting : HandCricketPhase.Bowling;

        game.SetPlayerBatOrBowl(Context.User, Choice);

        await DeferAsync();
        await ModifyOriginalResponseAsync(x =>
        {
            x.Content = $"### {Context.User.Mention} has chosen to {choice.ToUpper()}";
            x.Components = null;
        });


        var sb = new StringBuilder();
        sb.AppendLine($"### Batting: {game.GetBatter().Mention}");
        sb.AppendLine($"### Bowling: {game.GetBowler().Mention}");
        sb.AppendLine($"### Score Board");
        sb.AppendLine($"- {game.Player1.Mention}: `{game.PlayerRuns[game.Player1]}`");
        sb.AppendLine($"- {game.Player2.Mention}: `{game.PlayerRuns[game.Player2]}`");

        await FollowupAsync(sb.ToString(), components: game.GetBattingComponent());

    }

    [ComponentInteraction("assistant:handcricket:*:batting:*")]
    public async Task HandCricketBattingAsync(string gameId, string choice)
    {
        if (!GameStates.TryGetValue(gameId, out var game))
        {
            await RespondAsync("Game not found", ephemeral: true);
            return;
        }

        if (!game.IsPlayerInGame(Context.User))
        {
            await RespondAsync("You can start your own game with `/handcricket` command", ephemeral: true);
            return;
        }

        if (game.Phase is not GamePhase.FirstInnings and not GamePhase.SecondInnings)
        {
            await RespondAsync("Invalid phase", ephemeral: true);
            return;
        }

        if (!int.TryParse(choice, out int number) || number < 1 || number > 6)
        {
            await RespondAsync("Invalid choice", ephemeral: true);
            return;
        }
        if (!game.IsValidTurn(Context.User))
        {
            await RespondAsync("Wait for your turn!", ephemeral: true);
            return;
        }
        var CurrentRuns = game.SetPlayerRuns(Context.User, number);

        await DeferAsync();
        if (game.Phase == GamePhase.GameOver)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Game Over");
            sb.AppendLine($"- {game.Player1.Mention} scored {game.PlayerRuns[game.Player1]}");
            sb.AppendLine($"- {game.Player2.Mention} scored {game.PlayerRuns[game.Player2]}");
            var winner = game.PlayerRuns[game.Player1] > game.PlayerRuns[game.Player2] ? game.Player1 : game.Player2;
            sb.AppendLine($"### {winner.Mention} has won the game!");

            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = sb.ToString();
                x.Components = null;
            });

            GameStates.Remove(gameId);
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine($"### Batting: {game.GetBatter().Mention}");
            sb.AppendLine($"### Bowling: {game.GetBowler().Mention}");
            sb.AppendLine($"### Score Board");
            sb.AppendLine($"- {game.Player1.Mention}: `{game.PlayerRuns[game.Player1]}`");
            sb.AppendLine($"- {game.Player2.Mention}: `{game.PlayerRuns[game.Player2]}`");
            if (CurrentRuns != null)
            {
                sb.AppendLine("### Previous Runs");
                sb.AppendLine($"- {game.GetBatter().Mention} selected {CurrentRuns[game.GetBatter()]}");
                sb.AppendLine($"- {game.GetBowler().Mention} selected {CurrentRuns[game.GetBowler()]}");
            }
            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = sb.ToString();
                x.Components = game.GetBattingComponent();
            });
        }
    }

}

class HandCricketGame
{
    public SocketUser Player1 { get; }
    public SocketUser Player2 { get; }
    public string GameId { get; }
    public GamePhase Phase { get; private set; } = GamePhase.OddEven;
    public HandCricketChoice? Player1Choice { get; private set; }
    public HandCricketChoice? Player2Choice { get; private set; }
    public int? Player1Toss { get; private set; }
    public int? Player2Toss { get; private set; }
    public SocketUser? TossWinner { get; private set; }

    public Dictionary<SocketUser, HandCricketPhase> PlayerPhase { get; private set; } = new();
    public Dictionary<SocketUser, int> PlayerRuns { get; private set; } = new();
    public Dictionary<SocketUser, int?> PlayerCurrentRun { get; private set; } = new();


    public HandCricketGame(SocketUser player1, SocketUser player2, string gameId)
    {
        Player1 = player1;
        Player2 = player2;
        GameId = gameId;
    }

    public bool IsPlayerInGame(SocketUser player) => player == Player1 || player == Player2;

    public SocketUser GetOddEvenChoiceMaker() => Player1Choice.HasValue ? Player1 : Player2;

    public SocketUser GetOtherPlayer(SocketUser player) => player == Player1 ? Player2 : Player1;

    public SocketUser GetBatter() => PlayerPhase[Player1] == HandCricketPhase.Batting ? Player1 : Player2;

    public SocketUser GetBowler() => PlayerPhase[Player1] == HandCricketPhase.Bowling ? Player1 : Player2;


    public void SetPlayerChoiceEvenOdd(SocketUser player, HandCricketChoice choice)
    {
        if (Phase != GamePhase.OddEven)
            throw new InvalidOperationException("Invalid phase");

        if (player == Player1)
            Player1Choice = choice;
        else if (player == Player2)
            Player2Choice = choice;

        Phase = GamePhase.Toss;
    }

    public bool HasPlayerMadeToss(SocketUser player) => player == Player1 ? Player1Toss.HasValue : Player2Toss.HasValue;

    public bool IsTossComplete() => Player1Toss.HasValue && Player2Toss.HasValue;

    public void SetPlayerToss(SocketUser player, int toss)
    {
        if (Phase != GamePhase.Toss)
            throw new InvalidOperationException("Invalid phase");

        if (player == Player1)
            Player1Toss = toss;
        else if (player == Player2)
            Player2Toss = toss;

        if (IsTossComplete())
        {
            var total = Player1Toss + Player2Toss;
            var isEven = total % 2 == 0;
            TossWinner = (isEven && Player1Choice == HandCricketChoice.Even) || (!isEven && Player1Choice == HandCricketChoice.Odd) ? Player1 : Player2;
            Phase = GamePhase.ChooseToBatOrBowl;
        }
    }

    public void SetPlayerBatOrBowl(SocketUser player, HandCricketPhase choice)
    {
        if (Phase != GamePhase.ChooseToBatOrBowl)
            throw new InvalidOperationException("Invalid phase");

        if (player != TossWinner)
            throw new InvalidOperationException("Only toss winner can choose to bat or bowl");

        PlayerPhase[player] = choice;
        PlayerPhase[GetOtherPlayer(player)] = choice == HandCricketPhase.Batting ? HandCricketPhase.Bowling : HandCricketPhase.Batting;
        Phase = GamePhase.FirstInnings;
        PlayerRuns[Player1] = 0;
        PlayerRuns[Player2] = 0;
        ResetCurrentRuns();
    }

    public bool IsValidTurn(SocketUser player)
    {
        return (Phase == GamePhase.FirstInnings || Phase == GamePhase.SecondInnings) && !PlayerCurrentRun[player].HasValue;
    }

    private Dictionary<SocketUser, int?> ResetCurrentRuns()
    {
        var runs = new Dictionary<SocketUser, int?>(PlayerCurrentRun);
        PlayerCurrentRun[Player1] = null;
        PlayerCurrentRun[Player2] = null;
        return runs;
    }

    private bool AreBothPlayersRunsSet() => PlayerCurrentRun[Player1].HasValue && PlayerCurrentRun[Player2].HasValue;

    private void SwitchRoles()
    {
        var batter = GetBatter();
        var bowler = GetBowler();
        PlayerPhase[batter] = HandCricketPhase.Bowling;
        PlayerPhase[bowler] = HandCricketPhase.Batting;
    }

    public Dictionary<SocketUser, int?>? SetPlayerRuns(SocketUser player, int runs)
    {
        if (Phase != GamePhase.FirstInnings && Phase != GamePhase.SecondInnings)
            throw new InvalidOperationException("Invalid phase");
        PlayerCurrentRun[player] = runs;
        if (AreBothPlayersRunsSet())
        {
            if (PlayerCurrentRun[Player1] == PlayerCurrentRun[Player2])
            {
                if (Phase == GamePhase.FirstInnings)
                {
                    Phase = GamePhase.SecondInnings;
                    SwitchRoles();
                    return ResetCurrentRuns();
                }
                else
                {
                    Phase = GamePhase.GameOver;
                }
            }
            else
            {
                var Batter = GetBatter();
                int CurrentRuns = PlayerCurrentRun[Batter] ?? 0;
                PlayerRuns[Batter] += CurrentRuns;
                return ResetCurrentRuns();
            }
        }
        return null;
    }

    public MessageComponent GetOddEvenComponent() => new ComponentBuilder()
        .WithButton("Odd", $"assistant:handcricket:{GameId}:odd_even:odd", ButtonStyle.Primary)
        .WithButton("Even", $"assistant:handcricket:{GameId}:odd_even:even", ButtonStyle.Success)
        .Build();

    public MessageComponent GetTossComponent()
    {
        var builder = new ComponentBuilder();
        int number = 1;
        for (int row = 0; row <= 1; row++)
            for (int col = 0; col <= 2; col++)
                builder.WithButton($"{number++}", $"assistant:handcricket:{GameId}:toss:{number - 1}", ButtonStyle.Secondary, row: row);
        return builder.Build();
    }

    public MessageComponent GetBatOrBowlComponent() => new ComponentBuilder()
        .WithButton("Bat", $"assistant:handcricket:{GameId}:bat_or_bowl:bat", ButtonStyle.Primary)
        .WithButton("Bowl", $"assistant:handcricket:{GameId}:bat_or_bowl:bowl", ButtonStyle.Success)
        .Build();

    public MessageComponent GetBattingComponent()
    {
        var builder = new ComponentBuilder();
        int number = 1;
        for (int row = 0; row <= 1; row++)
            for (int col = 0; col <= 2; col++)
                builder.WithButton($"{number++}", $"assistant:handcricket:{GameId}:batting:{number - 1}", ButtonStyle.Secondary, row: row);
        return builder.Build();
    }
}

enum HandCricketChoice
{
    Odd,
    Even
}

enum GamePhase
{
    OddEven,
    Toss,
    ChooseToBatOrBowl,
    FirstInnings,
    SecondInnings,
    GameOver
}

enum HandCricketPhase
{
    Batting,
    Bowling
}