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

    private IUser? GetWinner()
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
        return winner != null ? $"{winner.Mention} wins!" : "It's a tie!";
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

    private static string GetChoiceEmoji(RpsChoice choice) => choice switch
    {
        RpsChoice.Rock => "🪨",
        RpsChoice.Paper => "📰",
        RpsChoice.Scissors => "✂️",
        _ => "❔"
    };

    public MessageComponent BuildGameComponent(ulong messageId)
    {
        var builder = new ComponentBuilderV2();
        var container = new ContainerBuilder();

        var buttons = new ActionRowBuilder()
            .WithButton("Rock", $"assistant:rps:{messageId}:{RpsChoice.Rock}", ButtonStyle.Secondary,
                new Emoji("🪨"), disabled: BothPlayersChosen)
            .WithButton("Paper", $"assistant:rps:{messageId}:{RpsChoice.Paper}", ButtonStyle.Secondary,
                new Emoji("📰"), disabled: BothPlayersChosen)
            .WithButton("Scissors", $"assistant:rps:{messageId}:{RpsChoice.Scissors}", ButtonStyle.Secondary,
                new Emoji("✂️"), disabled: BothPlayersChosen);

        if (BothPlayersChosen)
        {
            var winner = GetWinner();
            container.WithAccentColor(winner != null ? Color.Green : Color.DarkGrey);

            container.WithTextDisplay(
                new TextDisplayBuilder(winner != null ? $"# {winner.Username} won!" : "# It's a tie!"));
            container.WithTextDisplay(new TextDisplayBuilder($"{Player1.Mention} vs {Player2.Mention}"));
            container.WithSeparator();

            var p1Choice = GetChoice(Player1);
            var p2Choice = GetChoice(Player2);

            container.WithTextDisplay(
                new TextDisplayBuilder($"**{Player1.Username}:** {GetChoiceEmoji(p1Choice)} {p1Choice}"));
            container.WithTextDisplay(
                new TextDisplayBuilder($"**{Player2.Username}:** {GetChoiceEmoji(p2Choice)} {p2Choice}"));
        }
        else
        {
            container.WithTextDisplay(new TextDisplayBuilder("# Rock Paper Scissors"));
            container.WithTextDisplay(new TextDisplayBuilder($"{Player1.Mention} vs {Player2.Mention}"));
            container.WithSeparator();

            string status;
            if (HasChosen(Player1) && !HasChosen(Player2))
                status = $"{Player1.Mention} has chosen! Waiting for {Player2.Mention}...";
            else if (!HasChosen(Player1) && HasChosen(Player2))
                status = $"{Player2.Mention} has chosen! Waiting for {Player1.Mention}...";
            else
                status = "Choose your weapon!";

            container.WithTextDisplay(new TextDisplayBuilder(status));
        }

        container.WithActionRow(buttons);
        builder.WithContainer(container);
        return builder.Build();
    }
}