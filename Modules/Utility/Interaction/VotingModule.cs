using Assistant.Net.Services.Features;
using Assistant.Net.Services.Features.Logic;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Utility.Interaction;

[Group("poll", "Commands for creating and participating in Elo-based polls.")]
public class VotingModule(ILogger<VotingModule> logger, PollService pollService)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string VoteButtonPrefix = "assistant:poll:vote:";
    private const string SkipButtonPrefix = "assistant:poll:skip:";

    [SlashCommand("create", "Set up the candidates for an Elo poll in this channel.")]
    [RequireContext(ContextType.Guild)]
    public async Task CreatePollAsync(
        [Summary(description: "The title or question for the poll.")]
        string title,
        [Summary(description: "Comma-separated list of candidates (at least 2).")]
        string candidates)
    {
        if (Context.Channel is not SocketGuildChannel guildChannel)
        {
            await RespondAsync("This command can only be used in server channels.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        var candidateList = candidates.Split(',')
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();

        if (candidateList.Count < 2)
        {
            await RespondAsync("Please provide at least two unique, non-empty candidates separated by commas.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!pollService.CreatePoll(guildChannel.Id, Context.User.Id, title, candidateList, out _,
                out var errorMessage))
        {
            await RespondAsync(errorMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder($"# 📊 New Poll Created: {title}"))
            .WithTextDisplay(new TextDisplayBuilder($"Created by {Context.User.Mention}"))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"**Candidates:**\n{string.Join("\n", candidateList.Select(c => $"- {c}"))}"))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder("*Use `/poll vote` to cast your votes!*"));

        var components = new ComponentBuilderV2().WithContainer(container).Build();

        await RespondAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
        logger.LogInformation("Created Elo poll '{Title}' in Channel {ChannelId} by User {UserId}", title,
            guildChannel.Id, Context.User.Id);
    }

    [SlashCommand("vote", "Cast your votes in the active poll for this channel.")]
    [RequireContext(ContextType.Guild)]
    public async Task VoteAsync()
    {
        if (Context.Channel is not SocketGuildChannel guildChannel)
        {
            await RespondAsync("This command can only be used in server channels.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        if (!pollService.TryGetPoll(guildChannel.Id, out var eloSystem) || eloSystem == null)
        {
            await RespondAsync("There is no active poll in this channel.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (eloSystem.HasVotedBefore(Context.User.Id))
        {
            await RespondAsync("You have already cast your votes for this poll!", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        if (pollService.HasActiveVotingSession(Context.User.Id))
        {
            await RespondAsync(
                "You seem to have an ongoing voting session. Please complete or wait for it to time out.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        var userState = pollService.StartVotingSession(Context.User.Id, guildChannel.Id, eloSystem);
        if (userState != null)
            await RespondEphemeralVotePromptAsync(eloSystem, userState).ConfigureAwait(false);
        else
            await RespondAsync("Could not start your voting session. Please try again.", ephemeral: true)
                .ConfigureAwait(false);
    }

    [SlashCommand("results", "End the current poll in this channel and display the results.")]
    [RequireContext(ContextType.Guild)]
    public async Task PollResultsAsync()
    {
        if (Context.Channel is not SocketGuildChannel guildChannel)
        {
            await RespondAsync("This command can only be used in server channels.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        if (!pollService.TryGetPoll(guildChannel.Id, out var eloSystem) || eloSystem == null)
        {
            await RespondAsync("There is no active poll in this channel.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var guildUser = Context.User as SocketGuildUser;
        if (eloSystem.CreatorId != Context.User.Id && (guildUser == null || !guildUser.GuildPermissions.ManageGuild))
        {
            await RespondAsync(
                $"Only the poll creator (<@{eloSystem.CreatorId}>) or an administrator can end the poll.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (pollService.TryEndPoll(guildChannel.Id, out var endedPoll) && endedPoll != null)
        {
            var resultsComponent = endedPoll.GenerateResultsComponent();
            await RespondAsync(components: resultsComponent, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
            logger.LogInformation("Ended Elo poll '{Title}' in Channel {ChannelId} by User {UserId}",
                endedPoll.Title, guildChannel.Id, Context.User.Id);
        }
        else
        {
            await RespondAsync("Failed to remove the poll. It might have been ended already.", ephemeral: true)
                .ConfigureAwait(false);
            logger.LogWarning("Failed to remove poll for Channel {ChannelId} during results command.",
                guildChannel.Id);
        }
    }

    [ComponentInteraction("assistant:poll:vote:*", true)]
    public async Task HandleVoteButtonAsync()
    {
        if (Context.Interaction is not SocketMessageComponent component) return;

        var customIdParts = component.Data.CustomId.Split(':');
        if (customIdParts.Length != 6)
        {
            logger.LogWarning("Invalid vote button CustomId format: {CustomId}", component.Data.CustomId);
            await component.RespondAsync("Invalid button data.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!ulong.TryParse(customIdParts[3], out var userIdFromId) || userIdFromId != Context.User.Id)
        {
            await component.RespondAsync("This button isn't for you!", ephemeral: true).ConfigureAwait(false);
            return;
        }

        string winner, loser;
        try
        {
            winner = EloRatingSystem.DecodeCandidate(customIdParts[4]);
            loser = EloRatingSystem.DecodeCandidate(customIdParts[5]);
        }
        catch (FormatException ex)
        {
            logger.LogError(ex, "Failed to decode candidate from vote button CustomId: {CustomId}",
                component.Data.CustomId);
            await component.RespondAsync("Error processing button data.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!pollService.TryGetUserVotingState(Context.User.Id, out var userState) || userState == null)
        {
            await component.RespondAsync(
                    "Your voting session seems to have expired or is invalid. Try `/poll vote` again.", ephemeral: true)
                .ConfigureAwait(false);
            await TryRemoveComponents(component).ConfigureAwait(false);
            return;
        }

        if (!pollService.TryGetPoll(userState.ChannelId, out var eloSystem) || eloSystem == null)
        {
            await component.RespondAsync("The poll has ended.", ephemeral: true).ConfigureAwait(false);
            pollService.EndVotingSession(Context.User.Id);
            await TryRemoveComponents(component).ConfigureAwait(false);
            return;
        }

        await component.DeferAsync(true).ConfigureAwait(false);

        eloSystem.UpdateRatings(winner, loser);
        userState.CurrentPairIndex++;

        await RespondEphemeralVotePromptAsync(eloSystem, userState, component).ConfigureAwait(false);
    }


    [ComponentInteraction("assistant:poll:skip:*", true)]
    public async Task HandleSkipButtonAsync()
    {
        if (Context.Interaction is not SocketMessageComponent component) return;

        var customIdParts = component.Data.CustomId.Split(':');
        if (customIdParts.Length != 5)
        {
            logger.LogWarning("Invalid skip button CustomId format: {CustomId}", component.Data.CustomId);
            await component.RespondAsync("Invalid button data.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!ulong.TryParse(customIdParts[3], out var userIdFromId) || userIdFromId != Context.User.Id)
        {
            await component.RespondAsync("This button isn't for you!", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!pollService.TryGetUserVotingState(Context.User.Id, out var userState) || userState == null)
        {
            await component.RespondAsync(
                    "Your voting session seems to have expired or is invalid. Try `/poll vote` again.", ephemeral: true)
                .ConfigureAwait(false);
            await TryRemoveComponents(component).ConfigureAwait(false);
            return;
        }

        if (!pollService.TryGetPoll(userState.ChannelId, out var eloSystem) || eloSystem == null)
        {
            await component.RespondAsync("The poll has ended.", ephemeral: true).ConfigureAwait(false);
            pollService.EndVotingSession(Context.User.Id);
            await TryRemoveComponents(component).ConfigureAwait(false);
            return;
        }

        await component.DeferAsync(true).ConfigureAwait(false);

        userState.CurrentPairIndex++;

        await RespondEphemeralVotePromptAsync(eloSystem, userState, component).ConfigureAwait(false);
    }

    private async Task RespondEphemeralVotePromptAsync(EloRatingSystem eloSystem, UserVotingState userState,
        IComponentInteraction? interaction = null)
    {
        var currentPair = userState.GetCurrentPair();

        if (currentPair == null)
        {
            eloSystem.AddVoter(userState.UserId);
            pollService.EndVotingSession(userState.UserId);

            var completionContainer = new ContainerBuilder()
                .WithTextDisplay(new TextDisplayBuilder("✅ **Voting Complete!**"))
                .WithTextDisplay(
                    new TextDisplayBuilder(
                        "Thank you for participating. The results will be shown when the poll ends."));

            var components = new ComponentBuilderV2().WithContainer(completionContainer).Build();

            if (interaction != null)
                await interaction.ModifyOriginalResponseAsync(props =>
                {
                    props.Content = "";
                    props.Components = components;
                    props.Flags = MessageFlags.ComponentsV2;
                }).ConfigureAwait(false);
            else
                await RespondAsync(components: components, ephemeral: true, flags: MessageFlags.ComponentsV2)
                    .ConfigureAwait(false);

            logger.LogInformation("User {UserId} completed voting for poll '{Title}' in Channel {ChannelId}",
                userState.UserId, eloSystem.Title, userState.ChannelId);
        }
        else
        {
            var (c1, c2) = currentPair.Value;
            var progress = $"Pair {userState.CurrentPairIndex + 1} of {userState.ShuffledPairs.Count}";

            var promptContainer = new ContainerBuilder()
                .WithTextDisplay(new TextDisplayBuilder($"**{eloSystem.Title}** | {progress}"))
                .WithSeparator()
                .WithTextDisplay(new TextDisplayBuilder("Which do you prefer?"))
                .WithActionRow(row => row
                    .WithButton(c1,
                        $"{VoteButtonPrefix}{userState.UserId}:{EloRatingSystem.EncodeCandidate(c1)}:{EloRatingSystem.EncodeCandidate(c2)}")
                    .WithButton(c2,
                        $"{VoteButtonPrefix}{userState.UserId}:{EloRatingSystem.EncodeCandidate(c2)}:{EloRatingSystem.EncodeCandidate(c1)}"))
                .WithActionRow(row => row
                    .WithButton("Skip", $"{SkipButtonPrefix}{userState.UserId}:{userState.CurrentPairIndex}",
                        ButtonStyle.Secondary, new Emoji("⏩")));

            var components = new ComponentBuilderV2().WithContainer(promptContainer).Build();

            if (interaction != null)
                await interaction.ModifyOriginalResponseAsync(props =>
                {
                    props.Content = "";
                    props.Components = components;
                    props.Flags = MessageFlags.ComponentsV2;
                }).ConfigureAwait(false);
            else
                await RespondAsync(components: components, ephemeral: true, flags: MessageFlags.ComponentsV2)
                    .ConfigureAwait(false);
        }
    }

    private async Task TryRemoveComponents(SocketMessageComponent interaction)
    {
        try
        {
            var container = new ContainerBuilder()
                .WithTextDisplay(new TextDisplayBuilder("*This voting session has ended.*"));

            await interaction.ModifyOriginalResponseAsync(props =>
            {
                props.Components = new ComponentBuilderV2().WithContainer(container).Build();
                props.Flags = MessageFlags.ComponentsV2;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to remove components from interaction message {MessageId}",
                interaction.Message.Id);
        }
    }
}