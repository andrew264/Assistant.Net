using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Text;

namespace Assistant.Net.Modules;
public class RPSGameModule : InteractionModuleBase<SocketInteractionContext>
{
    public required InteractionService Commands { get; set; }
    private static Dictionary<string, GameState> GameStates = new();

    [SlashCommand("rps", "Play a Game of Rock Paper Scissors")]
    public async Task RPSGameAsync(
        [Summary(description: "Select your Opponent")] SocketUser? opponent = null)
    {
        string gameID = Context.Interaction.Id.ToString();
        SocketUser user1 = Context.User;
        SocketUser user2 = opponent ?? Context.Client.CurrentUser;

        if (user1 == user2)
        {
            await RespondAsync("You can't play with yourself", ephemeral: true);
            return;
        }

        var game = new GameState(user1, user2);
        if (user2.IsBot)
            game.SetChoice(user2, (RPSChoices)new Random().Next(0, 3));

        GameStates[gameID] = game;

        var builder = new ComponentBuilder()
            .WithButton("rock", $"assistant:rps:{gameID}:rock", ButtonStyle.Secondary, emote: new Emoji("🪨"))
            .WithButton("paper", $"assistant:rps:{gameID}:paper", ButtonStyle.Secondary, emote: new Emoji("📃"))
            .WithButton("scissors", $"assistant:rps:{gameID}:scissors", ButtonStyle.Secondary, emote: new Emoji("✂️"));

        await RespondAsync($"Choose your move\n{user1.Mention} & {user2.Mention}", components: builder.Build());
    }

    [ComponentInteraction("assistant:rps:*:*")]
    public async Task RPSButtonPress(string gameID, string buttonName)
    {
        if (!GameStates.TryGetValue(gameID, out var game))
        {
            await RespondAsync("Game not found", ephemeral: true);
            return;
        }

        if (!game.UserInGameState(Context.User))
        {
            await RespondAsync("You can start your own game with `/rps` command", ephemeral: true);
            return;
        }

        var choice = buttonName switch
        {
            "rock" => RPSChoices.Rock,
            "paper" => RPSChoices.Paper,
            "scissors" => RPSChoices.Scissors,
            _ => RPSChoices.NONE
        };

        if (game.GetChoice(Context.User) != RPSChoices.NONE)
        {
            await RespondAsync("You have already made a choice", ephemeral: true);
            return;
        }
        else
        {
            game.SetChoice(Context.User, choice);
        }

        if (game.BothPlayersMadeChoice())
        {
            var winnerChoice = game.GetChoice(game.GetWinner()!);
            var loserChoice = game.GetChoice(game.Choices.Keys.First(x => x != game.GetWinner()));
            var components = new ComponentBuilder()
                .WithButton(new ButtonBuilder()
                    .WithLabel("rock")
                    .WithCustomId("rock")
                    .WithEmote(new Emoji("🪨"))
                    .WithStyle((winnerChoice == RPSChoices.Rock) ? ButtonStyle.Success : (loserChoice == RPSChoices.Rock) ? ButtonStyle.Danger : ButtonStyle.Secondary)
                    .WithDisabled(true))
                .WithButton(new ButtonBuilder()
                    .WithLabel("paper")
                    .WithCustomId("paper")
                    .WithEmote(new Emoji("📃"))
                    .WithStyle((winnerChoice == RPSChoices.Paper) ? ButtonStyle.Success : (loserChoice == RPSChoices.Paper) ? ButtonStyle.Danger : ButtonStyle.Secondary)
                    .WithDisabled(true))
                .WithButton(new ButtonBuilder()
                    .WithLabel("scissors")
                    .WithEmote(new Emoji("✂️"))
                    .WithCustomId("scissors")
                    .WithStyle((winnerChoice == RPSChoices.Scissors) ? ButtonStyle.Success : (loserChoice == RPSChoices.Scissors) ? ButtonStyle.Danger : ButtonStyle.Secondary)
                    .WithDisabled(true));

            await DeferAsync();
            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = game.ToString();
                x.Components = components.Build();
            });
            GameStates.Remove(gameID);
        }
        else
        {
            await DeferAsync();
            await ModifyOriginalResponseAsync(x => x.Content = game.ToString());
        }
    }

    private class GameState
    {
        public Dictionary<SocketUser, RPSChoices> Choices { get; }

        public GameState(SocketUser user1, SocketUser user2)
        {
            Choices = new()
            {
                { user1, RPSChoices.NONE },
                { user2, RPSChoices.NONE }
            };
        }

        public bool UserInGameState(SocketUser user) => Choices.ContainsKey(user);

        public void SetChoice(SocketUser user, RPSChoices choice)
        {
            if (Choices.ContainsKey(user))
                Choices[user] = choice;
        }

        public RPSChoices GetChoice(SocketUser user) => Choices[user];

        public bool BothPlayersMadeChoice() => Choices.Values.All(x => x != RPSChoices.NONE);

        public SocketUser? GetWinner()
        {
            var user1 = Choices.Keys.First();
            var user2 = Choices.Keys.Last();
            var (user1Choice, user2Choice) = (Choices[user1], Choices[user2]);

            if (user1Choice == user2Choice)
                return null;

            return (user1Choice == RPSChoices.Rock && user2Choice == RPSChoices.Scissors) ||
                   (user1Choice == RPSChoices.Paper && user2Choice == RPSChoices.Rock) ||
                   (user1Choice == RPSChoices.Scissors && user2Choice == RPSChoices.Paper)
                ? user1
                : user2;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            if (BothPlayersMadeChoice())
            {
                foreach (var entry in Choices)
                    sb.AppendLine($"{entry.Key.Mention} chose {entry.Value}");

                var winner = GetWinner();
                sb.AppendLine(winner == null ? "It's a **TIE**!" : $"## THE WINNER IS 🥳🎉 {winner.Mention} 🎊🥳 !");
            }
            else
            {
                foreach (var entry in Choices)
                {
                    if (entry.Value == RPSChoices.NONE)
                        sb.AppendLine($"Waiting for {entry.Key.Mention} to make a choice");
                }
            }

            return sb.ToString();
        }
    }

    private enum RPSChoices
    {
        NONE = -1,
        Rock = 0,
        Paper = 1,
        Scissors = 2
    }
}