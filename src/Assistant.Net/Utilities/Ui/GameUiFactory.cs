using Assistant.Net.Services.Games.Logic;
using Assistant.Net.Services.Games.Models;
using Discord;

namespace Assistant.Net.Utilities.Ui;

public static class GameUiFactory
{
    private const string RpsCustomIdPrefix = "rps";
    private const string TttCustomIdPrefix = "tictactoe";
    private const string HcCustomIdPrefix = "hc";

    public static MessageComponent GetRpsTimeoutDisplay()
    {
        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder("# Rock Paper Scissors"))
            .WithTextDisplay(new TextDisplayBuilder("This game has timed out due to inactivity."))
            .WithActionRow(row => row
                .WithButton("Rock", "dummy_rock", ButtonStyle.Secondary, new Emoji("🪨"), disabled: true)
                .WithButton("Paper", "dummy_paper", ButtonStyle.Secondary, new Emoji("📰"), disabled: true)
                .WithButton("Scissors", "dummy_scissors", ButtonStyle.Secondary, new Emoji("✂️"), disabled: true)
            );
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    public static MessageComponent GetTicTacToeTimeoutDisplay()
    {
        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder("# Tic Tac Toe"))
            .WithTextDisplay(new TextDisplayBuilder("This game has timed out due to inactivity."))
            .WithSeparator();

        for (var i = 0; i < 3; i++)
        {
            var rowBuilder = new ActionRowBuilder();
            for (var j = 0; j < 3; j++)
                rowBuilder.WithButton("\u200b", $"dummy_disabled_{i}_{j}", ButtonStyle.Secondary, disabled: true);
            container.WithActionRow(rowBuilder);
        }

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    public static MessageComponent GetHandCricketTimeoutDisplay(string p1Mention, string p2Mention)
    {
        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder("# Hand Cricket"))
            .WithTextDisplay(
                new TextDisplayBuilder(
                    $"The game between {p1Mention} and {p2Mention} has timed out due to inactivity."));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    public static MessageComponent BuildRpsGameComponent(ulong messageId, RpsGame game)
    {
        var builder = new ComponentBuilderV2();
        var container = new ContainerBuilder();

        var buttons = new ActionRowBuilder()
            .WithButton("Rock", $"{RpsCustomIdPrefix}:{messageId}:{RpsChoice.Rock}", ButtonStyle.Secondary,
                new Emoji("🪨"), disabled: game.BothPlayersChosen)
            .WithButton("Paper", $"{RpsCustomIdPrefix}:{messageId}:{RpsChoice.Paper}", ButtonStyle.Secondary,
                new Emoji("📰"), disabled: game.BothPlayersChosen)
            .WithButton("Scissors", $"{RpsCustomIdPrefix}:{messageId}:{RpsChoice.Scissors}", ButtonStyle.Secondary,
                new Emoji("✂️"), disabled: game.BothPlayersChosen);

        if (game.BothPlayersChosen)
        {
            var winnerId = game.GetWinnerId();
            container.WithAccentColor(winnerId != null ? Color.Green : Color.DarkGrey);

            var winnerName = winnerId == game.Player1Id ? game.Player1Name :
                winnerId == game.Player2Id ? game.Player2Name : null;

            container.WithTextDisplay(
                new TextDisplayBuilder(winnerName != null ? $"# {winnerName} won!" : "# It's a tie!"));
            container.WithTextDisplay(new TextDisplayBuilder($"<@{game.Player1Id}> vs <@{game.Player2Id}>"));
            container.WithSeparator();

            var p1Choice = game.GetChoice(game.Player1Id);
            var p2Choice = game.GetChoice(game.Player2Id);

            container.WithTextDisplay(
                new TextDisplayBuilder($"**{game.Player1Name}:** {GetRpsChoiceEmoji(p1Choice)} {p1Choice}"));
            container.WithTextDisplay(
                new TextDisplayBuilder($"**{game.Player2Name}:** {GetRpsChoiceEmoji(p2Choice)} {p2Choice}"));
        }
        else
        {
            container.WithTextDisplay(new TextDisplayBuilder("# Rock Paper Scissors"));
            container.WithTextDisplay(new TextDisplayBuilder($"<@{game.Player1Id}> vs <@{game.Player2Id}>"));
            container.WithSeparator();

            string status;
            if (game.HasChosen(game.Player1Id) && !game.HasChosen(game.Player2Id))
                status = $"<@{game.Player1Id}> has chosen! Waiting for <@{game.Player2Id}>...";
            else if (!game.HasChosen(game.Player1Id) && game.HasChosen(game.Player2Id))
                status = $"<@{game.Player2Id}> has chosen! Waiting for <@{game.Player1Id}>...";
            else
                status = "Choose your weapon!";

            container.WithTextDisplay(new TextDisplayBuilder(status));
        }

        container.WithActionRow(buttons);
        builder.WithContainer(container);
        return builder.Build();
    }

    private static string GetRpsChoiceEmoji(RpsChoice choice) => choice switch
    {
        RpsChoice.Rock => "🪨",
        RpsChoice.Paper => "📰",
        RpsChoice.Scissors => "✂️",
        _ => "❔"
    };

    public static MessageComponent BuildTicTacToeComponent(TicTacToeGame game)
    {
        var builder = new ComponentBuilderV2();
        var container = new ContainerBuilder();

        string statusMessage;
        switch (game.Result)
        {
            case GameResultState.XWins:
                statusMessage = $"**<@{game.Player1Id}> wins!**";
                container.WithAccentColor(Color.Green);
                break;
            case GameResultState.OWins:
                statusMessage = $"**<@{game.Player2Id}> wins!**";
                container.WithAccentColor(Color.Green);
                break;
            case GameResultState.Tie:
                statusMessage = "**It's a tie!**";
                container.WithAccentColor(Color.DarkGrey);
                break;
            case GameResultState.None:
            default:
                statusMessage = $"It's {game.CurrentPlayerMention}'s turn!";
                break;
        }

        container
            .WithTextDisplay(new TextDisplayBuilder("# Tic Tac Toe"))
            .WithTextDisplay(new TextDisplayBuilder($"<@{game.Player1Id}> (❌) vs <@{game.Player2Id}> (⭕)"))
            .WithTextDisplay(new TextDisplayBuilder(statusMessage));

        if (game is { IsGameOver: false, IsBotGuaranteedWin: true } && !string.IsNullOrEmpty(game.BotTaunt))
        {
            container.WithSeparator();
            container.WithTextDisplay(new TextDisplayBuilder(game.BotTaunt));
        }

        container.WithSeparator();

        var disableAll = game.IsGameOver;
        for (var row = 0; row < 3; row++)
        {
            var actionRow = new ActionRowBuilder();
            for (var col = 0; col < 3; col++)
            {
                var index = row * 3 + col + 1;
                var marker = game.GetMarkerAt(row, col);

                var label = marker switch
                {
                    PlayerMarker.None => "\u200b",
                    PlayerMarker.X => "❌",
                    PlayerMarker.O => "⭕",
                    _ => "?"
                };

                var style = marker switch
                {
                    PlayerMarker.X => ButtonStyle.Primary,
                    PlayerMarker.O => ButtonStyle.Success,
                    _ => ButtonStyle.Secondary
                };

                var disabled = disableAll || marker != PlayerMarker.None;

                actionRow.WithButton(label, $"{TttCustomIdPrefix}:{game.GameId}:{index}", style, disabled: disabled);
            }

            container.WithActionRow(actionRow);
        }

        builder.WithContainer(container);
        return builder.Build();
    }

    public static MessageComponent BuildHandCricketComponent(HandCricketGame game)
    {
        var builder = new ComponentBuilderV2();
        var container = new ContainerBuilder();

        container.AddComponent(
            new TextDisplayBuilder($"# Hand Cricket: {game.Player1Name} vs {game.Player2Name}"));
        container.AddComponent(new TextDisplayBuilder($"*Phase: {GetHumanPhaseName(game.CurrentPhase)}*"));
        container.WithSeparator();

        var p1Role = game.CurrentBatterId == game.Player1Id ? "🏏" : "⚾";
        var p2Role = game.CurrentBatterId == game.Player2Id ? "🏏" : "⚾";
        container.AddComponent(new TextDisplayBuilder($"**{game.Player1Name} {p1Role}:** {game.Player1Score}"));
        container.AddComponent(new TextDisplayBuilder($"**{game.Player2Name} {p2Role}:** {game.Player2Score}"));

        var targetScore = game.GetTargetScore();
        if (targetScore > 0 && game.CurrentPhase == HandCricketPhase.Inning2Batting)
            container.AddComponent(new TextDisplayBuilder($"**Target:** {targetScore}"));

        var prompt = game.GetCurrentPrompt();
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            container.WithSeparator();
            container.WithTextDisplay(new TextDisplayBuilder(prompt));
        }

        var buttonActionRows = GetActionRowsForHandCricket(game);
        foreach (var row in buttonActionRows) container.WithActionRow(row);

        container.WithSeparator();
        container.WithTextDisplay(new TextDisplayBuilder($"*Game ID: {game.GameId[..8]}*"));

        builder.WithContainer(container);
        return builder.Build();
    }

    private static string GetHumanPhaseName(HandCricketPhase phase)
    {
        return phase switch
        {
            HandCricketPhase.TossSelectEvenOdd => "Toss - Choose Even/Odd",
            HandCricketPhase.TossSelectNumber => "Toss - Choose Number",
            HandCricketPhase.TossSelectBatBowl => "Toss - Choose Bat/Bowl",
            HandCricketPhase.Inning1Batting => "Inning 1",
            HandCricketPhase.Inning2Batting => "Inning 2",
            HandCricketPhase.GameOver => "Game Over",
            _ => phase.ToString()
        };
    }

    private static List<ActionRowBuilder> GetActionRowsForHandCricket(HandCricketGame game)
    {
        var rows = new List<ActionRowBuilder>();

        switch (game.CurrentPhase)
        {
            case HandCricketPhase.TossSelectEvenOdd:
                rows.Add(new ActionRowBuilder()
                    .WithButton("Even", $"{HcCustomIdPrefix}:{game.GameId}:toss_eo:even", ButtonStyle.Success)
                    .WithButton("Odd", $"{HcCustomIdPrefix}:{game.GameId}:toss_eo:odd", ButtonStyle.Danger));
                break;

            case HandCricketPhase.TossSelectNumber:
                rows.AddRange(CreateNumberButtonRows(HandCricketGame.TossNumbers, "toss_num", game.GameId));
                break;

            case HandCricketPhase.TossSelectBatBowl:
                rows.Add(new ActionRowBuilder()
                    .WithButton("Bat 🏏", $"{HcCustomIdPrefix}:{game.GameId}:batbowl:bat")
                    .WithButton("Bowl ⚾", $"{HcCustomIdPrefix}:{game.GameId}:batbowl:bowl", ButtonStyle.Success));
                break;

            case HandCricketPhase.Inning1Batting:
            case HandCricketPhase.Inning2Batting:
                rows.AddRange(CreateNumberButtonRows(HandCricketGame.GameNumbers, "play_num", game.GameId));
                break;

            case HandCricketPhase.GameOver:
                break;
        }

        return rows;
    }

    private static List<ActionRowBuilder> CreateNumberButtonRows(IEnumerable<int> numbers, string action, string gameId)
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

            currentRow.WithButton(num.ToString(), $"{HcCustomIdPrefix}:{gameId}:{action}:{num}",
                ButtonStyle.Secondary);
            count++;
        }

        if (currentRow.Components.Count > 0) actionRows.Add(currentRow);

        return actionRows;
    }
}