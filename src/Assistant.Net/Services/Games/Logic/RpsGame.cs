using Assistant.Net.Services.Games.Models;

namespace Assistant.Net.Services.Games.Logic;

public class RpsGame
{
    private readonly Dictionary<ulong, RpsChoice> _choices = new();
    private readonly Random _random = new();

    public RpsGame(ulong player1Id, string player1Name, bool p1IsBot, ulong player2Id, string player2Name, bool p2IsBot)
    {
        Player1Id = player1Id;
        Player1Name = player1Name;
        Player2Id = player2Id;
        Player2Name = player2Name;

        P1IsBot = p1IsBot;
        P2IsBot = p2IsBot;

        _choices[Player1Id] = RpsChoice.None;
        _choices[Player2Id] = RpsChoice.None;

        if (P1IsBot)
            _choices[Player1Id] = GetRandomChoice();
        if (P2IsBot)
            _choices[Player2Id] = GetRandomChoice();
    }

    public ulong Player1Id { get; }
    public string Player1Name { get; }
    public bool P1IsBot { get; }

    public ulong Player2Id { get; }
    public string Player2Name { get; }
    public bool P2IsBot { get; }

    public bool BothPlayersChosen => HasChosen(Player1Id) && HasChosen(Player2Id);

    public RpsChoice GetChoice(ulong playerId) => _choices.GetValueOrDefault(playerId, RpsChoice.None);

    public bool HasChosen(ulong playerId) => GetChoice(playerId) != RpsChoice.None;

    private RpsChoice GetRandomChoice()
    {
        var choices = new[] { RpsChoice.Rock, RpsChoice.Paper, RpsChoice.Scissors };
        return choices[_random.Next(choices.Length)];
    }

    public bool MakeChoice(ulong playerId, RpsChoice choice)
    {
        if (!_choices.ContainsKey(playerId) || choice == RpsChoice.None) return false;
        if (HasChosen(playerId)) return false;

        _choices[playerId] = choice;
        return true;
    }

    public ulong? GetWinnerId()
    {
        if (!BothPlayersChosen) return null;

        var choice1 = _choices[Player1Id];
        var choice2 = _choices[Player2Id];

        if (choice1 == choice2) return null;

        return (choice1, choice2) switch
        {
            (RpsChoice.Rock, RpsChoice.Scissors) => Player1Id,
            (RpsChoice.Paper, RpsChoice.Rock) => Player1Id,
            (RpsChoice.Scissors, RpsChoice.Paper) => Player1Id,
            (RpsChoice.Scissors, RpsChoice.Rock) => Player2Id,
            (RpsChoice.Rock, RpsChoice.Paper) => Player2Id,
            (RpsChoice.Paper, RpsChoice.Scissors) => Player2Id,
            _ => null
        };
    }

    public string GetResultMessage()
    {
        if (!BothPlayersChosen) return "Waiting for players...";

        var winnerId = GetWinnerId();
        return winnerId != null ? $"<@{winnerId}> wins!" : "It's a tie!";
    }
}