using Assistant.Net.Modules.Games.Models.HandCricket;
using Assistant.Net.Services.Games;
using Discord;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Games.Logic;

public class HandCricketGame
{
    private const string CustomIdPrefix = "assistant:hc";
    private static readonly int[] TossNumbers = [1, 2, 0x3, 4, 5, 6];
    private static readonly int[] GameNumbers = [1, 2, 3, 4, 5, 6];
    private readonly GameStatsService? _gameStatsService;
    private readonly ILogger _logger;
    private string? _lastOutcomeMessage;

    public HandCricketGame(IUser player1, IUser player2, ulong channelId, GameStatsService? gameStatsService,
        ILogger logger)
    {
        GameId = Guid.NewGuid().ToString();
        Player1 = player1;
        Player2 = player2;
        InteractionChannelId = channelId;
        _gameStatsService = gameStatsService;
        _logger = logger;
        CurrentPhase = HandCricketPhase.TossSelectEvenOdd;
        CurrentBatter = Player1;
        CurrentBowler = Player2;
        LastInteractionTime = DateTime.UtcNow;
    }

    public string GameId { get; }
    public IUser Player1 { get; }
    public IUser Player2 { get; }
    public HandCricketPhase CurrentPhase { get; private set; }
    public ulong InteractionChannelId { get; }

    public TossNumberChoices CurrentTossChoices { get; } = new();
    public IUser? TossEvenOddChooser { get; private set; }
    public IUser? TossWinner { get; private set; }
    public bool? TossWinnerChoseBat { get; private set; }

    public IUser CurrentBatter { get; private set; }
    public IUser CurrentBowler { get; private set; }
    public int Player1Score { get; private set; }
    public int Player2Score { get; private set; }
    public GameNumberChoices CurrentTurnChoices { get; private set; } = new();
    public int CurrentInning { get; private set; }
    public GameNumberChoices PreviousTurnChoices { get; private set; } = new();

    public DateTime LastInteractionTime { get; private set; }

    public bool SetTossEvenOddPreference(IUser chooser, EvenOddChoice choice)
    {
        if (CurrentPhase != HandCricketPhase.TossSelectEvenOdd) return false;

        CurrentTossChoices.Player1ChoicePreference = chooser.Id == Player1.Id ? choice :
            choice == EvenOddChoice.Even ? EvenOddChoice.Odd : EvenOddChoice.Even;
        TossEvenOddChooser = chooser;
        CurrentPhase = HandCricketPhase.TossSelectNumber;
        LastInteractionTime = DateTime.UtcNow;
        _logger.LogDebug("[HC {GameId}] User {Chooser} set toss pref; P1 pref: {P1Pref}. Phase -> {Phase}", GameId,
            chooser.Username, CurrentTossChoices.Player1ChoicePreference, CurrentPhase);
        return true;
    }

    public bool SetTossNumber(IUser chooser, int number)
    {
        if (CurrentPhase != HandCricketPhase.TossSelectNumber) return false;
        if (!TossNumbers.Contains(number)) return false;

        var updated = false;
        if (chooser.Id == Player1.Id && CurrentTossChoices.Player1Number == null)
        {
            CurrentTossChoices.Player1Number = number;
            updated = true;
        }
        else if (chooser.Id == Player2.Id && CurrentTossChoices.Player2Number == null)
        {
            CurrentTossChoices.Player2Number = number;
            updated = true;
        }

        if (!updated) return updated;
        _logger.LogDebug("[HC {GameId}] User {Chooser} chose toss number {Number}", GameId, chooser.Username, number);
        LastInteractionTime = DateTime.UtcNow;
        return updated;
    }

    public bool ResolveToss()
    {
        if (CurrentPhase != HandCricketPhase.TossSelectNumber ||
            CurrentTossChoices.Player1Number == null ||
            CurrentTossChoices.Player2Number == null) return false;

        var sum = CurrentTossChoices.Player1Number.Value + CurrentTossChoices.Player2Number.Value;
        var isSumEven = sum % 2 == 0;
        var sumParity = isSumEven ? EvenOddChoice.Even : EvenOddChoice.Odd;

        var player1WinsToss = sumParity == CurrentTossChoices.Player1ChoicePreference;
        TossWinner = player1WinsToss ? Player1 : Player2;
        CurrentPhase = HandCricketPhase.TossSelectBatBowl;
        LastInteractionTime = DateTime.UtcNow;

        _lastOutcomeMessage = $"{Player1.Mention} selected {CurrentTossChoices.Player1Number}\n" +
                              $"{Player2.Mention} selected {CurrentTossChoices.Player2Number}\n" +
                              $"Sum is {sum} ({sumParity}).\n" +
                              $"{TossWinner.Mention} won the toss!";

        _logger.LogInformation("[HC {GameId}] Toss resolved. Winner: {Winner}. Phase -> {Phase}", GameId,
            TossWinner.Username, CurrentPhase);
        return true;
    }

    public bool SetBatOrBowlChoice(IUser chooser, bool choseBat)
    {
        if (CurrentPhase != HandCricketPhase.TossSelectBatBowl || chooser.Id != TossWinner?.Id) return false;

        TossWinnerChoseBat = choseBat;
        if (choseBat)
        {
            CurrentBatter = TossWinner;
            CurrentBowler = TossWinner.Id == Player1.Id ? Player2 : Player1;
        }
        else
        {
            CurrentBowler = TossWinner;
            CurrentBatter = TossWinner.Id == Player1.Id ? Player2 : Player1;
        }

        CurrentPhase = HandCricketPhase.Inning1Batting;
        CurrentInning = 0;
        LastInteractionTime = DateTime.UtcNow;
        _logger.LogInformation("[HC {GameId}] User {Chooser} chose to {Choice}. Batter: {Batter}. Phase -> {Phase}",
            GameId, chooser.Username, choseBat ? "Bat" : "Bowl", CurrentBatter.Username, CurrentPhase);
        return true;
    }

    public bool SetGameNumber(IUser chooser, int number)
    {
        if (CurrentPhase != HandCricketPhase.Inning1Batting &&
            CurrentPhase != HandCricketPhase.Inning2Batting) return false;
        if (!GameNumbers.Contains(number)) return false; // Validate number

        var updated = false;
        if (chooser.Id == Player1.Id && CurrentTurnChoices.Player1Number == null)
        {
            CurrentTurnChoices.Player1Number = number;
            updated = true;
        }
        else if (chooser.Id == Player2.Id && CurrentTurnChoices.Player2Number == null)
        {
            CurrentTurnChoices.Player2Number = number;
            updated = true;
        }

        if (!updated) return updated;
        _logger.LogDebug("[HC {GameId}] User {Chooser} chose game number {Number}", GameId, chooser.Username, number);
        LastInteractionTime = DateTime.UtcNow;
        return updated;
    }

    public bool BothPlayersSelectedGameNumber() => CurrentTurnChoices is
        { Player1Number: not null, Player2Number: not null };

    public (bool inningOver, bool gameOver) ResolveTurn()
    {
        if (!BothPlayersSelectedGameNumber()) return (false, false);
        _lastOutcomeMessage = null;

        var batterChoice = CurrentBatter.Id == Player1.Id
            ? CurrentTurnChoices.Player1Number!.Value
            : CurrentTurnChoices.Player2Number!.Value;
        var bowlerChoice = CurrentBowler.Id == Player1.Id
            ? CurrentTurnChoices.Player1Number!.Value
            : CurrentTurnChoices.Player2Number!.Value;

        var isOut = batterChoice == bowlerChoice;
        PreviousTurnChoices = CurrentTurnChoices;

        if (!isOut)
        {
            if (CurrentBatter.Id == Player1.Id) Player1Score += batterChoice;
            else Player2Score += batterChoice;
        }

        CurrentTurnChoices = new GameNumberChoices();

        if (CurrentInning == 0)
        {
            if (!isOut) return (false, false);
            CurrentInning = 1;
            (CurrentBatter, CurrentBowler) = (CurrentBowler, CurrentBatter);
            CurrentPhase = HandCricketPhase.Inning2Batting;
            _logger.LogInformation(
                "[HC {GameId}] Inning 1 over. Batter Out. Score: P1={P1S} P2={P2S}. New Batter: {Batter}. Phase -> {Phase}",
                GameId, Player1Score, Player2Score, CurrentBatter.Username, CurrentPhase);
            _lastOutcomeMessage = $"{CurrentBowler.Mention} is out! Target: {GetTargetScore()}";
            return (true, false);
        }

        if (isOut)
        {
            CurrentPhase = HandCricketPhase.GameOver;
            _logger.LogInformation("[HC {GameId}] Game Over. Batter Out (Inning 2). Final Score: P1={P1S} P2={P2S}",
                GameId, Player1Score, Player2Score);
            _lastOutcomeMessage = $"{CurrentBatter.Mention} is out!";
            return (true, true);
        }

        if ((Player1Score <= Player2Score || CurrentBatter.Id != Player1.Id) &&
            (Player2Score <= Player1Score || CurrentBatter.Id != Player2.Id))
            return (false, false);

        CurrentPhase = HandCricketPhase.GameOver;
        _logger.LogInformation("[HC {GameId}] Game Over. Target Chased. Final Score: P1={P1S} P2={P2S}", GameId,
            Player1Score, Player2Score);
        _lastOutcomeMessage = "Target chased!";
        return (false, true);
    }

    public int GetTargetScore()
    {
        if (CurrentInning == 0) return -1;
        return (CurrentBatter.Id == Player1.Id ? Player2Score : Player1Score) + 1;
    }

    public async Task<string> GetResultStringAndRecordStats(ulong guildId)
    {
        if (CurrentPhase != HandCricketPhase.GameOver) return "Game not over.";

        string resultMessage;
        ulong? winnerId;
        ulong? loserId;
        var isTie = false;

        if (Player1Score > Player2Score)
        {
            resultMessage = $"{Player1.Mention} wins!";
            winnerId = Player1.Id;
            loserId = Player2.Id;
        }
        else if (Player2Score > Player1Score)
        {
            resultMessage = $"{Player2.Mention} wins!";
            winnerId = Player2.Id;
            loserId = Player1.Id;
        }
        else
        {
            resultMessage = "It's a tie!";
            winnerId = Player1.Id;
            loserId = Player2.Id;
            isTie = true;
        }

        if (_gameStatsService != null)
            try
            {
                await _gameStatsService.RecordGameResultAsync(winnerId.Value, loserId.Value, guildId,
                    GameStatsService.HandCricketGameName, isTie).ConfigureAwait(false);
                _logger.LogInformation(
                    "[HC {GameId}] Recorded stats for Guild {GuildId}. Winner: {Winner}, Loser: {Loser}, Tie: {IsTie}",
                    GameId, guildId, winnerId, loserId, isTie);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HC {GameId}] Failed to record stats for Guild {GuildId}", GameId, guildId);
            }
        else if (_gameStatsService == null)
            _logger.LogWarning("[HC {GameId}] GameStatsService not available, skipping stat recording.", GameId);

        return resultMessage;
    }

    private string GetHumanPhaseName()
    {
        return CurrentPhase switch
        {
            HandCricketPhase.TossSelectEvenOdd => "Toss - Choose Even/Odd",
            HandCricketPhase.TossSelectNumber => "Toss - Choose Number",
            HandCricketPhase.TossSelectBatBowl => "Toss - Choose Bat/Bowl",
            HandCricketPhase.Inning1Batting => "Inning 1",
            HandCricketPhase.Inning2Batting => "Inning 2",
            HandCricketPhase.GameOver => "Game Over",
            _ => CurrentPhase.ToString()
        };
    }

    public Embed GetEmbed()
    {
        var embed = new EmbedBuilder()
            .WithTitle($"Hand Cricket: {Player1.Username} vs {Player2.Username}")
            .WithColor(Color.Orange)
            .WithFooter($"Game ID: {GameId[..5]} | Phase: {GetHumanPhaseName()}");

        embed.AddField($"{Player1.Username} ({(CurrentBatter.Id == Player1.Id ? "🏏" : "⚾")})", Player1Score.ToString(),
            true);
        embed.AddField($"{Player2.Username} ({(CurrentBatter.Id == Player2.Id ? "🏏" : "⚾")})", Player2Score.ToString(),
            true);

        var targetScore = GetTargetScore();
        if (targetScore > 0 && CurrentPhase == HandCricketPhase.Inning2Batting)
            embed.AddField("Target", targetScore.ToString(), true);
        else if (CurrentInning == 0 && CurrentPhase == HandCricketPhase.Inning1Batting)
            embed.AddField("Target", "Set in 2nd Inning", true);

        var p1Last = PreviousTurnChoices.Player1Number?.ToString() ?? "-";
        var p2Last = PreviousTurnChoices.Player2Number?.ToString() ?? "-";

        if (p1Last != "-" && p2Last != "-")
            embed.AddField("Last Selection", $"{Player1.Username}: {p1Last}\n{Player2.Username}: {p2Last}");


        return embed.Build();
    }

    public MessageComponent GetComponents()
    {
        var builder = new ComponentBuilder();
        LastInteractionTime = DateTime.UtcNow;

        switch (CurrentPhase)
        {
            case HandCricketPhase.TossSelectEvenOdd:
                builder.WithButton("Even", $"{CustomIdPrefix}:{GameId}:toss_eo:even", ButtonStyle.Success, row: 0);
                builder.WithButton("Odd", $"{CustomIdPrefix}:{GameId}:toss_eo:odd", ButtonStyle.Danger, row: 0);
                break;

            case HandCricketPhase.TossSelectNumber:
                AddNumberButtons(builder, TossNumbers, "toss_num");
                break;

            case HandCricketPhase.TossSelectBatBowl:
                builder.WithButton("Bat 🏏", $"{CustomIdPrefix}:{GameId}:batbowl:bat", row: 0);
                builder.WithButton("Bowl ⚾", $"{CustomIdPrefix}:{GameId}:batbowl:bowl", ButtonStyle.Success, row: 0);
                break;

            case HandCricketPhase.Inning1Batting:
            case HandCricketPhase.Inning2Batting:
                AddNumberButtons(builder, GameNumbers, "play_num");
                break;

            case HandCricketPhase.GameOver:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return builder.Build();
    }

    private void AddNumberButtons(ComponentBuilder builder, IEnumerable<int> numbers, string action)
    {
        var count = 0;
        var row = 0;
        foreach (var num in numbers)
        {
            builder.WithButton(num.ToString(), $"{CustomIdPrefix}:{GameId}:{action}:{num}", ButtonStyle.Secondary,
                row: row);
            count++;
            if (count % 5 == 0)
                row++;
        }
    }

    public string GetCurrentPrompt()
    {
        var outcome = _lastOutcomeMessage;
        _lastOutcomeMessage = null;

        string phasePrompt;
        switch (CurrentPhase)
        {
            case HandCricketPhase.TossSelectEvenOdd:
                phasePrompt = $"{Player1.Mention} / {Player2.Mention}, select Even or Odd for the toss.";
                break;
            case HandCricketPhase.TossSelectNumber:
                var tossPrefMsg = TossEvenOddChooser != null
                    ? $"{TossEvenOddChooser.Mention} chose `{(CurrentTossChoices.Player1ChoicePreference == EvenOddChoice.Even ? "Even" : "Odd")}` (P1 Pref)."
                    : "";
                var waitingForToss = "";
                if (CurrentTossChoices.Player1Number == null && CurrentTossChoices.Player2Number != null)
                    waitingForToss = Player1.Mention;
                else if (CurrentTossChoices.Player1Number != null && CurrentTossChoices.Player2Number == null)
                    waitingForToss = Player2.Mention;

                phasePrompt = $"{tossPrefMsg}\nSelect a number (1-6) for the toss." +
                              (string.IsNullOrEmpty(waitingForToss)
                                  ? ""
                                  : $" Waiting for {waitingForToss}...");
                break;
            case HandCricketPhase.TossSelectBatBowl:
                phasePrompt = $"{TossWinner?.Mention}, choose to Bat or Bowl.";
                break;
            case HandCricketPhase.Inning1Batting:
            case HandCricketPhase.Inning2Batting:
                var waitingForGame = "";
                if (CurrentTurnChoices.Player1Number == null && CurrentTurnChoices.Player2Number != null)
                    waitingForGame = Player1.Mention;
                else if (CurrentTurnChoices.Player1Number != null && CurrentTurnChoices.Player2Number == null)
                    waitingForGame = Player2.Mention;
                phasePrompt =
                    $"{CurrentBatter.Mention} is batting. {CurrentBowler.Mention} is bowling.\nSelect a number (1-6)." +
                    (string.IsNullOrEmpty(waitingForGame) ? "" : $" Waiting for {waitingForGame}...");
                break;
            case HandCricketPhase.GameOver:
                phasePrompt = "Game Over!";
                break;
            default:
                phasePrompt = "Hand Cricket";
                break;
        }

        return string.IsNullOrEmpty(outcome) ? phasePrompt : $"{outcome}\n{new string('-', 20)}\n{phasePrompt}";
    }
}