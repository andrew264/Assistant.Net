using Assistant.Net.Services.Games.Models;

namespace Assistant.Net.Services.Games.Logic;

public class HandCricketGame
{
    public static readonly int[] TossNumbers = [1, 2, 3, 4, 5, 6];
    public static readonly int[] GameNumbers = [1, 2, 3, 4, 5, 6];
    private string? _lastOutcomeMessage;

    public HandCricketGame(ulong player1Id, string player1Name, ulong player2Id, string player2Name)
    {
        GameId = Guid.NewGuid().ToString();
        Player1Id = player1Id;
        Player1Name = player1Name;
        Player2Id = player2Id;
        Player2Name = player2Name;

        CurrentPhase = HandCricketPhase.TossSelectEvenOdd;
        CurrentBatterId = Player1Id;
        CurrentBowlerId = Player2Id;
    }

    public string GameId { get; }
    public ulong Player1Id { get; }
    public string Player1Name { get; }
    public ulong Player2Id { get; }
    public string Player2Name { get; }

    public HandCricketPhase CurrentPhase { get; private set; }

    public TossNumberChoices CurrentTossChoices { get; } = new();
    public ulong? TossWinnerId { get; private set; }

    public ulong CurrentBatterId { get; private set; }
    public ulong CurrentBowlerId { get; private set; }

    public int Player1Score { get; private set; }
    public int Player2Score { get; private set; }

    private GameNumberChoices CurrentTurnChoices { get; set; } = new();
    private int CurrentInning { get; set; }

    public void SetTossEvenOddPreference(ulong chooserId, EvenOddChoice choice)
    {
        if (CurrentPhase != HandCricketPhase.TossSelectEvenOdd) return;

        CurrentTossChoices.Player1ChoicePreference = chooserId == Player1Id ? choice :
            choice == EvenOddChoice.Even ? EvenOddChoice.Odd : EvenOddChoice.Even;
        CurrentPhase = HandCricketPhase.TossSelectNumber;
    }

    public bool SetTossNumber(ulong chooserId, int number)
    {
        if (CurrentPhase != HandCricketPhase.TossSelectNumber) return false;
        if (!TossNumbers.Contains(number)) return false;

        var updated = false;
        if (chooserId == Player1Id && CurrentTossChoices.Player1Number == null)
        {
            CurrentTossChoices.Player1Number = number;
            updated = true;
        }
        else if (chooserId == Player2Id && CurrentTossChoices.Player2Number == null)
        {
            CurrentTossChoices.Player2Number = number;
            updated = true;
        }

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
        TossWinnerId = player1WinsToss ? Player1Id : Player2Id;
        CurrentPhase = HandCricketPhase.TossSelectBatBowl;

        _lastOutcomeMessage = $"<@{Player1Id}> selected {CurrentTossChoices.Player1Number}\n" +
                              $"<@{Player2Id}> selected {CurrentTossChoices.Player2Number}\n" +
                              $"Sum is {sum} ({sumParity}).\n" +
                              $"<@{TossWinnerId}> won the toss!";
    }

    public void SetBatOrBowlChoice(ulong chooserId, bool choseBat)
    {
        if (CurrentPhase != HandCricketPhase.TossSelectBatBowl || chooserId != TossWinnerId) return;

        if (choseBat)
        {
            CurrentBatterId = TossWinnerId.Value;
            CurrentBowlerId = TossWinnerId.Value == Player1Id ? Player2Id : Player1Id;
        }
        else
        {
            CurrentBowlerId = TossWinnerId.Value;
            CurrentBatterId = TossWinnerId.Value == Player1Id ? Player2Id : Player1Id;
        }

        CurrentPhase = HandCricketPhase.Inning1Batting;
        CurrentInning = 0;
    }

    public bool SetGameNumber(ulong chooserId, int number)
    {
        if (CurrentPhase != HandCricketPhase.Inning1Batting &&
            CurrentPhase != HandCricketPhase.Inning2Batting) return false;
        if (!GameNumbers.Contains(number)) return false; // Validate number

        var updated = false;
        if (chooserId == Player1Id && CurrentTurnChoices.Player1Number == null)
        {
            CurrentTurnChoices.Player1Number = number;
            updated = true;
        }
        else if (chooserId == Player2Id && CurrentTurnChoices.Player2Number == null)
        {
            CurrentTurnChoices.Player2Number = number;
            updated = true;
        }

        return updated;
    }

    public bool BothPlayersSelectedGameNumber() => CurrentTurnChoices is
        { Player1Number: not null, Player2Number: not null };

    public bool ResolveTurn()
    {
        if (!BothPlayersSelectedGameNumber()) return false;
        _lastOutcomeMessage = null;

        var batterChoice = CurrentBatterId == Player1Id
            ? CurrentTurnChoices.Player1Number!.Value
            : CurrentTurnChoices.Player2Number!.Value;
        var bowlerChoice = CurrentBowlerId == Player1Id
            ? CurrentTurnChoices.Player1Number!.Value
            : CurrentTurnChoices.Player2Number!.Value;

        var isOut = batterChoice == bowlerChoice;

        if (!isOut)
        {
            if (CurrentBatterId == Player1Id) Player1Score += batterChoice;
            else Player2Score += batterChoice;
        }

        CurrentTurnChoices = new GameNumberChoices();

        if (CurrentInning == 0)
        {
            if (!isOut) return false;
            CurrentInning = 1;
            (CurrentBatterId, CurrentBowlerId) = (CurrentBowlerId, CurrentBatterId);
            CurrentPhase = HandCricketPhase.Inning2Batting;
            _lastOutcomeMessage = $"<@{CurrentBowlerId}> is out! Target: {GetTargetScore()}";
            return false;
        }

        if (isOut)
        {
            CurrentPhase = HandCricketPhase.GameOver;
            _lastOutcomeMessage = $"<@{CurrentBatterId}> is out!";
            return true;
        }

        if ((Player1Score <= Player2Score || CurrentBatterId != Player1Id) &&
            (Player2Score <= Player1Score || CurrentBatterId != Player2Id))
            return false;

        CurrentPhase = HandCricketPhase.GameOver;
        _lastOutcomeMessage = "Target chased!";
        return true;
    }

    public int GetTargetScore()
    {
        if (CurrentInning == 0) return -1;
        return (CurrentBatterId == Player1Id ? Player2Score : Player1Score) + 1;
    }

    public string GetCurrentPrompt()
    {
        var outcome = _lastOutcomeMessage;
        _lastOutcomeMessage = null; // Consume the message

        var phasePrompt = CurrentPhase switch
        {
            HandCricketPhase.TossSelectEvenOdd =>
                $"<@{Player1Id}> / <@{Player2Id}>, select Even or Odd for the toss.",
            HandCricketPhase.TossSelectNumber => GetTossNumberPrompt(),
            HandCricketPhase.TossSelectBatBowl => $"<@{TossWinnerId}>, choose to Bat or Bowl.",
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
            waitingFor = $"<@{Player1Id}>";
        else if (CurrentTossChoices is { Player1Number: not null, Player2Number: null })
            waitingFor = $"<@{Player2Id}>";

        var prompt = "Select a number (1-6) for the toss.";
        if (!string.IsNullOrEmpty(waitingFor)) prompt += $" Waiting for {waitingFor}...";

        return prompt;
    }

    private string GetGameNumberPrompt()
    {
        var waitingFor = "";
        if (CurrentTurnChoices.Player1Number == null && CurrentTurnChoices.Player2Number != null)
            waitingFor = $"<@{Player1Id}>";
        else if (CurrentTurnChoices is { Player1Number: not null, Player2Number: null })
            waitingFor = $"<@{Player2Id}>";

        var prompt = $"<@{CurrentBatterId}> is batting. <@{CurrentBowlerId}> is bowling.\nSelect a number (1-6).";
        if (!string.IsNullOrEmpty(waitingFor)) prompt += $" Waiting for {waitingFor}...";

        return prompt;
    }
}