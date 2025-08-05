using Assistant.Net.Modules.Music.Logic;
using Assistant.Net.Modules.Music.Logic.Player;
using Assistant.Net.Services.Music;
using Discord;
using Discord.Interactions;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Base;

public abstract class MusicInteractionModuleBase(MusicService musicService, ILogger logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    protected readonly ILogger Logger = logger;
    protected readonly MusicService MusicService = musicService;

    protected async Task<(CustomPlayer? Player, bool IsError)> GetVerifiedPlayerAsync(
        PlayerChannelBehavior channelBehavior = PlayerChannelBehavior.None,
        MemberVoiceStateBehavior memberBehavior = MemberVoiceStateBehavior.Ignore,
        Func<PlayerRetrieveStatus, string>? customErrorMessageProvider = null)
    {
        var (player, retrieveStatus) = await MusicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, channelBehavior, memberBehavior).ConfigureAwait(false);

        if (player is not null) return (player, false);
        var errorMessage = customErrorMessageProvider?.Invoke(retrieveStatus)
                           ?? MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
        await RespondOrFollowupErrorAsync(errorMessage).ConfigureAwait(false);
        return (null, true);
    }

    protected async Task RespondOrFollowupAsync(string? text = null, bool ephemeral = false,
        MessageComponent? components = null, AllowedMentions? allowedMentions = null, bool isError = false)
    {
        var effectiveEphemeral = ephemeral || isError;
        var finalAllowedMentions = allowedMentions ?? AllowedMentions.None;

        if (Context.Interaction.HasResponded)
            await FollowupAsync(text, ephemeral: effectiveEphemeral, components: components,
                allowedMentions: finalAllowedMentions, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
        else
            await RespondAsync(text, ephemeral: effectiveEphemeral, components: components,
                allowedMentions: finalAllowedMentions, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    private async Task RespondOrFollowupErrorAsync(string errorMessage)
    {
        await RespondOrFollowupAsync(errorMessage, isError: true).ConfigureAwait(false);
    }
}