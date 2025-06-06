using System.Collections.Concurrent;
using Assistant.Net.Modules.Voting.Logic;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Voting.InteractionModules;

[Group("poll", "Commands for creating and participating in Elo-based polls.")]
public class VotingModule(ILogger<VotingModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    private const string VoteButtonPrefix = "assistant:poll:vote:";
    private const string SkipButtonPrefix = "assistant:poll:skip:";

    private static readonly ConcurrentDictionary<ulong, EloRatingSystem> ActivePolls = new();

    private static readonly ConcurrentDictionary<ulong, UserVotingState> UserVotingStates = new();

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

        if (ActivePolls.TryGetValue(guildChannel.Id, out var existingPoll))
        {
            await RespondAsync(
                $"There is already an active poll ('{existingPoll.Title}') in this channel, created by <@{existingPoll.CreatorId}>. Use `/poll results` to finish it first.",
                ephemeral: true).ConfigureAwait(false);
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

        try
        {
            var eloSystem = new EloRatingSystem(candidateList, Context.User.Id, title);

            if (ActivePolls.TryAdd(guildChannel.Id, eloSystem))
            {
                var embed = new EmbedBuilder()
                    .WithTitle($"📊 New Poll Created: {title}")
                    .WithDescription(
                        $"Poll created by {Context.User.Mention}.\n\n**Candidates:**\n{string.Join("\n", candidateList.Select(c => $"- {c}"))}\n\nUse `/poll vote` to cast your votes!")
                    .WithColor(Color.Blue)
                    .WithFooter($"Poll active in #{guildChannel.Name}")
                    .WithTimestamp(DateTimeOffset.UtcNow);

                await RespondAsync(embed: embed.Build()).ConfigureAwait(false);
                logger.LogInformation("Created Elo poll '{Title}' in Channel {ChannelId} by User {UserId}", title,
                    guildChannel.Id, Context.User.Id);
            }
            else
            {
                // Should not happen
                await RespondAsync("Failed to create poll due to a conflict. Please try again.", ephemeral: true)
                    .ConfigureAwait(false);
                logger.LogWarning("Failed to add poll for Channel {ChannelId} due to race condition.",
                    guildChannel.Id);
            }
        }
        catch (ArgumentException ex)
        {
            await RespondAsync($"Error creating poll: {ex.Message}", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating poll '{Title}' in Channel {ChannelId}", title, guildChannel.Id);
            await RespondAsync("An unexpected error occurred while creating the poll.", ephemeral: true)
                .ConfigureAwait(false);
        }
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

        if (!ActivePolls.TryGetValue(guildChannel.Id, out var eloSystem))
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

        // Check if user already has an active voting state
        if (UserVotingStates.ContainsKey(Context.User.Id))
        {
            await RespondAsync(
                "You seem to have an ongoing voting session. Please complete or wait for it to time out.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        // Start a new voting session
        var shuffledPairs = eloSystem.GetShuffledCandidatePairings();
        if (shuffledPairs.Count == 0)
        {
            await RespondAsync("No candidate pairs could be generated for this poll (this shouldn't happen!).",
                ephemeral: true).ConfigureAwait(false);
            logger.LogWarning("No pairs generated for poll in channel {ChannelId}", guildChannel.Id);
            return;
        }

        var userState = new UserVotingState(Context.User.Id, guildChannel.Id, shuffledPairs);

        if (UserVotingStates.TryAdd(Context.User.Id, userState))
        {
            await RespondEphemeralVotePromptAsync(eloSystem, userState).ConfigureAwait(false);
        }
        else
        {
            await RespondAsync("Could not start your voting session due to a conflict. Please try again.",
                ephemeral: true).ConfigureAwait(false);
            logger.LogWarning("Failed to add user voting state for User {UserId} due to race condition.",
                Context.User.Id);
        }
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

        if (!ActivePolls.TryGetValue(guildChannel.Id, out var eloSystem))
        {
            await RespondAsync("There is no active poll in this channel.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        // Permission Check
        var guildUser = Context.User as SocketGuildUser;
        if (eloSystem.CreatorId != Context.User.Id && (guildUser == null || !guildUser.GuildPermissions.ManageGuild))
        {
            await RespondAsync(
                $"Only the poll creator (<@{eloSystem.CreatorId}>) or an administrator can end the poll.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        // Remove the poll before sending results
        if (ActivePolls.TryRemove(guildChannel.Id, out _))
        {
            var userKeysToRemove = UserVotingStates
                .Where(kvp => kvp.Value.ChannelId == guildChannel.Id)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in userKeysToRemove) UserVotingStates.TryRemove(key, out _);

            var summary = eloSystem.GenerateSummary();

            var messageParts = summary.SmartChunkSplitList();
            await RespondAsync(messageParts[0]).ConfigureAwait(false);
            for (var i = 1; i < messageParts.Count; i++)
                await FollowupAsync(messageParts[i]).ConfigureAwait(false);

            logger.LogInformation("Ended Elo poll '{Title}' in Channel {ChannelId} by User {UserId}", eloSystem.Title,
                guildChannel.Id, Context.User.Id);
        }
        else
        {
            await RespondAsync("Failed to remove the poll. It might have been ended already.", ephemeral: true)
                .ConfigureAwait(false);
            logger.LogWarning("Failed to remove poll for Channel {ChannelId} during results command.",
                guildChannel.Id);
        }
    }

    // --- Component Interaction Handlers ---

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
            logger.LogWarning(
                "User mismatch on vote button. Expected {ExpectedUserId}, Got {ActualUserId}. CustomId: {CustomId}",
                Context.User.Id, userIdFromId, component.Data.CustomId);
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


        if (!UserVotingStates.TryGetValue(Context.User.Id, out var userState))
        {
            await component.RespondAsync(
                    "Your voting session seems to have expired or is invalid. Try `/poll vote` again.", ephemeral: true)
                .ConfigureAwait(false);
            await TryRemoveComponents(component).ConfigureAwait(false);
            return;
        }

        if (!ActivePolls.TryGetValue(userState.ChannelId, out var eloSystem))
        {
            await component.RespondAsync("The poll has ended.", ephemeral: true).ConfigureAwait(false);
            UserVotingStates.TryRemove(Context.User.Id, out _);
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
            logger.LogWarning(
                "User mismatch on skip button. Expected {ExpectedUserId}, Got {ActualUserId}. CustomId: {CustomId}",
                Context.User.Id, userIdFromId, component.Data.CustomId);
            await component.RespondAsync("This button isn't for you!", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!UserVotingStates.TryGetValue(Context.User.Id, out var userState))
        {
            await component.RespondAsync(
                    "Your voting session seems to have expired or is invalid. Try `/poll vote` again.", ephemeral: true)
                .ConfigureAwait(false);
            await TryRemoveComponents(component).ConfigureAwait(false);
            return;
        }

        if (!ActivePolls.TryGetValue(userState.ChannelId, out var eloSystem))
        {
            await component.RespondAsync("The poll has ended.", ephemeral: true).ConfigureAwait(false);
            UserVotingStates.TryRemove(Context.User.Id, out _);
            await TryRemoveComponents(component).ConfigureAwait(false);
            return;
        }

        // --- Perform Action ---
        await component.DeferAsync(true).ConfigureAwait(false);

        userState.CurrentPairIndex++;

        await RespondEphemeralVotePromptAsync(eloSystem, userState, component).ConfigureAwait(false);
    }


    // --- Helper Methods ---

    private async Task RespondEphemeralVotePromptAsync(EloRatingSystem eloSystem, UserVotingState userState,
        SocketMessageComponent? interaction = null)
    {
        var currentPair = userState.GetCurrentPair();

        if (currentPair == null)
        {
            eloSystem.AddVoter(userState.UserId);
            UserVotingStates.TryRemove(userState.UserId, out _);

            const string completionMsg = "✅ Voting Complete! Thank you for participating.";
            if (interaction != null)
            {
                await interaction.ModifyOriginalResponseAsync(props =>
                {
                    props.Content = completionMsg;
                    props.Components = new ComponentBuilder().Build();
                }).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync(completionMsg, ephemeral: true).ConfigureAwait(false);
                logger.LogWarning("Responded completion message on initial /vote call for user {UserId}",
                    userState.UserId);
            }

            logger.LogInformation("User {UserId} completed voting for poll '{Title}' in Channel {ChannelId}",
                userState.UserId, eloSystem.Title, userState.ChannelId);
        }
        else
        {
            var (c1, c2) = currentPair.Value;
            var progress = $"Pair {userState.CurrentPairIndex + 1} of {userState.ShuffledPairs.Count}";
            var prompt = $"**{eloSystem.Title}**\nWhich do you prefer?\n\n(`{progress}`)";

            var builder = new ComponentBuilder()
                .WithButton(c1,
                    $"{VoteButtonPrefix}{userState.UserId}:{EloRatingSystem.EncodeCandidate(c1)}:{EloRatingSystem.EncodeCandidate(c2)}",
                    row: 0)
                .WithButton(c2,
                    $"{VoteButtonPrefix}{userState.UserId}:{EloRatingSystem.EncodeCandidate(c2)}:{EloRatingSystem.EncodeCandidate(c1)}",
                    row: 0)
                .WithButton("Skip", $"{SkipButtonPrefix}{userState.UserId}:{userState.CurrentPairIndex}",
                    ButtonStyle.Secondary, new Emoji("⏩"), row: 1);

            if (interaction != null)
                await interaction.ModifyOriginalResponseAsync(props =>
                {
                    props.Content = prompt;
                    props.Components = builder.Build();
                }).ConfigureAwait(false);
            else
                await RespondAsync(prompt, components: builder.Build(), ephemeral: true).ConfigureAwait(false);
        }
    }

    private async Task TryRemoveComponents(SocketMessageComponent interaction)
    {
        try
        {
            await interaction.ModifyOriginalResponseAsync(props => props.Components = new ComponentBuilder().Build())
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to remove components from interaction message {MessageId}",
                interaction.Message.Id);
        }
    }
}