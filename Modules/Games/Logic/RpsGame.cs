using Assistant.Net.Modules.Games.Models;
using Assistant.Net.Services.Games;
using Discord;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Games.Logic;

public class RpsGame
{
    private readonly Dictionary<ulong, RpsChoice> _choices = new();
    private readonly GameStatsService? _gameStatsService;
    private readonly ILogger _logger;
    private readonly Random _random = new();

    public RpsGame(IUser player1, IUser player2, GameStatsService? gameStatsService, ILogger logger)
    {
        Player1 = player1;
        Player2 = player2;
        _gameStatsService = gameStatsService;
        _logger = logger;

        _choices[Player1.Id] = RpsChoice.None;
        _choices[Player2.Id] = RpsChoice.None;

        if (Player1.IsBot)
            _choices[Player1.Id] = GetRandomChoice();
        if (Player2.IsBot)
            _choices[Player2.Id] = GetRandomChoice();
    }

    public IUser Player1 { get; }
    public IUser Player2 { get; }
    public bool BothPlayersChosen => HasChosen(Player1) && HasChosen(Player2);

    public RpsChoice GetChoice(IUser player) => _choices.GetValueOrDefault(player.Id, RpsChoice.None);

    public bool HasChosen(IUser player) => GetChoice(player) != RpsChoice.None;

    private RpsChoice GetRandomChoice()
    {
        var choices = new[] { RpsChoice.Rock, RpsChoice.Paper, RpsChoice.Scissors };
        return choices[_random.Next(choices.Length)];
    }

    public bool MakeChoice(IUser player, RpsChoice choice)
    {
        if (!_choices.ContainsKey(player.Id) || choice == RpsChoice.None) return false;
        if (HasChosen(player)) return false;

        _choices[player.Id] = choice;
        _logger.LogDebug("RPS Game ({P1} vs {P2}): {Player} chose {Choice}", Player1.Username, Player2.Username,
            player.Username, choice);
        return true;
    }

    public IUser? GetWinner()
    {
        if (!BothPlayersChosen) return null;

        var choice1 = _choices[Player1.Id];
        var choice2 = _choices[Player2.Id];

        if (choice1 == choice2) return null;

        return (choice1, choice2) switch
        {
            (RpsChoice.Rock, RpsChoice.Scissors) => Player1,
            (RpsChoice.Paper, RpsChoice.Rock) => Player1,
            (RpsChoice.Scissors, RpsChoice.Paper) => Player1,
            (RpsChoice.Scissors, RpsChoice.Rock) => Player2,
            (RpsChoice.Rock, RpsChoice.Paper) => Player2,
            (RpsChoice.Paper, RpsChoice.Scissors) => Player2,
            _ => null
        };
    }

    public string GetResultMessage()
    {
        if (!BothPlayersChosen) return "Waiting for players...";

        var winner = GetWinner();
        if (winner != null)
            return $"{winner.Mention} wins!";
        return "It's a tie!";
    }

    public Embed GetResultEmbed()
    {
        var winner = GetWinner();
        var embed = new EmbedBuilder()
            .WithTitle(winner != null ? $"{winner.Username} won!" : "It's a tie!")
            .WithColor(winner != null ? Color.Green : Color.Blue)
            .AddField(Player1.Username, GetChoice(Player1).ToString(), true)
            .AddField(Player2.Username, GetChoice(Player2).ToString(), true);

        return embed.Build();
    }

    public async Task RecordStatsIfApplicable(ulong guildId)
    {
        if (!BothPlayersChosen || Player1.IsBot || Player2.IsBot || _gameStatsService == null) return;

        var winner = GetWinner();
        try
        {
            if (winner == Player1)
                await _gameStatsService.RecordGameResultAsync(Player1.Id, Player2.Id, guildId,
                    GameStatsService.RpsGameName).ConfigureAwait(false);
            else if (winner == Player2)
                await _gameStatsService.RecordGameResultAsync(Player2.Id, Player1.Id, guildId,
                    GameStatsService.RpsGameName).ConfigureAwait(false);
            else
                await _gameStatsService.RecordGameResultAsync(Player1.Id, Player2.Id, guildId,
                    GameStatsService.RpsGameName, true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record RPS game stats for Guild {GuildId} ({P1} vs {P2})", guildId,
                Player1.Username, Player2.Username);
        }
    }

    public MessageComponent GetButtons(ulong messageId, bool disableAll = false)
    {
        var builder = new ComponentBuilder()
            .WithButton("Rock", $"assistant:rps:{messageId}:{RpsChoice.Rock}", ButtonStyle.Secondary,
                new Emoji("🪨"), disabled: disableAll)
            .WithButton("Paper", $"assistant:rps:{messageId}:{RpsChoice.Paper}", ButtonStyle.Secondary,
                new Emoji("📰"), disabled: disableAll)
            .WithButton("Scissors", $"assistant:rps:{messageId}:{RpsChoice.Scissors}", ButtonStyle.Secondary,
                new Emoji("✂️"), disabled: disableAll);

        return builder.Build();
    }
}