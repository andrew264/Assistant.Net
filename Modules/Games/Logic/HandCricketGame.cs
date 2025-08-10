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

    public HandCricketGame(IUser player1, IUser player2, GameStatsService? gameStatsService,
        ILogger logger)
    {
        GameId = Guid.NewGuid().ToString();
        Player1 = player1;
        Player2 = player2;
        _gameStatsService = gameStatsService;
        _logger = logger;
        CurrentPhase = HandCricketPhase.TossSelectEvenOdd;
        CurrentBatter = Player1;
        CurrentBowler = Player2;
    }

    public string GameId { get; }
    public IUser Player1 { get; }
    public IUser Player2 { get; }
    public HandCricketPhase CurrentPhase { get; private set; }

    public TossNumberChoices CurrentTossChoices { get; } = new();
    public IUser? TossWinner { get; private set; }

    private IUser CurrentBatter { get; set; }
    private IUser CurrentBowler { get; set; }
    private int Player1Score { get; set; }
    private int Player2Score { get; set; }
    private GameNumberChoices CurrentTurnChoices { get; set; } = new();
    private int CurrentInning { get; set; }

    public void SetTossEvenOddPreference(IUser chooser, EvenOddChoice choice)
    {
        if (CurrentPhase != HandCricketPhase.TossSelectEvenOdd) return;

        CurrentTossChoices.Player1ChoicePreference = chooser.Id == Player1.Id ? choice :
            choice == EvenOddChoice.Even ? EvenOddChoice.Odd : EvenOddChoice.Even;
        CurrentPhase = HandCricketPhase.TossSelectNumber;
        _logger.LogDebug("[HC {GameId}] User {Chooser} set toss pref; P1 pref: {P1Pref}. Phase -> {Phase}", GameId,
            chooser.Username, CurrentTossChoices.Player1ChoicePreference, CurrentPhase);
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
        return updated;
    }

    public void ResolveToss()
    {
        if (CurrentPhase != HandCricketPhase.TossSelectNumber ||
            CurrentTossChoices.Player1Number == null ||
            CurrentTossChoices.Player2Number == null)
            return;

        var sum = CurrentTossChoices.Player1Number.Value + CurrentTossChoices.Player2Number.Value;
        var isSumEven = sum % 2 == 0;
        var sumParity = isSumEven ? EvenOddChoice.Even : EvenOddChoice.Odd;

        var player1WinsToss = sumParity == CurrentTossChoices.Player1ChoicePreference;
        TossWinner = player1WinsToss ? Player1 : Player2;
        CurrentPhase = HandCricketPhase.TossSelectBatBowl;

        _lastOutcomeMessage = $"{Player1.Mention} selected {CurrentTossChoices.Player1Number}\n" +
                              $"{Player2.Mention} selected {CurrentTossChoices.Player2Number}\n" +
                              $"Sum is {sum} ({sumParity}).\n" +
                              $"{TossWinner.Mention} won the toss!";

        _logger.LogInformation("[HC {GameId}] Toss resolved. Winner: {Winner}. Phase -> {Phase}", GameId,
            TossWinner.Username, CurrentPhase);
    }

    public void SetBatOrBowlChoice(IUser chooser, bool choseBat)
    {
        if (CurrentPhase != HandCricketPhase.TossSelectBatBowl || chooser.Id != TossWinner?.Id) return;

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
        _logger.LogInformation("[HC {GameId}] User {Chooser} chose to {Choice}. Batter: {Batter}. Phase -> {Phase}",
            GameId, chooser.Username, choseBat ? "Bat" : "Bowl", CurrentBatter.Username, CurrentPhase);
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
        return updated;
    }

    public bool BothPlayersSelectedGameNumber() => CurrentTurnChoices is
        { Player1Number: not null, Player2Number: not null };

    public bool ResolveTurn()
    {
        if (!BothPlayersSelectedGameNumber()) return false;
        _lastOutcomeMessage = null;

        var batterChoice = CurrentBatter.Id == Player1.Id
            ? CurrentTurnChoices.Player1Number!.Value
            : CurrentTurnChoices.Player2Number!.Value;
        var bowlerChoice = CurrentBowler.Id == Player1.Id
            ? CurrentTurnChoices.Player1Number!.Value
            : CurrentTurnChoices.Player2Number!.Value;

        var isOut = batterChoice == bowlerChoice;

        if (!isOut)
        {
            if (CurrentBatter.Id == Player1.Id) Player1Score += batterChoice;
            else Player2Score += batterChoice;
        }

        CurrentTurnChoices = new GameNumberChoices();

        if (CurrentInning == 0)
        {
            if (!isOut) return false;
            CurrentInning = 1;
            (CurrentBatter, CurrentBowler) = (CurrentBowler, CurrentBatter);
            CurrentPhase = HandCricketPhase.Inning2Batting;
            _logger.LogInformation(
                "[HC {GameId}] Inning 1 over. Batter Out. Score: P1={P1S} P2={P2S}. New Batter: {Batter}. Phase -> {Phase}",
                GameId, Player1Score, Player2Score, CurrentBatter.Username, CurrentPhase);
            _lastOutcomeMessage = $"{CurrentBowler.Mention} is out! Target: {GetTargetScore()}";
            return false;
        }

        if (isOut)
        {
            CurrentPhase = HandCricketPhase.GameOver;
            _logger.LogInformation("[HC {GameId}] Game Over. Batter Out (Inning 2). Final Score: P1={P1S} P2={P2S}",
                GameId, Player1Score, Player2Score);
            _lastOutcomeMessage = $"{CurrentBatter.Mention} is out!";
            return true;
        }

        if ((Player1Score <= Player2Score || CurrentBatter.Id != Player1.Id) &&
            (Player2Score <= Player1Score || CurrentBatter.Id != Player2.Id))
            return false;

        CurrentPhase = HandCricketPhase.GameOver;
        _logger.LogInformation("[HC {GameId}] Game Over. Target Chased. Final Score: P1={P1S} P2={P2S}", GameId,
            Player1Score, Player2Score);
        _lastOutcomeMessage = "Target chased!";
        return true;
    }

    private int GetTargetScore()
    {
        if (CurrentInning == 0) return -1;
        return (CurrentBatter.Id == Player1.Id ? Player2Score : Player1Score) + 1;
    }

    public async Task GetResultStringAndRecordStats(ulong guildId)
    {
        if (CurrentPhase != HandCricketPhase.GameOver) return;

        ulong? winnerId;
        ulong? loserId;
        var isTie = false;

        if (Player1Score > Player2Score)
        {
            winnerId = Player1.Id;
            loserId = Player2.Id;
        }
        else if (Player2Score > Player1Score)
        {
            winnerId = Player2.Id;
            loserId = Player1.Id;
        }
        else
        {
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

    public MessageComponent BuildGameComponent()
    {
        var builder = new ComponentBuilderV2();
        var container = new ContainerBuilder();

        // --- Header ---
        container.AddComponent(new TextDisplayBuilder($"# Hand Cricket: {Player1.Username} vs {Player2.Username}"));
        container.AddComponent(new TextDisplayBuilder($"*Phase: {GetHumanPhaseName()}*"));
        container.WithSeparator();

        // --- Scoreboard ---
        var p1Role = CurrentBatter.Id == Player1.Id ? "🏏" : "⚾";
        var p2Role = CurrentBatter.Id == Player2.Id ? "🏏" : "⚾";
        container.AddComponent(new TextDisplayBuilder($"**{Player1.Username} {p1Role}:** {Player1Score}"));
        container.AddComponent(new TextDisplayBuilder($"**{Player2.Username} {p2Role}:** {Player2Score}"));

        var targetScore = GetTargetScore();
        if (targetScore > 0 && CurrentPhase == HandCricketPhase.Inning2Batting)
            container.AddComponent(new TextDisplayBuilder($"**Target:** {targetScore}"));

        // --- Last Turn Info & Current Prompt ---
        var prompt = GetCurrentPrompt();
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            container.WithSeparator();
            container.WithTextDisplay(new TextDisplayBuilder(prompt));
        }

        // --- Buttons ---
        var buttonActionRows = GetActionRowsForCurrentPhase();
        foreach (var row in buttonActionRows) container.WithActionRow(row);

        // --- Footer ---
        container.WithSeparator();
        container.WithTextDisplay(new TextDisplayBuilder($"*Game ID: {GameId[..8]}*"));

        builder.WithContainer(container);
        return builder.Build();
    }


    private string GetCurrentPrompt()
    {
        var outcome = _lastOutcomeMessage;
        _lastOutcomeMessage = null; // Consume the message

        var phasePrompt = CurrentPhase switch
        {
            HandCricketPhase.TossSelectEvenOdd =>
                $"{Player1.Mention} / {Player2.Mention}, select Even or Odd for the toss.",
            HandCricketPhase.TossSelectNumber => GetTossNumberPrompt(),
            HandCricketPhase.TossSelectBatBowl => $"{TossWinner?.Mention}, choose to Bat or Bowl.",
            HandCricketPhase.Inning1Batting or HandCricketPhase.Inning2Batting => GetGameNumberPrompt(),
            HandCricketPhase.GameOver => "Game Over!",
            _ => "Hand Cricket"
        };

        return string.IsNullOrWhiteSpace(outcome) ? phasePrompt : $"**{outcome}**\n\n{phasePrompt}";
    }

    private string GetTossNumberPrompt()
    {
        var waitingFor = "";
        if (CurrentTossChoices.Player1Number == null && CurrentTossChoices.Player2Number != null)
            waitingFor = Player1.Mention;
        else if (CurrentTossChoices is { Player1Number: not null, Player2Number: null })
            waitingFor = Player2.Mention;

        var prompt = "Select a number (1-6) for the toss.";
        if (!string.IsNullOrEmpty(waitingFor)) prompt += $" Waiting for {waitingFor}...";

        return prompt;
    }

    private string GetGameNumberPrompt()
    {
        var waitingFor = "";
        if (CurrentTurnChoices.Player1Number == null && CurrentTurnChoices.Player2Number != null)
            waitingFor = Player1.Mention;
        else if (CurrentTurnChoices is { Player1Number: not null, Player2Number: null })
            waitingFor = Player2.Mention;

        var prompt = $"{CurrentBatter.Mention} is batting. {CurrentBowler.Mention} is bowling.\nSelect a number (1-6).";
        if (!string.IsNullOrEmpty(waitingFor)) prompt += $" Waiting for {waitingFor}...";

        return prompt;
    }

    private List<ActionRowBuilder> GetActionRowsForCurrentPhase()
    {
        var rows = new List<ActionRowBuilder>();

        switch (CurrentPhase)
        {
            case HandCricketPhase.TossSelectEvenOdd:
                rows.Add(new ActionRowBuilder()
                    .WithButton("Even", $"{CustomIdPrefix}:{GameId}:toss_eo:even", ButtonStyle.Success)
                    .WithButton("Odd", $"{CustomIdPrefix}:{GameId}:toss_eo:odd", ButtonStyle.Danger));
                break;

            case HandCricketPhase.TossSelectNumber:
                rows.AddRange(CreateNumberButtonRows(TossNumbers, "toss_num"));
                break;

            case HandCricketPhase.TossSelectBatBowl:
                rows.Add(new ActionRowBuilder()
                    .WithButton("Bat 🏏", $"{CustomIdPrefix}:{GameId}:batbowl:bat")
                    .WithButton("Bowl ⚾", $"{CustomIdPrefix}:{GameId}:batbowl:bowl", ButtonStyle.Success));
                break;

            case HandCricketPhase.Inning1Batting:
            case HandCricketPhase.Inning2Batting:
                rows.AddRange(CreateNumberButtonRows(GameNumbers, "play_num"));
                break;

            case HandCricketPhase.GameOver:
                // No buttons for game over state
                break;
        }

        return rows;
    }

    private List<ActionRowBuilder> CreateNumberButtonRows(IEnumerable<int> numbers, string action)
    {
        var actionRows = new List<ActionRowBuilder>();
        var currentRow = new ActionRowBuilder();
        var count = 0;

        foreach (var num in numbers)
        {
            if (count > 0 && count % 5 == 0)
            {
                actionRows.Add(currentRow);
                currentRow = new ActionRowBuilder();
            }

            currentRow.WithButton(num.ToString(), $"{CustomIdPrefix}:{GameId}:{action}:{num}", ButtonStyle.Secondary);
            count++;
        }

        if (currentRow.Components.Count > 0) actionRows.Add(currentRow);

        return actionRows;
    }
}